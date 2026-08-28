using Caca.Runtime;

namespace Caca.Tests;

public class InterpreterTests
{
    [Fact]
    public void Variables_are_declared_assigned_and_printed()
    {
        var output = TestHost.Run("""
            var x = 2;
            var y = 4;
            var z = y / x;
            print z;
            """);

        Assert.Equal(TestHost.Lines("2"), output);
    }

    [Fact]
    public void Printing_an_arithmetic_expression_works()
    {
        // Previously this emitted invalid IL and printed garbage.
        Assert.Equal(TestHost.Lines("2"), TestHost.Run("var x = 1; print x + 1;"));
    }

    [Fact]
    public void Strings_concatenate_with_plus()
    {
        Assert.Equal(TestHost.Lines("ab"), TestHost.Run("""var s = "a"; print s + "b";"""));
    }

    [Fact]
    public void Concatenating_a_string_with_an_int_converts_the_int()
    {
        Assert.Equal(
            TestHost.Lines("count: 42"),
            TestHost.Run("""var n = 42; print "count: " + n;"""));
    }

    [Fact]
    public void For_loop_bound_is_inclusive()
    {
        // `for x = 1 to 3` used to run twice, which the original source flagged
        // with a TODO in the parser.
        Assert.Equal(TestHost.Lines("1", "2", "3"), TestHost.Run("for i = 1 to 3 do print i; end;"));
    }

    [Fact]
    public void A_loop_declares_its_variable_when_it_is_new()
    {
        Assert.Equal(TestHost.Lines("1"), TestHost.Run("for i = 1 to 1 do print i; end;"));
    }

    [Fact]
    public void A_loop_reuses_an_existing_variable_and_leaves_it_visible()
    {
        Assert.Equal(
            TestHost.Lines("1", "2", "2"),
            TestHost.Run("var i = 0; for i = 1 to 2 do print i; end; print i;"));
    }

    [Fact]
    public void A_loop_whose_bounds_are_reversed_never_runs()
    {
        Assert.Equal(TestHost.Lines("done"), TestHost.Run("""
            for i = 5 to 1 do print i; end;
            print "done";
            """));
    }

    [Fact]
    public void The_upper_bound_is_evaluated_once()
    {
        Assert.Equal(
            TestHost.Lines("1", "2", "3"),
            TestHost.Run("var n = 3; for i = 1 to n do print i; n = 99; end;"));
    }

    [Fact]
    public void Loops_nest()
    {
        Assert.Equal(
            TestHost.Lines("11", "12", "21", "22"),
            TestHost.Run("""
                for i = 1 to 2 do
                    for j = 1 to 2 do
                        print i * 10 + j;
                    end;
                end;
                """));
    }

    [Fact]
    public void Read_int_parses_a_line_of_input()
    {
        Assert.Equal(TestHost.Lines("43"), TestHost.Run("var x = 0; read_int x; print x + 1;", "42\n"));
    }

    [Fact]
    public void Read_string_takes_a_whole_line()
    {
        Assert.Equal(
            TestHost.Lines("hello there"),
            TestHost.Run("""var s = ""; read_string s; print s;""", "hello there\n"));
    }

    [Fact]
    public void Read_int_on_non_numeric_input_reports_a_language_error()
    {
        var compilation = Compilation.Create("var x = 0; read_int x;");
        var exception = Assert.Throws<CacaRuntimeException>(
            () => compilation.Run(new StringReader("banana\n"), new StringWriter()));

        Assert.Contains("not an integer", exception.Message);
    }

    [Fact]
    public void Division_by_zero_reports_a_language_error()
    {
        var compilation = Compilation.Create("var a = 5; var b = 0; print a / b;");
        var exception = Assert.Throws<CacaRuntimeException>(
            () => compilation.Run(new StringReader(string.Empty), new StringWriter()));

        Assert.Contains("divide by zero", exception.Message);
    }

    [Fact]
    public void Printing_is_culture_independent()
    {
        Assert.Equal(TestHost.Lines("-1234"), TestHost.Run("print -1234;"));
    }
}
