using System.Reflection;
using System.Runtime.Loader;

namespace Caca.Tests;

/// <summary>
/// Exercises the IL backend by emitting a real assembly, loading it and running
/// its entry point in process. This is what proves the emitted IL is valid:
/// the runtime rejects a malformed method body when it is jitted.
/// </summary>
public class EmitterTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("caca-emit-").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // The loaded assembly keeps a file handle on some platforms.
        }
    }

    /// <summary>Compiles a program to an assembly, runs it, and returns its output.</summary>
    private string Emit(string source, string input = "", [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var path = Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.dll");
        var (assemblyPath, configPath) = compilation.Emit(path);

        Assert.True(File.Exists(assemblyPath));
        Assert.True(File.Exists(configPath), "the runtime configuration must be written next to the assembly");

        var context = new AssemblyLoadContext(name, isCollectible: true);

        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var main = assembly.GetType("Program")?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(main);

            var output = new StringWriter { NewLine = "\n" };
            var originalOut = Console.Out;
            var originalIn = Console.In;

            try
            {
                Console.SetOut(output);
                Console.SetIn(new StringReader(input));
                main.Invoke(null, null);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetIn(originalIn);
            }

            return output.ToString();
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Emits_a_runnable_assembly()
    {
        Assert.Equal(TestHost.Lines("hello, world"), Emit("""print "hello, world";"""));
    }

    [Theory]
    [InlineData("10 - 2 - 3", "5")]
    [InlineData("10 / 2 * 5", "25")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("-5 + 3", "-2")]
    public void Emitted_arithmetic_matches_the_language_semantics(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), Emit($"print {expression};"));
    }

    [Fact]
    public void Emitted_code_prints_an_arithmetic_expression()
    {
        // This program used to produce IL the runtime refused to jit.
        Assert.Equal(TestHost.Lines("2"), Emit("var x = 1; print x + 1;"));
    }

    [Fact]
    public void Emitted_code_concatenates_strings()
    {
        Assert.Equal(TestHost.Lines("count: 42"), Emit("""var n = 42; print "count: " + n;"""));
    }

    [Fact]
    public void Emitted_loop_bound_is_inclusive()
    {
        Assert.Equal(TestHost.Lines("1", "2", "3"), Emit("for i = 1 to 3 do print i; end;"));
    }

    [Fact]
    public void Emitted_loop_evaluates_its_upper_bound_once()
    {
        Assert.Equal(TestHost.Lines("1", "2", "3"), Emit("var n = 3; for i = 1 to n do print i; n = 99; end;"));
    }

    [Fact]
    public void Emitted_loop_with_reversed_bounds_never_runs()
    {
        Assert.Equal(TestHost.Lines("done"), Emit("""for i = 5 to 1 do print i; end; print "done";"""));
    }

    [Fact]
    public void Emitted_code_reads_input()
    {
        Assert.Equal(TestHost.Lines("43"), Emit("var x = 0; read_int x; print x + 1;", "42\n"));
    }

    [Theory]
    [InlineData("1 < 2", "true")]
    [InlineData("2 <= 2", "true")]
    [InlineData("3 > 4", "false")]
    [InlineData("3 >= 3", "true")]
    [InlineData("2 == 2", "true")]
    [InlineData("2 != 2", "false")]
    [InlineData(@"""a"" == ""a""", "true")]
    [InlineData(@"""a"" != ""b""", "true")]
    [InlineData("!true", "false")]
    [InlineData("true && false", "false")]
    [InlineData("false || true", "true")]
    [InlineData("7 % 3", "1")]
    public void Emitted_comparisons_and_logic_evaluate(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), Emit($"print {expression};"));
    }

    [Fact]
    public void Emitted_logical_operators_short_circuit()
    {
        // If the right operand were evaluated the emitted code would divide by
        // zero and the runtime would raise DivideByZeroException.
        Assert.Equal(TestHost.Lines("false"), Emit("var z = 0; print z != 0 && 10 / z > 1;"));
    }

    [Fact]
    public void Emitted_if_else_selects_a_branch()
    {
        Assert.Equal(TestHost.Lines("no"), Emit("""if 1 > 2 then print "yes"; else print "no"; end;"""));
    }

    [Fact]
    public void Emitted_else_if_chain_selects_the_right_arm()
    {
        const string source = """
            var n = 0;
            read_int n;
            if n < 0 then print "negative";
            else if n == 0 then print "zero";
            else print "positive"; end;
            """;

        Assert.Equal(TestHost.Lines("zero"), Emit(source, "0\n"));
    }

    [Fact]
    public void Emitted_while_loop_runs()
    {
        Assert.Equal(TestHost.Lines("3", "2", "1"), Emit("""
            var n = 3;
            while n > 0 do print n; n = n - 1; end;
            """));
    }

    [Fact]
    public void Emitted_break_and_continue_work_in_both_loop_forms()
    {
        Assert.Equal(TestHost.Lines("1", "3"), Emit("""
            for i = 1 to 10 do
                if i > 3 then break; end;
                if i % 2 == 0 then continue; end;
                print i;
            end;
            """));

        Assert.Equal(TestHost.Lines("1", "3"), Emit("""
            var i = 0;
            while true do
                i = i + 1;
                if i > 3 then break; end;
                if i % 2 == 0 then continue; end;
                print i;
            end;
            """));
    }

    [Fact]
    public void Emitted_booleans_print_the_same_way_the_interpreter_prints_them()
    {
        // Console.WriteLine(bool) would print "True"; the language prints "true".
        Assert.Equal(TestHost.Lines("true", "ready: false"), Emit("""
            print 1 < 2;
            print "ready: " + (1 > 2);
            """));
    }

    /// <summary>
    /// The two backends must agree; this is the cheapest way to keep them from
    /// drifting apart as the language grows.
    /// </summary>
    [Theory]
    [InlineData("var x = 2; var y = 4; print y / x;", "")]
    [InlineData("for i = 1 to 4 do print i * i; end;", "")]
    [InlineData("""var n = 0; read_int n; for i = 1 to n do print "tick " + i; end;""", "3\n")]
    [InlineData("""var s = ""; read_string s; print s + "!";""", "hi\n")]
    [InlineData("print (1 + 2) * (3 - 4) / 1;", "")]
    [InlineData("var n = 0; read_int n; if n % 2 == 0 then print \"even\"; else print \"odd\"; end;", "6\n")]
    [InlineData("var i = 1; while i <= 5 do if i == 3 then i = i + 1; continue; end; print i * i; i = i + 1; end;", "")]
    [InlineData("for i = 1 to 20 do if i > 4 then break; end; print i >= 2 && i <= 3; end;", "")]
    public void Both_backends_produce_the_same_output(string source, string input)
    {
        Assert.Equal(TestHost.Run(source, input), Emit(source, input));
    }
}
