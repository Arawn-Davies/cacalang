using System.Reflection;
using Caca.Diagnostics;

namespace Caca.Tests;

/// <summary>
/// Extern functions: binding to .NET methods, calling them through the
/// interpreter, and the errors a target that does not resolve produces.
/// </summary>
public class ExternTests
{
    [Fact]
    public void Extern_binds_a_static_method()
    {
        Assert.Equal(TestHost.Lines("7"), TestHost.Run("""
            extern func max(a: int, b: int): int from "System.Math.Max";
            print max(3, 7);
            """));
    }

    [Fact]
    public void Extern_binds_a_float_method()
    {
        Assert.Equal(TestHost.Lines("3.0"), TestHost.Run("""
            extern func sqrt(x: float): float from "System.Math.Sqrt";
            print sqrt(9.0);
            """));
    }

    [Fact]
    public void Extern_arguments_widen_like_any_call()
    {
        // An int argument to a float parameter converts, exactly as it does
        // for a function declared in the program.
        Assert.Equal(TestHost.Lines("4.0"), TestHost.Run("""
            extern func sqrt(x: float): float from "System.Math.Sqrt";
            print sqrt(16);
            """));
    }

    [Fact]
    public void Extern_binds_an_instance_method_through_its_first_parameter()
    {
        Assert.Equal(TestHost.Lines("HELLO", "ell", "2"), TestHost.Run("""
            extern func shout(s: string): string from "System.String.ToUpperInvariant";
            extern func substring(s: string, start: int, length: int): string from "System.String.Substring";
            extern func index_of(s: string, part: string): int from "System.String.IndexOf";
            print shout("hello");
            print substring("hello", 1, 3);
            print index_of("hello", "ll");
            """));
    }

    [Fact]
    public void Extern_binds_a_property_getter()
    {
        Assert.Equal(TestHost.Lines("5"), TestHost.Run("""
            extern func length(s: string): int from "System.String.get_Length";
            print length("hello");
            """));
    }

    [Fact]
    public void Extern_void_method_is_called_as_a_statement()
    {
        Assert.Equal(TestHost.Lines("done"), TestHost.Run("""
            extern func collect() from "System.GC.Collect";
            collect();
            print "done";
            """));
    }

    [Fact]
    public void Extern_declaration_may_follow_its_callers()
    {
        // Externs are collected in the same first pass as other functions.
        Assert.Equal(TestHost.Lines("5.0"), TestHost.Run("""
            func hypotenuse(a: float, b: float): float do return sqrt(a * a + b * b); end
            extern func sqrt(x: float): float from "System.Math.Sqrt";
            print hypotenuse(3.0, 4.0);
            """));
    }

    [Fact]
    public void Extern_overload_must_match_exactly_not_by_widening()
    {
        // Math.Sqrt takes a double. Reflection's default binder would accept
        // this declaration by implicitly widening the int, which the emitter
        // cannot honour: it would hand the method an integer's bits as a float.
        TestHost.SingleError(
            """extern func sqrt(x: int): float from "System.Math.Sqrt";""",
            DiagnosticCode.ExternTargetNotFound);
    }

    [Fact]
    public void Extern_null_return_prints_as_an_empty_line()
    {
        // GetEnvironmentVariable returns null for an unset name; the language
        // has no null, and printing it writes an empty line, as the emitted
        // program's Console.WriteLine does.
        Assert.Equal(TestHost.Lines("", "done"), TestHost.Run("""
            extern func env(name: string): string from "System.Environment.GetEnvironmentVariable";
            print env("CACA_TEST_UNSET_VARIABLE_XYZ");
            print "done";
            """));
    }

    [Fact]
    public void Extern_null_return_is_not_the_empty_string()
    {
        Assert.Equal(TestHost.Lines("false"), TestHost.Run("""
            extern func env(name: string): string from "System.Environment.GetEnvironmentVariable";
            print env("CACA_TEST_UNSET_VARIABLE_XYZ") == "";
            """));
    }

