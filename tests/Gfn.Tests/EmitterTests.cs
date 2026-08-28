using System.Reflection;
using System.Runtime.Loader;

namespace Gfn.Tests;

/// <summary>
/// Exercises the IL backend by emitting a real assembly, loading it and running
/// its entry point in process. This is what proves the emitted IL is valid:
/// the runtime rejects a malformed method body when it is jitted.
/// </summary>
public class EmitterTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("gfn-emit-").FullName;

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
    public void Both_backends_produce_the_same_output(string source, string input)
    {
        Assert.Equal(TestHost.Run(source, input), Emit(source, input));
    }
}
