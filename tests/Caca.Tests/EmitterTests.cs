using System.Reflection;
using System.Runtime.Loader;

namespace Caca.Tests;

/// <summary>
/// Exercises the IL backend by emitting a real assembly, loading it and running
/// its entry point in process. This is what proves the emitted IL is valid:
/// the runtime rejects a malformed method body when it is jitted.
/// </summary>
[Collection(ConsoleCollection.Name)]
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; failing to remove a temporary directory
            // must not fail the test that produced it.
        }
    }

    /// <summary>Compiles a program to an assembly, runs it, and returns its output.</summary>
    private string Emit(
        string source,
        string input = "",
        IReadOnlyList<Assembly>? references = null,
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var compilation = Compilation.Create(source, references: references);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var path = Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.exe");

        // These tests exercise the IL, so they skip the native launcher.
        var result = compilation.Emit(path, withLauncher: false);
        var assemblyPath = result.AssemblyPath;

        Assert.True(File.Exists(assemblyPath));
        Assert.True(
            File.Exists(result.RuntimeConfigPath),
            "the runtime configuration must be written next to the assembly");

        var context = new AssemblyLoadContext(name, isCollectible: true);

        try
        {
            // Loading from bytes rather than from the path leaves no file
            // mapping behind. AssemblyLoadContext.Unload is asynchronous, so on
            // Windows a mapped file is still locked when the directory is
            // removed, and the delete fails with UnauthorizedAccessException.
            var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(assemblyPath)));
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

    [Fact]
    public void Emitted_functions_take_arguments_and_return_values()
    {
        Assert.Equal(TestHost.Lines("7"), Emit("""
            func add(a: int, b: int): int do return a + b; end
            print add(3, 4);
            """));
    }

    [Fact]
    public void Emitted_functions_recurse()
    {
        Assert.Equal(TestHost.Lines("120"), Emit("""
            func factorial(n: int): int do
                if n <= 1 then return 1; end;
                return n * factorial(n - 1);
            end
            print factorial(5);
            """));
    }

    [Fact]
    public void Emitted_functions_recurse_mutually_regardless_of_declaration_order()
    {
        Assert.Equal(TestHost.Lines("true"), Emit("""
            func isEven(n: int): bool do
                if n == 0 then return true; end;
                return isOdd(n - 1);
            end
            func isOdd(n: int): bool do
                if n == 0 then return false; end;
                return isEven(n - 1);
            end
            print isEven(10);
            """));
    }

    [Fact]
    public void Emitted_void_function_is_called_as_a_statement()
    {
        Assert.Equal(TestHost.Lines("hello, world"), Emit("""
            func greet(name: string) do print "hello, " + name; end
            greet("world");
            """));
    }

    [Fact]
    public void Emitted_function_may_assign_to_its_parameters()
    {
        // Writing to a parameter emits starg rather than stloc.
        Assert.Equal(TestHost.Lines("20", "2"), Emit("""
            func tenTimes(n: int): int do n = n * 10; return n; end
            var x = 2;
            print tenTimes(x);
            print x;
            """));
    }

    [Fact]
    public void Emitted_function_returns_from_inside_a_loop()
    {
        Assert.Equal(TestHost.Lines("3"), Emit("""
            func firstMultipleOfThree(limit: int): int do
                for i = 1 to limit do
                    if i % 3 == 0 then return i; end;
                end;
                return 0;
            end
            print firstMultipleOfThree(10);
            """));
    }

    [Fact]
    public void Emitted_result_of_a_call_used_as_a_statement_is_discarded()
    {
        // The value has to be popped, or the method body ends with a full stack
        // and the runtime refuses to jit it.
        Assert.Equal(TestHost.Lines("called"), Emit("""
            func f(): int do print "called"; return 1; end
            f();
            """));
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("1.0", "1.0")]
    [InlineData("7.0 / 2.0", "3.5")]
    [InlineData("7.5 % 2.0", "1.5")]
    [InlineData("1 + 1.5", "2.5")]
    [InlineData("3 / 2.0", "1.5")]
    [InlineData("-2.5", "-2.5")]
    [InlineData("1.0 / 0.0", "Infinity")]
    [InlineData("0.0 / 0.0", "NaN")]
    public void Emitted_float_arithmetic_matches_the_interpreter(string expression, string expected)
    {
        // The formatting rule is generated into the assembly, since a compiled
        // program cannot call back into the compiler.
        Assert.Equal(TestHost.Lines(expected), Emit($"print {expression};"));
    }

    [Theory]
    [InlineData("1.5 < 2.0", "true")]
    [InlineData("1.5 <= 1.5", "true")]
    [InlineData("1.5 >= 1.6", "false")]
    [InlineData("2.0 == 2", "true")]
    [InlineData("2.0 != 2", "false")]
    public void Emitted_float_comparisons_match_the_interpreter(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), Emit($"print {expression};"));
    }

    [Fact]
    public void Emitted_comparisons_against_not_a_number_are_all_false()
    {
        // The inclusive comparisons are emitted as the unordered opposite,
        // negated; the ordered form would make every one of these true.
        Assert.Equal(TestHost.Lines("false", "false", "false", "false"), Emit("""
            var nan = 0.0 / 0.0;
            print nan < 1.0;
            print nan <= 1.0;
            print nan > 1.0;
            print nan >= 1.0;
            """));
    }

    [Fact]
    public void Emitted_functions_take_and_return_floats()
    {
        Assert.Equal(TestHost.Lines("2.5"), Emit("""
            func half(n: float): float do return n / 2.0; end
            print half(5);
            """));
    }

    [Fact]
    public void Emitted_floats_concatenate_into_strings()
    {
        Assert.Equal(TestHost.Lines("about 3.14 and 2.0"), Emit("""print "about " + 3.14 + " and " + 2.0;"""));
    }

    [Fact]
    public void Emitted_code_reads_a_float()
    {
        Assert.Equal(TestHost.Lines("3.0"), Emit("var x = 0.0; read_float x; print x * 2.0;", "1.5\n"));
    }

    [Fact]
    public void Emitted_extern_calls_bind_to_static_and_instance_methods()
    {
        Assert.Equal(TestHost.Lines("3.0", "HELLO"), Emit("""
            extern func sqrt(x: float): float from "System.Math.Sqrt";
            extern func shout(s: string): string from "System.String.ToUpperInvariant";
            print sqrt(9.0);
            print shout("hello");
            """));
    }

    [Fact]
    public void Emitted_extern_calls_a_referenced_assembly()
    {
        // The emitted assembly carries a reference to the C# library; running
        // it proves that reference resolves.
        Assert.Equal(
            TestHost.Lines("hello from C#, emitted", "42", "Hello, World from C#!"),
            Emit(
                """
                extern func greet(name: string): string from "Caca.ReferenceLibrary.Greetings.Greet";
                extern func triple(n: int): int from "Caca.ReferenceLibrary.Greetings.Triple";
                extern func say_hello() from "Caca.ReferenceLibrary.Greetings.SayHello";
                print greet("emitted");
                print triple(14);
                say_hello();
                """,
                references: [ExternTests.ReferenceLibrary]));
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
    [InlineData("func fib(n: int): int do if n < 2 then return n; end; return fib(n - 1) + fib(n - 2); end for i = 0 to 10 do print fib(i); end;", "")]
    [InlineData("func label(n: int): string do if n % 2 == 0 then return \"even\"; else return \"odd\"; end; end for i = 1 to 4 do print i + \" is \" + label(i); end;", "")]
    [InlineData("func shout(s: string) do print s + \"!\"; end var line = \"\"; read_string line; shout(line);", "hey\n")]
    [InlineData("var t = 0.0; for i = 1 to 5 do t = t + 1.0 / i; end; print t;", "")]
    [InlineData("func area(r: float): float do return 3.14159 * r * r; end for i = 1 to 3 do print area(i); end;", "")]
    [InlineData("print 0.1 + 0.2;", "")]
    [InlineData("extern func sqrt(x: float): float from \"System.Math.Sqrt\"; print sqrt(2.0);", "")]
    [InlineData("extern func max(a: int, b: int): int from \"System.Math.Max\"; print max(2, 9) + max(1, 3);", "")]
    [InlineData("extern func trim(s: string): string from \"System.String.Trim\"; var line = \"\"; read_string line; print trim(line) + \"!\";", "  hey  \n")]
    [InlineData("var s = \"\"; read_string s; print s + \"!\";", "")]
    [InlineData("extern func env(name: string): string from \"System.Environment.GetEnvironmentVariable\"; print env(\"CACA_TEST_UNSET_VARIABLE_XYZ\"); print \"done\";", "")]
    public void Both_backends_produce_the_same_output(string source, string input)
    {
        Assert.Equal(TestHost.Run(source, input), Emit(source, input));
    }
}