    [Fact]
    public void Extern_instance_call_on_a_null_receiver_is_a_runtime_error()
    {
        var compilation = Compilation.Create("""
            extern func env(name: string): string from "System.Environment.GetEnvironmentVariable";
            extern func length(s: string): int from "System.String.get_Length";
            print length(env("CACA_TEST_UNSET_VARIABLE_XYZ"));
            """);

        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));
        var exception = Assert.Throws<Runtime.CacaRuntimeException>(
            () => compilation.Run(new StringReader(string.Empty), new StringWriter()));

        Assert.Contains("null", exception.Message);
    }

    [Fact]
    public void From_remains_usable_as_an_identifier()
    {
        // `from` is contextual, recognized only inside an extern declaration,
        // so declaring it did not take the name away from existing programs.
        Assert.Equal(TestHost.Lines("3"), TestHost.Run("var from = 1; print from + 2;"));
    }

    [Fact]
    public void Extern_does_not_bind_to_assemblies_outside_the_runtime()
    {
        // FloatFormat is public in the compiler's own assembly, which is
        // loaded into this process. Resolution must not see it without an
        // explicit reference: the emitted program would reference an assembly
        // it is never given a copy of.
        TestHost.SingleError(
            """extern func fmt(x: float): string from "Caca.Runtime.FloatFormat.ToText";""",
            DiagnosticCode.ExternTargetNotFound);
    }

    [Fact]
    public void Extern_target_without_a_type_is_invalid()
    {
        var error = TestHost.SingleError(
            """extern func sqrt(x: float): float from "Sqrt";""",
            DiagnosticCode.ExternTargetInvalid);

        Assert.Contains("Namespace.Type.Method", error.Message);
    }

    [Fact]
    public void Extern_target_with_an_unknown_type_is_not_found()
    {
        var error = TestHost.SingleError(
            """extern func f(): int from "No.Such.Type.Method";""",
            DiagnosticCode.ExternTargetNotFound);

        Assert.Contains("No.Such.Type", error.Message);
    }

    [Fact]
    public void Extern_target_with_an_unknown_method_is_not_found()
    {
        var error = TestHost.SingleError(
            """extern func f(x: int): int from "System.Math.NoSuchMethod";""",
            DiagnosticCode.ExternTargetNotFound);

        Assert.Contains("NoSuchMethod", error.Message);
    }

    [Fact]
    public void Extern_target_with_the_wrong_parameter_types_is_not_found()
    {
        TestHost.SingleError(
            """extern func sqrt(x: string): string from "System.Math.Sqrt";""",
            DiagnosticCode.ExternTargetNotFound);
    }

    [Fact]
    public void Extern_return_type_must_match_the_bound_method()
    {
        var error = TestHost.SingleError(
            """extern func sqrt(x: float): int from "System.Math.Sqrt";""",
            DiagnosticCode.ExternReturnTypeMismatch);

        Assert.Contains("int", error.Message);
    }

    [Fact]
    public void Extern_does_not_bind_instance_methods_on_value_types()
    {
        // A value-type receiver would need its address rather than its value.
        TestHost.SingleError(
            """extern func text(x: int): string from "System.Int32.ToString";""",
            DiagnosticCode.ExternTargetNotFound);
    }

    [Fact]
    public void Extern_call_checks_its_arguments_like_any_call()
    {
        TestHost.SingleError(
            """
            extern func max(a: int, b: int): int from "System.Math.Max";
            print max(1);
            """,
            DiagnosticCode.WrongArgumentCount);
    }

    [Fact]
    public void Extern_without_a_target_string_is_a_parse_error()
    {
        var errors = TestHost.Errors("extern func f(): int do return 1; end");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void Extern_failure_is_a_runtime_error()
    {
        var compilation = Compilation.Create("""
            extern func parse(s: string): int from "System.Int32.Parse";
            print parse("not a number");
            """);

        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));
        var exception = Assert.Throws<Runtime.CacaRuntimeException>(
            () => compilation.Run(new StringReader(string.Empty), new StringWriter()));

        Assert.Contains("'parse' failed", exception.Message);
    }

    // ---------------------------------------------------- referenced assemblies

    /// <summary>The C# fixture library, loaded from beside the test assembly.</summary>
    internal static Assembly ReferenceLibrary { get; } =
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Caca.ReferenceLibrary.dll"));

    [Fact]
    public void Extern_binds_into_a_referenced_assembly()
    {
        var compilation = Compilation.Create(
            """
            extern func greet(name: string): string from "Caca.ReferenceLibrary.Greetings.Greet";
            extern func triple(n: int): int from "Caca.ReferenceLibrary.Greetings.Triple";
            print greet("caca");
            print triple(14);
            """,
            references: [ReferenceLibrary]);

        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader(string.Empty), output);
        Assert.Equal(TestHost.Lines("hello from C#, caca", "42"), output.ToString());
    }
}

/// <summary>
/// Extern tests that redirect the process-wide console: a bound .NET method
/// writes to <see cref="Console"/> itself, not to the interpreter's writer.
/// </summary>
[Collection(ConsoleCollection.Name)]
public class ExternConsoleTests
{
    [Fact]
    public void Extern_void_method_from_a_referenced_assembly_writes_to_the_console()
    {
        var compilation = Compilation.Create(
            """
            extern func say_hello() from "Caca.ReferenceLibrary.Greetings.SayHello";
            say_hello();
            """,
            references: [ExternTests.ReferenceLibrary]);

        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        var original = Console.Out;

        try
        {
            Console.SetOut(output);
            compilation.Run(new StringReader(string.Empty), output);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(TestHost.Lines("Hello, World from C#!"), output.ToString());
    }
}
