using Caca.Diagnostics;

namespace Caca.Tests;

public class ControlFlowTests
{
    [Theory]
    [InlineData("1 < 2", "true")]
    [InlineData("2 < 1", "false")]
    [InlineData("2 <= 2", "true")]
    [InlineData("3 > 2", "true")]
    [InlineData("2 >= 3", "false")]
    [InlineData("2 == 2", "true")]
    [InlineData("2 != 2", "false")]
    [InlineData(@"""a"" == ""a""", "true")]
    [InlineData(@"""a"" == ""b""", "false")]
    [InlineData(@"""a"" != ""b""", "true")]
    [InlineData("true == true", "true")]
    [InlineData("true && false", "false")]
    [InlineData("true || false", "true")]
    [InlineData("!true", "false")]
    [InlineData("!(1 > 2)", "true")]
    [InlineData("1 < 2 && 2 < 3", "true")]
    [InlineData("1 + 1 == 2", "true")]
    [InlineData("7 % 3 == 1", "true")]
    public void Comparisons_and_logic_evaluate(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Theory]
    [InlineData("7 % 3", "1")]
    [InlineData("-7 % 3", "-1")]
    [InlineData("2 + 3 * 4 > 10 && 1 < 2", "true")]
    public void Modulo_and_precedence_across_the_new_operators(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Fact]
    public void If_takes_the_then_branch()
    {
        Assert.Equal(TestHost.Lines("yes"), TestHost.Run("""if 1 < 2 then print "yes"; end;"""));
    }

    [Fact]
    public void If_takes_the_else_branch()
    {
        Assert.Equal(
            TestHost.Lines("no"),
            TestHost.Run("""if 1 > 2 then print "yes"; else print "no"; end;"""));
    }

    [Fact]
    public void If_without_a_match_and_no_else_does_nothing()
    {
        Assert.Equal(TestHost.Lines("after"), TestHost.Run("""
            if false then print "never"; end;
            print "after";
            """));
    }

    [Fact]
    public void Else_if_chains_close_with_a_single_end()
    {
        const string source = """
            var n = 0;
            read_int n;
            if n < 0 then
                print "negative";
            else if n == 0 then
                print "zero";
            else
                print "positive";
            end;
            """;

        Assert.Equal(TestHost.Lines("negative"), TestHost.Run(source, "-5\n"));
        Assert.Equal(TestHost.Lines("zero"), TestHost.Run(source, "0\n"));
        Assert.Equal(TestHost.Lines("positive"), TestHost.Run(source, "7\n"));
    }

    [Fact]
    public void While_repeats_until_its_condition_fails()
    {
        Assert.Equal(TestHost.Lines("3", "2", "1"), TestHost.Run("""
            var n = 3;
            while n > 0 do
                print n;
                n = n - 1;
            end;
            """));
    }

    [Fact]
    public void While_whose_condition_is_false_never_runs()
    {
        Assert.Equal(string.Empty, TestHost.Run("while false do print 1; end;"));
    }

    [Fact]
    public void Break_leaves_a_while_loop()
    {
        Assert.Equal(TestHost.Lines("1", "2"), TestHost.Run("""
            var i = 0;
            while true do
                i = i + 1;
                print i;
                if i == 2 then break; end;
            end;
            """));
    }

    [Fact]
    public void Break_leaves_a_for_loop()
    {
        Assert.Equal(TestHost.Lines("1", "2"), TestHost.Run("""
            for i = 1 to 10 do
                print i;
                if i == 2 then break; end;
            end;
            """));
    }

    [Fact]
    public void Continue_skips_the_rest_of_a_for_iteration()
    {
        Assert.Equal(TestHost.Lines("1", "3", "5"), TestHost.Run("""
            for i = 1 to 5 do
                if i % 2 == 0 then continue; end;
                print i;
            end;
            """));
    }

    [Fact]
    public void Continue_returns_to_a_while_condition()
    {
        Assert.Equal(TestHost.Lines("1", "3"), TestHost.Run("""
            var i = 0;
            while i < 4 do
                i = i + 1;
                if i % 2 == 0 then continue; end;
                print i;
            end;
            """));
    }

    [Fact]
    public void Break_leaves_only_the_innermost_loop()
    {
        Assert.Equal(TestHost.Lines("1", "1", "2", "1", "2", "3"), TestHost.Run("""
            for i = 1 to 3 do
                for j = 1 to 3 do
                    if j > i then break; end;
                    print j;
                end;
            end;
            """));
    }

    [Fact]
    public void Logical_operators_short_circuit()
    {
        // The right operand would divide by zero if it were evaluated.
        Assert.Equal(TestHost.Lines("false"), TestHost.Run("var z = 0; print z != 0 && 10 / z > 1;"));
        Assert.Equal(TestHost.Lines("true"), TestHost.Run("var z = 0; print z == 0 || 10 / z > 1;"));
    }

    [Fact]
    public void A_non_boolean_condition_is_rejected()
    {
        // There is no integer truthiness; conditions must be boolean.
        TestHost.SingleError("if 1 then print 1; end;", DiagnosticCode.TypeMismatch);
        TestHost.SingleError("while 1 do print 1; end;", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void Comparing_values_of_different_types_is_rejected()
    {
        TestHost.SingleError("""print 1 == "one";""", DiagnosticCode.OperatorNotDefined);
    }

    [Fact]
    public void Ordering_strings_is_rejected()
    {
        TestHost.SingleError("""print "a" < "b";""", DiagnosticCode.OperatorNotDefined);
    }

    [Fact]
    public void Logical_operators_reject_non_booleans()
    {
        TestHost.SingleError("print 1 && 2;", DiagnosticCode.OperatorNotDefined);
    }

    [Fact]
    public void Break_outside_a_loop_is_rejected()
    {
        TestHost.SingleError("break;", DiagnosticCode.NotInsideALoop);
        TestHost.SingleError("if true then continue; end;", DiagnosticCode.NotInsideALoop);
    }

    [Fact]
    public void Booleans_can_be_stored_in_variables()
    {
        Assert.Equal(TestHost.Lines("true", "false"), TestHost.Run("""
            var flag = true;
            print flag;
            flag = 1 > 2;
            print flag;
            """));
    }

    [Fact]
    public void Booleans_concatenate_into_strings()
    {
        Assert.Equal(TestHost.Lines("ready: true"), TestHost.Run("""print "ready: " + true;"""));
    }

    [Fact]
    public void Assigning_a_bool_to_an_int_variable_is_rejected()
    {
        TestHost.SingleError("var x = 1; x = true;", DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void A_lone_ampersand_suggests_the_pair()
    {
        var errors = TestHost.Errors("print true & false;");
        Assert.Contains("did you mean '&&'", errors[0].Message);
    }
}
