using System.Collections.Immutable;
using System.Diagnostics.SymbolStore;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Caca.Binding;
using Caca.Diagnostics;
using Caca.Syntax;

namespace Caca.Emit;

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
    /// <summary>
    /// Identifies the language in the debugging information. Debuggers that do
    /// not know it fall back to showing the file as text, which is all this
    /// language needs.
    /// </summary>
    /// <summary>The version of the portable PDB format written.</summary>
    private const ushort PortablePdbVersion = 0x0100;

    /// <summary>
    /// The generated float formatter. The angle brackets keep it from colliding
    /// with any function a program could declare.
    /// </summary>
    private const string FloatFormatterName = "<>FormatFloat";

    private static readonly Guid LanguageGuid = new("ca6a1a19-cafe-4caa-9a1a-cacaca1a4caa");

    private static readonly MethodInfo ConsoleWriteLineString =
        typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!;

    private static readonly MethodInfo ConsoleWriteLineInt =
        typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(int)])!;

    private static readonly MethodInfo ConsoleReadLine =
        typeof(Console).GetMethod(nameof(Console.ReadLine), Type.EmptyTypes)!;

    private static readonly MethodInfo StringEquals =
        typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo StringConcat =
        typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo IntToStringInvariant =
        typeof(int).GetMethod(nameof(int.ToString), [typeof(IFormatProvider)])!;

    private static readonly MethodInfo FloatToStringInvariant =
        typeof(double).GetMethod(nameof(double.ToString), [typeof(IFormatProvider)])!;

    private static readonly MethodInfo FloatIsFinite =
        typeof(double).GetMethod(nameof(double.IsFinite), [typeof(double)])!;

    private static readonly MethodInfo StringContainsChar =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(char)])!;

    private static readonly MethodInfo IntParseInvariant =
        typeof(int).GetMethod(nameof(int.Parse), [typeof(string), typeof(IFormatProvider)])!;

    private static readonly MethodInfo FloatParseInvariant =
        typeof(double).GetMethod(nameof(double.Parse), [typeof(string), typeof(IFormatProvider)])!;

    private static readonly MethodInfo InvariantCultureGetter =
        typeof(CultureInfo).GetProperty(nameof(CultureInfo.InvariantCulture))!.GetGetMethod()!;

    private readonly Dictionary<string, LocalBuilder> _locals = new(StringComparer.Ordinal);

    /// <summary>Parameters of the method being emitted, mapped to their argument index.</summary>
    private readonly Dictionary<string, int> _parameters = new(StringComparer.Ordinal);

    /// <summary>Every function in the program, so that a call can reference one.</summary>
    private readonly IReadOnlyDictionary<string, MethodBuilder> _methods;

    /// <summary>
    /// The <c>break</c> and <c>continue</c> targets of the enclosing loops,
    /// innermost last.
    /// </summary>
    private readonly Stack<(Label Break, Label Continue)> _loops = new();

    private readonly ILGenerator _il;

    /// <summary>The source file, or null when no debugging information is wanted.</summary>
    private readonly ISymbolDocumentWriter? _document;

    /// <summary>The generated method that renders a float as text.</summary>
    private readonly MethodBuilder _floatFormatter;

    private IlEmitter(
        ILGenerator il,
        IReadOnlyDictionary<string, MethodBuilder> methods,
        ISymbolDocumentWriter? document,
        MethodBuilder floatFormatter)
    {
        _il = il;
        _methods = methods;
        _document = document;
        _floatFormatter = floatFormatter;
    }

    /// <summary>
    /// Records that the instructions about to be emitted come from a particular
    /// piece of source, which is what lets a debugger step through a .caca file.
    /// </summary>
    private void MarkPosition(SourceLocation location)
    {
        if (_document is null || location.Line == 0)
        {
            return;
        }

        _il.MarkSequencePoint(_document, location.Line, location.Column, location.EndLine, location.EndColumn);
    }

    /// <summary>
    /// Writes <paramref name="program"/> to <paramref name="outputPath"/> as a
    /// runnable .NET assembly, along with the <c>.runtimeconfig.json</c> the
    /// host needs to launch it.
    /// </summary>
    /// <returns>The path of the generated runtime configuration file.</returns>
    public static string EmitAssembly(
        CompilationUnit unit,
        IReadOnlyDictionary<string, FunctionSymbol> functions,
        string outputPath,
        string? sourcePath = null)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(outputPath);

        if (string.IsNullOrEmpty(assemblyName))
        {
            throw new ArgumentException("the output path must name a file", nameof(outputPath));
        }

        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);
        var programType = module.DefineType("Program", TypeAttributes.Public | TypeAttributes.Sealed);

        // Naming the source file is what ties the emitted instructions back to
        // the text they came from.
        var document = sourcePath is null
            ? null
            : module.DefineDocument(Path.GetFullPath(sourcePath), LanguageGuid);

        // Every method is declared before any body is emitted, so a call can
        // reference a function declared later in the file, or the one it is in.
        var methods = DeclareMethods(programType, functions);
        var floatFormatter = EmitFloatFormatter(programType);

        foreach (var (name, method) in methods)
        {
            var function = functions[name];
            var emitter = new IlEmitter(method.GetILGenerator(), methods, document, floatFormatter);

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                emitter._parameters[function.Parameters[i].Name] = i;
            }

            emitter.EmitStatement(function.Declaration.Body);
            emitter.EmitDefaultReturn(function.ReturnType);
        }

        var main = programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        var mainEmitter = new IlEmitter(main.GetILGenerator(), methods, document, floatFormatter);
        mainEmitter.EmitStatement(unit.TopLevel);
        mainEmitter._il.Emit(OpCodes.Ret);

        programType.CreateType();

        WritePortableExecutable(builder, main, outputPath, withDebugInfo: document is not null);
        return WriteRuntimeConfig(outputPath);
    }

    /// <summary>
    /// Emits a method that renders a float exactly as the interpreter does.
    /// </summary>
    /// <remarks>
    /// The rule is "the shortest round-trippable form, with a trailing .0 when
    /// that form has neither a point nor an exponent", so that 1.0 prints as
    /// "1.0" rather than as "1", which reads as an int. A compiled program
    /// cannot call into this compiler, so the rule is emitted alongside it.
    /// </remarks>
    private static MethodBuilder EmitFloatFormatter(TypeBuilder programType)
    {
        var method = programType.DefineMethod(
            FloatFormatterName,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(string),
            [typeof(double)]);

        var il = method.GetILGenerator();
        var text = il.DeclareLocal(typeof(string));
        var asIs = il.DefineLabel();

        // text = value.ToString(CultureInfo.InvariantCulture)
        il.Emit(OpCodes.Ldarga_S, (byte)0);
        il.Emit(OpCodes.Call, InvariantCultureGetter);
        il.Emit(OpCodes.Call, FloatToStringInvariant);
        il.Emit(OpCodes.Stloc, text);

        // Infinities and NaN are written as they are.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, FloatIsFinite);
        il.Emit(OpCodes.Brfalse, asIs);

        foreach (var character in (char[])['.', 'E', 'e'])
        {
            il.Emit(OpCodes.Ldloc, text);
            il.Emit(OpCodes.Ldc_I4, character);
            il.Emit(OpCodes.Call, StringContainsChar);
            il.Emit(OpCodes.Brtrue, asIs);
        }

        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Ldstr, ".0");
        il.Emit(OpCodes.Call, StringConcat);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(asIs);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static Dictionary<string, MethodBuilder> DeclareMethods(
        TypeBuilder programType,
        IReadOnlyDictionary<string, FunctionSymbol> functions)
    {
        var methods = new Dictionary<string, MethodBuilder>(StringComparer.Ordinal);

        foreach (var (name, function) in functions)
        {
            var parameterTypes = function.Parameters.Select(p => p.Type.ToClrType()).ToArray();

            var method = programType.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                function.ReturnType.ToClrType(),
                parameterTypes);

            // Named parameters make the emitted assembly readable in a decompiler.
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                method.DefineParameter(i + 1, ParameterAttributes.None, function.Parameters[i].Name);
            }

            methods[name] = method;
        }

        return methods;
    }

    /// <summary>
    /// Closes a method body with a return.
    /// </summary>
    /// <remarks>
    /// The type checker has already proved that a function owing a value
    /// returns on every path, so for those this epilogue is unreachable. It is
    /// emitted anyway because a method body must end in a valid instruction.
    /// </remarks>
    private void EmitDefaultReturn(CacaType returnType)
    {
        switch (returnType)
        {
            case CacaType.Int:
            case CacaType.Bool:
                _il.Emit(OpCodes.Ldc_I4_0);
                break;

            case CacaType.Float:
                _il.Emit(OpCodes.Ldc_R8, 0d);
                break;

            case CacaType.String:
                _il.Emit(OpCodes.Ldnull);
                break;
        }

        _il.Emit(OpCodes.Ret);
    }

    private static void WritePortableExecutable(
        PersistedAssemblyBuilder builder,
        MethodBuilder entryPoint,
        string outputPath,
        bool withDebugInfo)
    {
        var metadata = builder.GenerateMetadata(out var ilStream, out var mappedFieldData, out var pdbMetadata);

        // A method's token is only assigned while the metadata is generated, so
        // the entry point cannot be resolved before that call.
        var entryPointHandle = MetadataTokens.MethodDefinitionHandle(entryPoint.MetadataToken);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // The debug directory is what tells a debugger the symbols exist and
        // where to look for them.
        var debugDirectory = withDebugInfo
            ? WriteProgramDatabase(pdbMetadata, metadata.GetRowCounts(), entryPointHandle, outputPath)
            : null;

        var peBuilder = new ManagedPEBuilder(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: mappedFieldData,
            debugDirectoryBuilder: debugDirectory,
            entryPoint: entryPointHandle);

        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        blob.WriteContentTo(stream);
    }

    /// <summary>
    /// Writes a portable PDB beside the assembly and describes it for the
    /// assembly's debug directory.
    /// </summary>
    /// <remarks>
    /// The sequence points and local names recorded while emitting have already
    /// been collected into <paramref name="pdbMetadata"/>; what remains is to
    /// serialize them and to give the pair a shared identifier so a debugger can
    /// tell that the symbols belong to this build of the assembly.
    /// </remarks>
    private static DebugDirectoryBuilder WriteProgramDatabase(
        MetadataBuilder pdbMetadata,
        ImmutableArray<int> typeSystemRowCounts,
        MethodDefinitionHandle entryPointHandle,
        string outputPath)
    {
        var pdbPath = Path.ChangeExtension(outputPath, ".pdb");

        // The identifier must be the same in both files but need not be random:
        // deriving it from the content keeps a build reproducible.
        var contentId = new BlobContentId(HashOf(pdbMetadata, outputPath), stamp: 0x5CACA000);

        var pdbBuilder = new PortablePdbBuilder(
            pdbMetadata,
            typeSystemRowCounts,
            entryPointHandle,
            _ => contentId);

        var pdbBlob = new BlobBuilder();
        pdbBuilder.Serialize(pdbBlob);

        using (var pdbStream = new FileStream(pdbPath, FileMode.Create, FileAccess.Write))
        {
            pdbBlob.WriteContentTo(pdbStream);
        }

        var debugDirectory = new DebugDirectoryBuilder();
        debugDirectory.AddCodeViewEntry(pdbPath, contentId, portablePdbVersion: PortablePdbVersion);
        return debugDirectory;
    }

    /// <summary>A stable identifier for one build's symbols.</summary>
    private static Guid HashOf(MetadataBuilder pdbMetadata, string outputPath)
    {
        var counts = pdbMetadata.GetRowCounts();
        var seed = new StringBuilder(Path.GetFileName(outputPath));

        foreach (var count in counts)
        {
            seed.Append(';').Append(count);
        }

        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString()));
        return new Guid(digest.AsSpan(0, 16));
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
                MarkPosition(declaration.Location);
                Declare(declaration.Name, declaration.Initializer.Type);
                EmitExpression(declaration.Initializer);
                Store(declaration.Name);
                break;

            case AssignmentStatement assignment:
                MarkPosition(assignment.Location);
                EmitExpression(assignment.Value);
                Store(assignment.Name);
                break;

            case PrintStatement print:
                MarkPosition(print.Location);
                EmitPrint(print);
                break;

            case ReadStatement read:
                MarkPosition(read.Location);
                EmitRead(read);
                break;

            case ForStatement loop:
                EmitFor(loop);
                break;

            case IfStatement conditional:
                EmitIf(conditional);
                break;

            case WhileStatement loop:
                EmitWhile(loop);
                break;

            case ReturnStatement returned:
                MarkPosition(returned.Location);

                if (returned.Value is not null)
                {
                    EmitExpression(returned.Value);
                }

                _il.Emit(OpCodes.Ret);
                break;

            case CallStatement call:
                MarkPosition(call.Location);
                EmitCall(call.Call);

                // The result of a call used as a statement is discarded.
                if (call.Call.Type != CacaType.Void)
                {
                    _il.Emit(OpCodes.Pop);
                }

                break;

            case BreakStatement:
                MarkPosition(statement.Location);
                _il.Emit(OpCodes.Br, _loops.Peek().Break);
                break;

            case ContinueStatement:
                MarkPosition(statement.Location);
                _il.Emit(OpCodes.Br, _loops.Peek().Continue);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void EmitPrint(PrintStatement print)
    {
        // Console.WriteLine(double) formats differently from this language, and
        // WriteLine(bool) writes "True", so both go through the text form.
        if (print.Expression.EffectiveType is CacaType.Bool or CacaType.Float)
        {
            EmitAsString(print.Expression);
            _il.Emit(OpCodes.Call, ConsoleWriteLineString);
            return;
        }

        EmitExpression(print.Expression);

        // Call the overload matching the value's type instead of boxing every
        // int and calling object.ToString on it.
        _il.Emit(
            OpCodes.Call,
            print.Expression.EffectiveType == CacaType.Int ? ConsoleWriteLineInt : ConsoleWriteLineString);
    }

    private void EmitRead(ReadStatement read)
    {
        _il.Emit(OpCodes.Call, ConsoleReadLine);

        if (read.Type is CacaType.Int or CacaType.Float)
        {
            // Parse with the invariant culture so a program behaves the same
            // way regardless of the machine's regional settings.
            _il.Emit(OpCodes.Call, InvariantCultureGetter);
            _il.Emit(OpCodes.Call, read.Type == CacaType.Int ? IntParseInvariant : FloatParseInvariant);
        }

        Store(read.Name);
    }

    private void EmitIf(IfStatement conditional)
    {
        var otherwise = _il.DefineLabel();
        var exit = _il.DefineLabel();

        MarkPosition(conditional.Condition.Location);
        EmitExpression(conditional.Condition);
        _il.Emit(OpCodes.Brfalse, otherwise);
        EmitStatement(conditional.ThenBranch);

        if (conditional.ElseBranch is null)
        {
            _il.MarkLabel(otherwise);
            return;
        }

        _il.Emit(OpCodes.Br, exit);
        _il.MarkLabel(otherwise);
        EmitStatement(conditional.ElseBranch);
        _il.MarkLabel(exit);
    }

    private void EmitWhile(WhileStatement loop)
    {
        var body = _il.DefineLabel();
        var test = _il.DefineLabel();
        var exit = _il.DefineLabel();

        // The condition is emitted after the body and jumped to first, so each
        // iteration costs one branch rather than two.
        _il.Emit(OpCodes.Br, test);
        _il.MarkLabel(body);

        _loops.Push((Break: exit, Continue: test));
        EmitStatement(loop.Body);
        _loops.Pop();

        _il.MarkLabel(test);
        MarkPosition(loop.Condition.Location);
        EmitExpression(loop.Condition);
        _il.Emit(OpCodes.Brtrue, body);
        _il.MarkLabel(exit);
    }

    private void EmitFor(ForStatement loop)
    {
        if (loop.DeclaresVariable)
        {
            Declare(loop.Name, CacaType.Int);
        }

        var counter = _locals[loop.Name];
        var bound = _il.DeclareLocal(typeof(int));
        var body = _il.DefineLabel();
        var exit = _il.DefineLabel();

        // counter = from; bound = to  (the bound is evaluated exactly once)
        MarkPosition(loop.From.Location.To(loop.To.Location));
        EmitExpression(loop.From);
        _il.Emit(OpCodes.Stloc, counter);
        EmitExpression(loop.To);
        _il.Emit(OpCodes.Stloc, bound);

        // Skip the loop entirely when it would not run at all.
        _il.Emit(OpCodes.Ldloc, counter);
        _il.Emit(OpCodes.Ldloc, bound);
        _il.Emit(OpCodes.Bgt, exit);

        _il.MarkLabel(body);

        // `continue` resumes at the bound test that precedes the increment.
        var next = _il.DefineLabel();
        _loops.Push((Break: exit, Continue: next));
        EmitStatement(loop.Body);
        _loops.Pop();
        _il.MarkLabel(next);

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
        EmitExpressionCore(expression);

        // An int used where a float is wanted is converted here, once, exactly
        // where the type checker said it should be.
        if (expression.ConvertedTo == CacaType.Float && expression.Type == CacaType.Int)
        {
            _il.Emit(OpCodes.Conv_R8);
        }
    }

    private void EmitExpressionCore(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal when literal.LiteralType == CacaType.Int:
                _il.Emit(OpCodes.Ldc_I4, literal.IntValue);
                break;

            case LiteralExpression literal when literal.LiteralType == CacaType.Bool:
                _il.Emit(literal.BoolValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                break;

            case LiteralExpression literal when literal.LiteralType == CacaType.Float:
                _il.Emit(OpCodes.Ldc_R8, literal.FloatValue);
                break;

            case LiteralExpression literal:
                _il.Emit(OpCodes.Ldstr, literal.StringValue);
                break;

            case ParenthesizedExpression parenthesized:
                EmitExpression(parenthesized.Expression);
                break;

            case VariableExpression variable:
                EmitLoad(variable.Name);
                break;

            case CallExpression call:
                EmitCall(call);
                break;

            case UnaryExpression unary:
                EmitExpression(unary.Operand);

                switch (unary.Operator)
                {
                    case UnaryOperator.Negate:
                        // neg works for both integers and floats.
                        _il.Emit(OpCodes.Neg);
                        break;

                    case UnaryOperator.Not:
                        // There is no `not` for booleans; compare against false.
                        _il.Emit(OpCodes.Ldc_I4_0);
                        _il.Emit(OpCodes.Ceq);
                        break;
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
        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
            case BinaryOperator.LogicalOr:
                EmitShortCircuit(binary);
                return;

            case BinaryOperator.Add when binary.Type == CacaType.String:
                EmitAsString(binary.Left);
                EmitAsString(binary.Right);
                _il.Emit(OpCodes.Call, StringConcat);
                return;
        }

        EmitExpression(binary.Left);
        EmitExpression(binary.Right);

        switch (binary.Operator)
        {
            case BinaryOperator.Add:
                _il.Emit(OpCodes.Add);
                return;
            case BinaryOperator.Subtract:
                _il.Emit(OpCodes.Sub);
                return;
            case BinaryOperator.Multiply:
                _il.Emit(OpCodes.Mul);
                return;
            case BinaryOperator.Divide:
                _il.Emit(OpCodes.Div);
                return;
            case BinaryOperator.Modulo:
                _il.Emit(OpCodes.Rem);
                return;

            case BinaryOperator.Equal:
                EmitEquality(binary);
                return;
            case BinaryOperator.NotEqual:
                EmitEquality(binary);
                EmitNegate();
                return;

            // The CLR has clt and cgt but no cle or cge, so the inclusive forms
            // are the strict opposite, negated. For floats the opposite has to
            // be the unordered comparison, or every comparison against NaN
            // would come out true.
            case BinaryOperator.Less:
                _il.Emit(OpCodes.Clt);
                return;
            case BinaryOperator.Greater:
                _il.Emit(OpCodes.Cgt);
                return;
            case BinaryOperator.LessOrEqual:
                _il.Emit(IsFloatComparison(binary) ? OpCodes.Cgt_Un : OpCodes.Cgt);
                EmitNegate();
                return;
            case BinaryOperator.GreaterOrEqual:
                _il.Emit(IsFloatComparison(binary) ? OpCodes.Clt_Un : OpCodes.Clt);
                EmitNegate();
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator");
        }
    }

    /// <summary>Whether an operator's operands reach the stack as floats.</summary>
    private static bool IsFloatComparison(BinaryExpression binary) =>
        binary.Left.EffectiveType == CacaType.Float || binary.Right.EffectiveType == CacaType.Float;

    /// <summary>Compares the two values already on the stack.</summary>
    private void EmitEquality(BinaryExpression binary)
    {
        // Reference equality is not what `==` means for strings.
        if (binary.Left.EffectiveType == CacaType.String)
        {
            _il.Emit(OpCodes.Call, StringEquals);
            return;
        }

        _il.Emit(OpCodes.Ceq);
    }

    /// <summary>Turns the boolean on the stack into its opposite.</summary>
    private void EmitNegate()
    {
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
    }

    /// <summary>
    /// Emits <c>&amp;&amp;</c> or <c>||</c>, evaluating the right operand only
    /// when the left one has not already decided the result.
    /// </summary>
    private void EmitShortCircuit(BinaryExpression binary)
    {
        var shortCircuit = _il.DefineLabel();
        var exit = _il.DefineLabel();
        var isAnd = binary.Operator == BinaryOperator.LogicalAnd;

        EmitExpression(binary.Left);
        _il.Emit(isAnd ? OpCodes.Brfalse : OpCodes.Brtrue, shortCircuit);

        EmitExpression(binary.Right);
        _il.Emit(OpCodes.Br, exit);

        _il.MarkLabel(shortCircuit);
        _il.Emit(isAnd ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
        _il.MarkLabel(exit);
    }

    /// <summary>Emits an expression, converting a non-string result to its text form.</summary>
    private void EmitAsString(Expression expression)
    {
        EmitExpression(expression);

        switch (expression.EffectiveType)
        {
            case CacaType.Float:
                _il.Emit(OpCodes.Call, _floatFormatter);
                break;

            case CacaType.Int:
                // int.ToString needs a managed reference to the value, so spill
                // it to a temporary first.
                var temporary = _il.DeclareLocal(typeof(int));
                _il.Emit(OpCodes.Stloc, temporary);
                _il.Emit(OpCodes.Ldloca, temporary);
                _il.Emit(OpCodes.Call, InvariantCultureGetter);
                _il.Emit(OpCodes.Call, IntToStringInvariant);
                break;

            case CacaType.Bool:
                // bool.ToString yields "True"; the language prints "true".
                var whenTrue = _il.DefineLabel();
                var done = _il.DefineLabel();
                _il.Emit(OpCodes.Brtrue, whenTrue);
                _il.Emit(OpCodes.Ldstr, "false");
                _il.Emit(OpCodes.Br, done);
                _il.MarkLabel(whenTrue);
                _il.Emit(OpCodes.Ldstr, "true");
                _il.MarkLabel(done);
                break;
        }
    }

    private void EmitCall(CallExpression call)
    {
        foreach (var argument in call.Arguments)
        {
            EmitExpression(argument);
        }

        _il.Emit(OpCodes.Call, _methods[call.Name]);
    }

    private void Declare(string name, CacaType type)
    {
        var local = _il.DeclareLocal(type.ToClrType());

        // Without a name a debugger shows the variable as V_0.
        if (_document is not null)
        {
            local.SetLocalSymInfo(name);
        }

        _locals[name] = local;
    }

    /// <summary>Pushes a variable, which may be a local or one of the method's parameters.</summary>
    private void EmitLoad(string name)
    {
        if (_parameters.TryGetValue(name, out var index))
        {
            _il.Emit(OpCodes.Ldarg, index);
            return;
        }

        _il.Emit(OpCodes.Ldloc, _locals[name]);
    }

    private void Store(string name)
    {
        if (_parameters.TryGetValue(name, out var index))
        {
            _il.Emit(OpCodes.Starg, index);
            return;
        }

        _il.Emit(OpCodes.Stloc, _locals[name]);
    }
}
