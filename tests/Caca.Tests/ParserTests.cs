using Caca.Diagnostics;

namespace Caca.Tests;

/// <summary>
/// Precedence and associativity are asserted through the interpreter, which is
/// the observable behaviour a user of the language cares about.
/// </summary>
public class ParserTests
{
    [Theory]
    [InlineData("10 - 2 - 3", "5")]      // was 11: subtraction parsed right associatively
    [InlineData("10 / 2 * 5", "25")]     // was 1: no precedence between * and /
    [InlineData("20 / 5 / 2", "2")]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("2 * 3 + 4", "10")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("2 * (3 + 4)", "14")]
    [InlineData("-5", "-5")]
    [InlineData("-5 + 3", "-2")]
    [InlineData("10 - -5", "15")]
    [InlineData("- (2 + 3)", "-5")]
    [InlineData("--5", "5")]
    public void Expressions_respect_precedence_and_associativity(string expression, string expected)
    {
        Assert.Equal(TestHost.Lines(expected), TestHost.Run($"print {expression};"));
    }

    [Fact]
    public void Trailing_semicolon_is_optional()
    {
        // The original parser read one past the end of the token list here and
        // died with an IndexOutOfRangeException.
        Assert.Equal(TestHost.Lines("1", "2"), TestHost.Run("print 1; print 2"));
    }

    [Fact]
    public void Empty_program_is_valid()
    {
        Assert.Equal(string.Empty, TestHost.Run(string.Empty));
    }

    [Fact]
    public void Program_of_only_comments_is_valid()
    {
        Assert.Equal(string.Empty, TestHost.Run("// nothing to see here"));
    }

    [Fact]
    public void Missing_equals_in_declaration_is_reported_with_a_position()
    {
        var error = TestHost.SingleError("var x 1;", DiagnosticCode.UnexpectedToken);

        Assert.Equal(1, error.Location.Line);
        Assert.Equal(7, error.Location.Column);
        Assert.Contains("expected '='", error.Message);
    }

    [Fact]
    public void Unclosed_parenthesis_is_reported()
    {
        var errors = TestHost.Errors("print (1 + 2;");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void Unterminated_loop_body_is_reported()
    {
        var errors = TestHost.Errors("for i = 1 to 3 do print i;");
        Assert.Contains(errors, e => e.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void Several_errors_are_reported_from_one_run()
    {
        // Recovery at statement boundaries means one mistake no longer hides
        // every later one.
        var errors = TestHost.Errors("print @; print $;");
        Assert.True(errors.Count > 1, $"expected more than one error, got {errors.Count}");
    }
}
