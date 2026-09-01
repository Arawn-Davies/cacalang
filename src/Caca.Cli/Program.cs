// The original code was Copyright (c) Microsoft Corporation, published with
// "Create a Language Compiler for the .NET Framework" by Joel Pobar,
// MSDN Magazine, February 2008:
// https://learn.microsoft.com/en-us/archive/msdn-magazine/2008/february/create-a-language-compiler-for-the-net-framework-using-csharp

using System.Reflection;
using Caca;
using Caca.Runtime;

namespace Caca.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitCompileError = 1;
    private const int ExitRuntimeError = 2;
    private const int ExitUsageError = 64;

    private static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (CacaRuntimeException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitRuntimeError;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsageError;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsageError;
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage(Console.Out);
            return args.Length == 0 ? ExitUsageError : ExitSuccess;
        }

        if (args[0] is "--version")
        {
            Console.WriteLine($"caca {typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");
            return ExitSuccess;
        }

        // `caca program.caca` keeps working as a shorthand for `caca build`.
        var (command, rest) = args[0].StartsWith('-') || File.Exists(args[0]) && !IsCommand(args[0])
            ? ("build", args)
            : (args[0], args[1..]);

        return command switch
        {
            "repl" => new Repl().Run(),
            "run" => Run(rest),
            "build" => Build(rest),
            "check" => Check(rest),
            _ => UnknownCommand(command),
        };
    }

    private static bool IsCommand(string value) => value is "run" or "build" or "check" or "repl";

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintUsage(Console.Error);
        return ExitUsageError;
    }

    private static int Run(string[] args)
    {
        if (!TryTakeReferences(args, out var sources, out var references, out var status)
            || !TryCompile(sources, references, out var compilation, out status))
        {
            return status;
        }

        compilation.Run();
        return ExitSuccess;
    }

    private static int Check(string[] args)
    {
        if (!TryTakeReferences(args, out var sources, out var references, out var status)
            || !TryCompile(sources, references, out _, out status))
        {
            return status;
        }

        Console.WriteLine("No errors found.");
        return ExitSuccess;
    }

    /// <summary>
    /// Splits <c>--ref &lt;assembly&gt;</c> options out of <paramref name="args"/>,
    /// leaving everything else in <paramref name="rest"/>.
    /// </summary>
    private static bool TryTakeReferences(
        string[] args,
        out string[] rest,
        out List<string> references,
        out int status)
    {
        references = [];
        var remaining = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--ref" or "-r"))
            {
                remaining.Add(args[i]);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("error: '--ref' requires the path of an assembly");
                rest = [];
                status = ExitUsageError;
                return false;
            }

            references.Add(args[++i]);
        }

        rest = [.. remaining];
        status = ExitSuccess;
        return true;
    }

    private static int Build(string[] args)
    {
        if (!TryTakeReferences(args, out var rest, out var referencePaths, out var referenceStatus))
        {
            return referenceStatus;
        }

        string? outputPath = null;
        var withLauncher = true;
        var withDebugInfo = true;
        var positional = new List<string>();

        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] is "--no-launcher")
            {
                withLauncher = false;
            }
            else if (rest[i] is "--no-debug")
            {
                withDebugInfo = false;
            }
            else if (rest[i] is "-o" or "--output")
            {
                if (i + 1 >= rest.Length)
                {
                    Console.Error.WriteLine("error: '--output' requires a path");
                    return ExitUsageError;
                }

                outputPath = rest[++i];
            }
            else
            {
                positional.Add(rest[i]);
            }
        }

        if (!TryCompile([.. positional], referencePaths, out var compilation, out var status))
        {
            return status;
        }

        // The runnable file keeps the .exe name the language has always used,
        // on every platform. The assembly beside it must be a .dll.
        outputPath ??= Path.ChangeExtension(Path.GetFileName(positional[0]), ".exe");

        // Every referenced assembly is copied beside the output, so no name
        // may repeat: not the program's, and not another reference's. Compared
        // without case, because on a case-insensitive file system the copy
        // would silently overwrite the other file.
        var assemblyName = Path.GetFileName(Path.ChangeExtension(outputPath, ".dll"));
        var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyName };

        foreach (var reference in referencePaths)
        {
            if (!takenNames.Add(Path.GetFileName(reference)))
            {
                Console.Error.WriteLine(
                    $"error: two of the files written beside the output would both be named " +
                    $"'{Path.GetFileName(reference)}'; rename the output with -o, or the clashing assembly");
                return ExitUsageError;
            }
        }
        var sourcePath = withDebugInfo ? Path.GetFullPath(positional[0]) : null;
        var result = compilation.Emit(outputPath, withLauncher, sourcePath);

        Console.WriteLine($"Compiled {compilation.FileName} to {result.AssemblyPath}");

        if (withDebugInfo)
        {
            Console.WriteLine(
                $"Wrote {Path.GetFileName(Path.ChangeExtension(result.AssemblyPath, ".pdb"))} " +
                "so a debugger can step through the source");
        }

        // The compiled program's assembly references are resolved from its own
        // directory, so each referenced assembly is copied beside it.
        foreach (var reference in referencePaths)
        {
            var destination = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(result.AssemblyPath))!,
                Path.GetFileName(reference));

            // Compared without case: on a case-insensitive file system a
            // differently-spelled path can still be the destination itself,
            // and copying a file onto itself throws.
            if (!string.Equals(Path.GetFullPath(reference), destination, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(reference, destination, overwrite: true);
                Console.WriteLine($"Copied {Path.GetFileName(reference)} beside it so the program can load it");
            }
        }

        if (result.Warning is not null)
        {
            Console.Error.WriteLine($"warning: {result.Warning}");
        }

        if (result.LauncherPath is not null)
        {
            var command = Path.IsPathRooted(result.LauncherPath)
                ? result.LauncherPath
                : Path.Combine(".", result.LauncherPath);

            Console.WriteLine($"Wrote launcher {result.LauncherPath}; run it with: {command}");
        }
        else
        {
            Console.WriteLine($"Run it with: dotnet {result.AssemblyPath}");
        }

        return ExitSuccess;
    }

    private static bool TryCompile(
        string[] args,
        IReadOnlyList<string> referencePaths,
        out Compilation compilation,
        out int status)
    {
        compilation = null!;

        if (args.Length != 1)
        {
            Console.Error.WriteLine("error: expected exactly one source file");
            PrintUsage(Console.Error);
            status = ExitUsageError;
            return false;
        }

        var path = args[0];

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: no such file '{path}'");
            status = ExitUsageError;
            return false;
        }

        if (!TryLoadReferences(referencePaths, out var references, out status))
        {
            return false;
        }

        compilation = Compilation.CreateFromFile(path, references);

        if (!compilation.Succeeded)
        {
            foreach (var diagnostic in compilation.FormatDiagnostics())
            {
                Console.Error.WriteLine(diagnostic);
            }

            var count = compilation.Diagnostics.Count;
            Console.Error.WriteLine($"{count} error{(count == 1 ? string.Empty : "s")}.");
            status = ExitCompileError;
            return false;
        }

        status = ExitSuccess;
        return true;
    }

    /// <summary>Loads the assemblies named by <c>--ref</c> so extern functions can bind to them.</summary>
    private static bool TryLoadReferences(
        IReadOnlyList<string> paths,
        out List<Assembly> references,
        out int status)
    {
        references = [];

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"error: no such assembly '{path}'");
                status = ExitUsageError;
                return false;
            }

            try
            {
                // LoadFrom, rather than LoadFile, so an assembly that itself
                // depends on others in the same directory can find them.
                references.Add(Assembly.LoadFrom(Path.GetFullPath(path)));
            }
            catch (BadImageFormatException)
            {
                Console.Error.WriteLine($"error: '{path}' is not a .NET assembly");
                status = ExitUsageError;
                return false;
            }
            catch (FileLoadException exception)
            {
                Console.Error.WriteLine($"error: could not load '{path}': {exception.Message}");
                status = ExitUsageError;
                return false;
            }
        }

        status = ExitSuccess;
        return true;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
            cacalang - a small language that compiles to .NET

            Usage:
              caca repl                            Start an interactive prompt
              caca run <file.caca>                 Run a program with the interpreter
              caca build <file.caca> [-o <path>]   Compile a program to a runnable executable
              caca check <file.caca>               Report errors without running anything
              caca <file.caca>                     Shorthand for 'caca build'

            Options:
              -o, --output <path>   Where to write the executable (default: <file>.exe)
              -r, --ref <path>      A .NET assembly extern functions may bind to;
                                    repeat for more than one
                  --no-launcher     Emit only the assembly, to be run with 'dotnet'
                  --no-debug        Do not write debugging symbols
              -h, --help            Show this help
                  --version         Show the compiler version
            """);
    }
}
