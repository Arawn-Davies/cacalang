using System.Diagnostics;
using Caca.Diagnostics;
using Caca.Emit;

namespace Caca.Tests;

/// <summary>
/// A fact that only runs where a C compiler is available as <c>cc</c>.
/// </summary>
/// <remarks>
/// The C backend's output is proved the same way the IL backend's is: by
/// building and running it. That needs a C compiler, which most development
/// machines and CI images have and a bare environment may not; skipping is
/// more honest there than failing.
/// </remarks>
public sealed class CcFactAttribute : FactAttribute
{
    public CcFactAttribute()
    {
        if (!CEmitterTests.CcAvailable)
        {
            Skip = "no C compiler is available as 'cc'";
        }
    }
}

/// <summary>The same, for theories.</summary>
public sealed class CcTheoryAttribute : TheoryAttribute
{
    public CcTheoryAttribute()
    {
        if (!CEmitterTests.CcAvailable)
        {
            Skip = "no C compiler is available as 'cc'";
        }
    }
}

/// <summary>
/// Exercises the C backend by compiling its output with a real C compiler and
/// running it, which is what proves the generated C is valid.
/// </summary>
public class CEmitterTests : IDisposable
{
    internal static readonly bool CcAvailable = ProbeCc();

    private readonly string _directory =
        Directory.CreateTempSubdirectory("caca-c-").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; failing to remove a temporary directory
            // must not fail the test that produced it.
        }
    }

    private static bool ProbeCc()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("cc", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            probe!.WaitForExit();
            return probe.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Compiles a program to C, builds it with cc, runs it, and returns its output.</summary>
    private string EmitC(string source, string input = "", [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var cPath = Path.Combine(_directory, $"{name}.c");
        var binaryPath = Path.Combine(_directory, name);
        var diagnostics = compilation.EmitC(cPath);
        Assert.Empty(diagnostics);

        var compile = Run("cc", $"\"{cPath}\" -o \"{binaryPath}\"", input: null);
        Assert.True(compile.ExitCode == 0, $"cc failed:\n{compile.Output}\n{compile.Error}");

        var run = Run(binaryPath, string.Empty, input);
        return run.Output;
    }

    private static (int ExitCode, string Output, string Error) Run(string command, string arguments, string? input)
    {
        using var process = Process.Start(new ProcessStartInfo(command, arguments)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    [CcFact]
    public void C_backend_compiles_and_runs_hello_world()
    {
        Assert.Equal(TestHost.Lines("hello, world"), EmitC("""print "hello, world";"""));
    }

    /// <summary>
    /// The C backend must agree with the interpreter on every program; this
    /// mirrors the IL backend's parity suite, minus the extern cases, which
    /// the C target rejects.
    /// </summary>
    [CcTheory]
    [InlineData("var x = 2; var y = 4; print y / x;", "")]
    [InlineData("for i = 1 to 4 do print i * i; end;", "")]
    [InlineData("""var n = 0; read_int n; for i = 1 to n do print "tick " + i; end;""", "3\n")]
    [InlineData("""var s = ""; read_string s; print s + "!";""", "hi\n")]
    [InlineData("print (1 + 2) * (3 - 4) / 1;", "")]
    [InlineData("var n = 0; read_int n; if n % 2 == 0 then print \"even\"; else print \"odd\"; end;", "6\n")]
    [InlineData("var i = 1; while i <= 5 do if i == 3 then i = i + 1; continue; end; print i * i; i = i + 1; end;", "")]
    [InlineData("for i = 1 to 20 do if i > 4 then break; end; print i >= 2 && i <= 3; end;", "")]
    [InlineData("for i = 1 to 10 do if i % 2 == 0 then continue; end; print i; end;", "")]
    [InlineData("func fib(n: int): int do if n < 2 then return n; end; return fib(n - 1) + fib(n - 2); end for i = 0 to 10 do print fib(i); end;", "")]
    [InlineData("func label(n: int): string do if n % 2 == 0 then return \"even\"; else return \"odd\"; end; end for i = 1 to 4 do print i + \" is \" + label(i); end;", "")]
    [InlineData("func shout(s: string) do print s + \"!\"; end var line = \"\"; read_string line; shout(line);", "hey\n")]
    [InlineData("var t = 0.0; for i = 1 to 5 do t = t + 1.0 / i; end; print t;", "")]
    [InlineData("func area(r: float): float do return 3.14159 * r * r; end for i = 1 to 3 do print area(i); end;", "")]
    [InlineData("print 0.1 + 0.2;", "")]
    [InlineData("var s = \"\"; read_string s; print s + \"!\";", "")]
    [InlineData("print 1.0 / 0.0; print -1.0 / 0.0; print 0.0 / 0.0;", "")]
    [InlineData("print 1.5 < 2.0; print 2.0 == 2; print 0.0 / 0.0 <= 1.0;", "")]
    [InlineData("var half: float = 1; print half; print half + 0.5;", "")]
    [InlineData("print 2147483647 + 1; print -2147483647 - 2; print 65536 * 65536;", "")]
    [InlineData("print 7.5 % 2.0; print 7 % -3; print -7 % 3;", "")]
    [InlineData("var x = 100000.0; x = x * x; print x * x; print x * x * x;", "")]
    [InlineData("print 0.0001; print 0.0001 / 10.0; print 1.0 / 3.0;", "")]
    [InlineData("var read = 1; var from = 2; print read + from;", "")]
    [InlineData("""print "a" == "a"; print "a" != "b"; print "" == "";""", "")]
    public void C_backend_matches_the_interpreter(string source, string input)
    {
        Assert.Equal(TestHost.Run(source, input), EmitC(source, input));
    }

    [CcFact]
    public void C_backend_for_loop_reaching_the_largest_int_terminates()
    {
        Assert.Equal(TestHost.Lines("2147483646", "2147483647", "done"), EmitC("""
            for i = 2147483645 to 2147483646 do
                print i + 1;
            end;
            print "done";
            """));
    }

    [CcFact]
    public void C_backend_division_by_zero_is_a_runtime_error()
    {
        var compilation = Compilation.Create("var z = 0; print 1 / z;");
        Assert.True(compilation.Succeeded);

        var cPath = Path.Combine(_directory, "divzero.c");
        var binaryPath = Path.Combine(_directory, "divzero");
        Assert.Empty(compilation.EmitC(cPath));
        Assert.Equal(0, Run("cc", $"\"{cPath}\" -o \"{binaryPath}\"", null).ExitCode);

        var run = Run(binaryPath, string.Empty, string.Empty);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("attempted to divide by zero", run.Error);
    }

    [CcFact]
    public void C_backend_bad_read_input_is_a_runtime_error()
    {
        var compilation = Compilation.Create("var n = 0; read_int n; print n;");
        Assert.True(compilation.Succeeded);

        var cPath = Path.Combine(_directory, "badread.c");
        var binaryPath = Path.Combine(_directory, "badread");
        Assert.Empty(compilation.EmitC(cPath));
        Assert.Equal(0, Run("cc", $"\"{cPath}\" -o \"{binaryPath}\"", null).ExitCode);

        var run = Run(binaryPath, string.Empty, "not a number\n");
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("'not a number' is not an integer", run.Error);
    }

    [Fact]
    public void C_target_rejects_extern_functions()
    {
        // No process is started here, so no compiler is needed.
        var compilation = Compilation.Create(
            """extern func sqrt(x: float): float from "System.Math.Sqrt"; print sqrt(4.0);""");
        Assert.True(compilation.Succeeded);

        var diagnostics = CEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        var error = Assert.Single(diagnostics);

        Assert.Equal(DiagnosticCode.ExternNotAvailableInC, error.Code);
        Assert.Contains("sqrt", error.Message);
        Assert.Empty(text);
    }
}
