using Caca.Cli;

namespace Caca.Tests;

public class ReplTests
{
    /// <summary>Feeds lines to the REPL and returns what it printed, prompts removed.</summary>
    private static string Session(params string[] lines)
    {
        var input = new StringReader(string.Concat(lines.Select(line => line + "\n")));
        var output = new StringWriter { NewLine = "\n" };

        new Repl(input, output).Run();

        return output.ToString()
            .Replace("caca> ", string.Empty, StringComparison.Ordinal)
            .Replace("  ... ", string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_expression_shows_its_value()
    {
        Assert.Contains("42", Session("21 * 2"));
    }

    [Fact]
    public void Variables_persist_between_entries()
    {
        var session = Session("var x = 21;", "x * 2");

        Assert.Contains("42", session);
    }

    [Fact]
    public void A_statement_is_not_printed_twice()
    {
        var session = Session("""print "once";""");

        Assert.Equal(1, session.Split("once").Length - 1);
    }

    [Fact]
    public void Earlier_output_is_not_repeated_when_a_later_entry_runs()
    {
        var session = Session("""print "first";""", """print "second";""");

        Assert.Equal(1, session.Split("first").Length - 1);
        Assert.Contains("second", session);
    }

    [Fact]
    public void A_function_can_be_defined_across_several_lines_and_then_called()
    {
        var session = Session(
            "func twice(n: int): int do",
            "return n * 2;",
            "end",
            "twice(50)");

        Assert.Contains("100", session);
    }

    [Fact]
    public void A_call_that_returns_nothing_runs_without_being_printed()
    {
        var session = Session("""func greet(s: string) do print "hi " + s; end""", """greet("you")""");

        Assert.Contains("hi you", session);
        Assert.DoesNotContain("error", session);
    }

    [Fact]
    public void An_error_is_reported_and_the_session_carries_on()
    {
        var session = Session("nope", """print "still here";""");

        Assert.Contains("CACA0008", session);
        Assert.Contains("still here", session);
    }

    [Fact]
    public void A_failed_entry_is_not_kept()
    {
        // If the broken entry had been kept, every later entry would fail too.
        var session = Session("var x = ;", "var x = 1;", "x");

        Assert.Contains("1", session);
    }

    [Fact]
    public void A_runtime_error_is_reported_without_ending_the_session()
    {
        var session = Session("var z = 0;", "1 / z", """print "alive";""");

        Assert.Contains("divide by zero", session);
        Assert.Contains("alive", session);
    }

    [Fact]
    public void Input_read_by_an_earlier_entry_is_not_consumed_again()
    {
        // The whole session reruns on each entry; the line read the first time
        // has to be replayed rather than taken from the input again.
        var session = Session("var n = 0;", "read_int n;", "7", "n * 2", "n + 1");

        Assert.Contains("14", session);
        Assert.Contains("8", session);
    }

    [Fact]
    public void List_shows_what_was_typed()
    {
        var session = Session("var x = 1;", "x", ":list");

        Assert.Contains("var x = 1;", session);
        Assert.Contains("\nx\n", session);
    }

    [Fact]
    public void Reset_forgets_the_session()
    {
        var session = Session("var x = 1;", ":reset", "x");

        Assert.Contains("Session reset.", session);
        Assert.Contains("CACA0008", session);
    }

    [Fact]
    public void Help_lists_the_commands()
    {
        Assert.Contains(":reset", Session(":help"));
    }

    [Fact]
    public void An_unknown_command_is_reported()
    {
        Assert.Contains("unknown command", Session(":nonsense"));
    }

    [Fact]
    public void Quit_ends_the_session()
    {
        var session = Session(":quit", """print "never";""");

        Assert.DoesNotContain("never", session);
    }

    [Fact]
    public void Blank_lines_are_ignored()
    {
        Assert.Contains("1", Session(string.Empty, "1"));
    }
}
