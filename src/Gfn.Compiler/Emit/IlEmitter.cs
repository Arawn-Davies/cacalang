using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Gfn.Binding;
using Gfn.Syntax;

namespace Gfn.Emit;

/// <summary>
/// Compiles a type-checked program to a .NET assembly on disk.
/// </summary>
/// <remarks>
/// <para>
/// The original emitter used <c>AppDomain.DefineDynamicAssembly</c> with
/// <c>AssemblyBuilderAccess.Save</c> and <c>AssemblyBuilder.Save</c>. None of
/// those exist outside .NET Framework, which is the single reason the project
/// could not be built or run anywhere but Windows.
/// </para>
/// <para>
/// <see cref="PersistedAssemblyBuilder"/> (.NET 9 and later) restores the
/// ability to write an assembly to disk, and <see cref="ManagedPEBuilder"/>
/// turns the emitted metadata into a portable executable.
/// </para>
/// </remarks>
public sealed class IlEmitter
{
    private static readonly MethodInfo ConsoleWriteLineString =
        typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!;

    private static readonly MethodInfo ConsoleWriteLineInt =
        typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(int)])!;

    private static readonly MethodInfo ConsoleReadLine =
        typeof(Console).GetMethod(nameof(Console.ReadLine), Type.EmptyTypes)!;

    private static readonly MethodInfo StringConcat =
        typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo IntToStringInvariant =
        typeof(int).GetMethod(nameof(int.ToString), [typeof(IFormatProvider)])!;

    private static readonly MethodInfo IntParseInvariant =
        typeof(int).GetMethod(nameof(int.Parse), [typeof(string), typeof(IFormatProvider)])!;

    private static readonly MethodInfo InvariantCultureGetter =
        typeof(CultureInfo).GetProperty(nameof(CultureInfo.InvariantCulture))!.GetGetMethod()!;

    private readonly Dictionary<string, LocalBuilder> _locals = new(StringComparer.Ordinal);
    private readonly ILGenerator _il;

    private IlEmitter(ILGenerator il) => _il = il;

    /// <summary>
    /// Writes <paramref name="program"/> to <paramref name="outputPath"/> as a
    /// runnable .NET assembly, along with the <c>.runtimeconfig.json</c> the
    /// host needs to launch it.
    /// </summary>
    /// <returns>The path of the generated runtime configuration file.</returns>
    public static string EmitAssembly(BlockStatement program, string outputPath)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(outputPath);

        if (string.IsNullOrEmpty(assemblyName))
        {
            throw new ArgumentException("the output path must name a file", nameof(outputPath));
        }

        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);
        var programType = module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Sealed);

        var main = programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        var emitter = new IlEmitter(main.GetILGenerator());
        emitter.EmitStatement(program);
        emitter._il.Emit(OpCodes.Ret);

        programType.CreateType();

        WritePortableExecutable(builder, main, outputPath);
        return WriteRuntimeConfig(outputPath);
    }

    private static void WritePortableExecutable(
        PersistedAssemblyBuilder builder,
        MethodBuilder entryPoint,
        string outputPath)
    {
        var metadata = builder.GenerateMetadata(out var ilStream, out var mappedFieldData);

        var peBuilder = new ManagedPEBuilder(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: mappedFieldData,
            entryPoint: MetadataTokens.MethodDefinitionHandle(entryPoint.MetadataToken));

        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        blob.WriteContentTo(stream);
    }

    /// <summary>
    /// Writes the runtime configuration next to the assembly. Without it the
    /// host cannot decide which framework to load and refuses to start.
    /// </summary>
    private static string WriteRuntimeConfig(string outputPath)
    {
        var version = Environment.Version;
        var configPath = Path.ChangeExtension(outputPath, ".runtimeconfig.json");

        var config = new
        {
            runtimeOptions = new
            {
                tfm = $"net{version.Major}.{version.Minor}",
                framework = new
                {
                    name = "Microsoft.NETCore.App",
                    version = $"{version.Major}.{version.Minor}.0",
                },
            },
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        return configPath;
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    EmitStatement(child);
                }

                break;

            case VariableDeclaration declaration:
                Declare(declaration.Name, declaration.Initializer.Type);
                EmitExpression(declaration.Initializer);
                Store(declaration.Name);
                break;

            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                Store(assignment.Name);
                break;

            case PrintStatement print:
                EmitPrint(print);
                break;

            case ReadStatement read:
                EmitRead(read);
                break;

            case ForStatement loop:
                EmitFor(loop);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void EmitPrint(PrintStatement print)
    {
        EmitExpression(print.Expression);

        // Call the overload matching the value's type instead of boxing every
        // int and calling object.ToString on it.
        _il.Emit(
            OpCodes.Call,
            print.Expression.Type == GfnType.Int ? ConsoleWriteLineInt : ConsoleWriteLineString);
    }

    private void EmitRead(ReadStatement read)
    {
        _il.Emit(OpCodes.Call, ConsoleReadLine);

        if (read.Type == GfnType.Int)
        {
            // Parse with the invariant culture so a program behaves the same
            // way regardless of the machine's regional settings.
            _il.Emit(OpCodes.Call, InvariantCultureGetter);
            _il.Emit(OpCodes.Call, IntParseInvariant);
        }

        Store(read.Name);
    }

    private void EmitFor(ForStatement loop)
    {
        if (loop.DeclaresVariable)
        {
            Declare(loop.Name, GfnType.Int);
        }

        var counter = _locals[loop.Name];
        var bound = _il.DeclareLocal(typeof(int));
        var body = _il.DefineLabel();
        var exit = _il.DefineLabel();

        // counter = from; bound = to  (the bound is evaluated exactly once)
        EmitExpression(loop.From);
        _il.Emit(OpCodes.Stloc, counter);
        EmitExpression(loop.To);
        _il.Emit(OpCodes.Stloc, bound);

        // Skip the loop entirely when it would not run at all.
        _il.Emit(OpCodes.Ldloc, counter);
        _il.Emit(OpCodes.Ldloc, bound);
        _il.Emit(OpCodes.Bgt, exit);

        _il.MarkLabel(body);
        EmitStatement(loop.Body);

        // `to` is inclusive, so the bound is tested before the increment. The
        // original emitted a `blt` after incrementing, which ran the body one
        // time too few, and would have wrapped around at int.MaxValue.
        _il.Emit(OpCodes.Ldloc, counter);
        _il.Emit(OpCodes.Ldloc, bound);
        _il.Emit(OpCodes.Bge, exit);

        _il.Emit(OpCodes.Ldloc, counter);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, counter);
        _il.Emit(OpCodes.Br, body);

        _il.MarkLabel(exit);
    }

    private void EmitExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal when literal.LiteralType == GfnType.Int:
                _il.Emit(OpCodes.Ldc_I4, literal.IntValue);
                break;

            case LiteralExpression literal:
                _il.Emit(OpCodes.Ldstr, literal.StringValue);
                break;

            case ParenthesizedExpression parenthesized:
                EmitExpression(parenthesized.Expression);
                break;

            case VariableExpression variable:
                _il.Emit(OpCodes.Ldloc, _locals[variable.Name]);
                break;

            case UnaryExpression unary:
                EmitExpression(unary.Operand);

                if (unary.Operator == UnaryOperator.Negate)
                {
                    _il.Emit(OpCodes.Neg);
                }

                break;

            case BinaryExpression binary:
                EmitBinary(binary);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(expression), expression, $"unhandled expression {expression.GetType().Name}");
        }
    }

    private void EmitBinary(BinaryExpression binary)
    {
        // Each operand is emitted at its own type. The original passed the
        // expression's expected type down to both operands, so `print x + 1`
        // converted the operands to string and then emitted an integer `add`
        // over them, producing IL the runtime rejects.
        if (binary.Type == GfnType.String)
        {
            EmitAsString(binary.Left);
            EmitAsString(binary.Right);
            _il.Emit(OpCodes.Call, StringConcat);
            return;
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);

        _il.Emit(binary.Operator switch
        {
            BinaryOperator.Add => OpCodes.Add,
            BinaryOperator.Subtract => OpCodes.Sub,
            BinaryOperator.Multiply => OpCodes.Mul,
            BinaryOperator.Divide => OpCodes.Div,
            _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator"),
        });
    }

    /// <summary>Emits an expression, converting an int result to its string form.</summary>
    private void EmitAsString(Expression expression)
    {
        EmitExpression(expression);

        if (expression.Type != GfnType.Int)
        {
            return;
        }

        // int.ToString needs a managed reference to the value, so spill it to a
        // temporary first.
        var temporary = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Stloc, temporary);
        _il.Emit(OpCodes.Ldloca, temporary);
        _il.Emit(OpCodes.Call, InvariantCultureGetter);
        _il.Emit(OpCodes.Call, IntToStringInvariant);
    }

    private void Declare(string name, GfnType type) =>
        _locals[name] = _il.DeclareLocal(type.ToClrType());

    private void Store(string name) => _il.Emit(OpCodes.Stloc, _locals[name]);
}
