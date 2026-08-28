using Caca.Binding;
using Caca.Diagnostics;
using Caca.Emit;
using Caca.Runtime;
using Caca.Syntax;

namespace Caca;

/// <summary>
/// The front end of the compiler: lexing, parsing and type checking, with the
/// resulting tree and diagnostics exposed for either backend to consume.
/// </summary>
public sealed class Compilation
{
    private Compilation(
        string? fileName,
        CompilationUnit program,
        BindingResult binding,
        DiagnosticBag diagnostics)
    {
        FileName = fileName;
        Program = program;
        Binding = binding;
        Diagnostics = diagnostics;
    }

    /// <summary>The file the source came from, used to prefix diagnostics.</summary>
    public string? FileName { get; }

    /// <summary>The parsed and type-checked program.</summary>
    public CompilationUnit Program { get; }

    /// <summary>What the type checker learned: the functions, and every name it resolved.</summary>
    public BindingResult Binding { get; }

    /// <summary>The functions the program declares, by name.</summary>
    public IReadOnlyDictionary<string, FunctionSymbol> Functions => Binding.Functions;

    public DiagnosticBag Diagnostics { get; }

    public bool Succeeded => !Diagnostics.HasErrors;

    /// <summary>Lexes, parses and type-checks <paramref name="text"/>.</summary>
    public static Compilation Create(string text, string? fileName = null)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = Lexer.Tokenize(text, diagnostics);
        var program = Parser.Parse(tokens, diagnostics);

        // Type checking assumes a well-formed tree, so it only runs once the
        // front end has produced one.
        var binding = diagnostics.HasErrors
            ? BindingResult.Empty
            : TypeChecker.Check(program, diagnostics);

        return new Compilation(fileName, program, binding, diagnostics);
    }

    public static Compilation CreateFromFile(string path) =>
        Create(File.ReadAllText(path), Path.GetFileName(path));

    /// <summary>Executes the program with the interpreter backend.</summary>
    /// <exception cref="InvalidOperationException">The program did not compile.</exception>
    /// <exception cref="CacaRuntimeException">The program failed while running.</exception>
    public void Run(TextReader? input = null, TextWriter? output = null)
    {
        EnsureSucceeded();
        new Interpreter(Functions, input, output).Run(Program);
    }

    /// <summary>Compiles the program to a .NET assembly at <paramref name="outputPath"/>.</summary>
    /// <returns>The paths of the assembly and its runtime configuration file.</returns>
    /// <exception cref="InvalidOperationException">The program did not compile.</exception>
    public (string AssemblyPath, string RuntimeConfigPath) Emit(string outputPath)
    {
        EnsureSucceeded();
        var configPath = IlEmitter.EmitAssembly(Program, Functions, outputPath);
        return (outputPath, configPath);
    }

    /// <summary>Formats every diagnostic, one per line, for display.</summary>
    public IEnumerable<string> FormatDiagnostics() => Diagnostics.Select(d => d.Format(FileName));

    private void EnsureSucceeded()
    {
        if (Diagnostics.HasErrors)
        {
            throw new InvalidOperationException("the program has errors and cannot be executed");
        }
    }
}
