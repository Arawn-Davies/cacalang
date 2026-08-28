using Caca.Diagnostics;
using Caca.Runtime;

namespace Caca.Tests;

public class FloatTests
{
    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("1.0", "1.0")]
    [InlineData("0.5 + 0.25", "0.75")]
    [InlineData("1.5 * 2.0", "3.0")]
    [InlineData("7.0 / 2.0", "3.5")]
    [InlineData("7.5 % 2.0", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("2.0 - 3.0", "-1.0")]
    public void Float_arithmetic_evaluates(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Fact]
    public void An_integral_float_keeps_a_decimal_point_so_it_does_not_read_as_an_int()
    {
        Assert.Equal(TestHost.Lines("1.0", "1"), TestHost.Run("print 1.0; print 1;"));
    }

    [Theory]
    [InlineData("1 + 1.5", "2.5")]
    [InlineData("1.5 + 1", "2.5")]
    [InlineData("3 / 2.0", "1.5")]
    [InlineData("2 * 1.5", "3.0")]
    public void An_int_mixed_with_a_float_is_widened(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Fact]
    public void Integer_division_still_truncates()
    {
        // Widening one operand is what changes the answer, not the other way round.
        Assert.Equal(TestHost.Lines("3", "3.5"), TestHost.Run("print 7 / 2; print 7 / 2.0;"));
    }

    [Theory]
    [InlineData("1.5 < 2.0", "true")]
    [InlineData("1.5 > 2", "false")]
    [InlineData("2.0 == 2", "true")]
    [InlineData("2.0 != 2", "false")]
    [InlineData("1.5 <= 1.5", "true")]
    [InlineData("1.5 >= 1.6", "false")]
    public void Floats_compare(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Fact]
    public void A_float_variable_can_be_declared_and_assigned()
    {
        Assert.Equal(TestHost.Lines("2.5", "4.0"), TestHost.Run("""
            var x = 2.5;
            print x;
            x = 4.0;
            print x;
            """));
    }

    [Fact]
    public void An_int_can_initialize_a_float_variable()
    {
        Assert.Equal(TestHost.Lines("2.0"), TestHost.Run("var x: float = 2; print x;"));
    }

    [Fact]
    public void A_float_cannot_be_stored_in_an_int_variable()
    {
        // Narrowing loses information, so it has to be asked for, and there is
        // no way to ask.
        TestHost.SingleError("var x = 1; x = 1.5;", DiagnosticCode.TypeMismatch);
        TestHost.SingleError("var x: int = 1.5;", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Functions_take_and_return_floats()
    {
        Assert.Equal(TestHost.Lines("2.5"), TestHost.Run("""
            func half(n: float): float do return n / 2.0; end
            print half(5.0);
            """));
    }

    [Fact]
    public void An_int_argument_is_widened_to_a_float_parameter()
    {
        Assert.Equal(TestHost.Lines("2.5"), TestHost.Run("""
            func half(n: float): float do return n / 2.0; end
            print half(5);
            """));
    }

    [Fact]
    public void An_int_can_be_returned_from_a_function_that_returns_a_float()
    {
        Assert.Equal(TestHost.Lines("3.0"), TestHost.Run("""
            func three(): float do return 3; end
            print three();
            """));
    }

    [Fact]
    public void A_float_argument_is_not_accepted_by_an_int_parameter()
    {
        TestHost.SingleError("""
            func double(n: int): int do return n * 2; end
            print double(1.5);
            """, DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Floats_concatenate_into_strings()
    {
        Assert.Equal(TestHost.Lines("pi is about 3.14"), TestHost.Run("""print "pi is about " + 3.14;"""));
    }

    [Fact]
    public void Dividing_a_float_by_zero_gives_an_infinity_rather_than_an_error()
    {
        // IEEE 754 defines this; integer division has no answer to give, which
        // is why that case still fails.
        Assert.Equal(TestHost.Lines("Infinity", "-Infinity"), TestHost.Run("""
            print 1.0 / 0.0;
            print -1.0 / 0.0;
            """));
    }

    [Fact]
    public void Integer_division_by_zero_is_still_an_error()
    {
        var compilation = Compilation.Create("var z = 0; print 1 / z;");

        Assert.Throws<CacaRuntimeException>(
            () => compilation.Run(new StringReader(string.Empty), new StringWriter()));
    }

    [Fact]
    public void Not_a_number_compares_false_against_everything_including_itself()
    {
        Assert.Equal(TestHost.Lines("NaN", "false", "false", "false"), TestHost.Run("""
            var nan = 0.0 / 0.0;
            print nan;
            print nan == nan;
            print nan < 1.0;
            print nan >= 1.0;
            """));
    }

    [Fact]
    public void A_loop_bound_must_still_be_an_int()
    {
        var errors = TestHost.Errors("for i = 1 to 3.5 do print i; end;");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.LoopBoundMustBeInt);
    }

    [Fact]
    public void Read_float_takes_a_number_from_input()
    {
        Assert.Equal(TestHost.Lines("3.0"), TestHost.Run("var x = 0.0; read_float x; print x * 2.0;", "1.5\n"));
    }

    [Fact]
    public void Read_float_rejects_input_that_is_not_a_number()
    {
        var compilation = Compilation.Create("var x = 0.0; read_float x;");
        var exception = Assert.Throws<CacaRuntimeException>(
            () => compilation.Run(new StringReader("banana\n"), new StringWriter()));

        Assert.Contains("not a number", exception.Message);
    }

    [Fact]
    public void Read_float_will_not_write_into_an_int_variable()
    {
        TestHost.SingleError("var x = 0; read_float x;", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void A_trailing_dot_is_not_part_of_a_number()
    {
        // `1.` would otherwise lex as a malformed float; the dot is left alone.
        var errors = TestHost.Errors("print 1.;");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_float_literal_that_is_too_large_is_reported()
    {
        TestHost.SingleError($"print {new string('9', 400)}.0;", DiagnosticCode.FloatOutOfRange);
    }

    [Fact]
    public void Logic_still_rejects_numbers()
    {
        TestHost.SingleError("print 1.5 && true;", DiagnosticCode.OperatorNotDefined);
    }
}
