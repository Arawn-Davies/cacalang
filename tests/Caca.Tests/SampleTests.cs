namespace Caca.Tests;

/// <summary>Runs the sample programs shipped with the compiler.</summary>
public class SampleTests
{
    [Fact]
    public void Helloworld_sample_runs()
    {
        var compilation = Compilation.CreateFromFile(TestHost.SamplePath("helloworld.caca"));
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader("7\n"), output);

        Assert.Equal(
            TestHost.Lines(
                "2",
                "enter a number",
                "7",
                "loop", "1", "loop in loop", "1", "loop in loop", "2", "loop in loop", "3",
                "loop", "2", "loop in loop", "1", "loop in loop", "2", "loop in loop", "3",
                "loop", "3", "loop in loop", "1", "loop in loop", "2", "loop in loop", "3",
                "that's it folks!"),
            output.ToString());
    }

    [Fact]
    public void Shell_sample_runs()
    {
        var compilation = Compilation.CreateFromFile(TestHost.SamplePath("shell.caca"));
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader("echo hi\nupper hi\nlen hello\nsqrt 16\nwrong\nexit\n"), output);

        Assert.Equal(
            TestHost.Lines(
                "cacash - type 'help' for commands, 'exit' to leave",
                "> ", "hi",
                "> ", "HI",
                "> ", "5",
                "> ", "4.0",
                "> ", "unknown command 'wrong'; type 'help'",
                "> ", "bye"),
            output.ToString());
    }

    /// <summary>
    /// One session against the toolkit, run through every command it has,
    /// exercising recursion, mutual recursion, and every kind of loop exit.
    /// </summary>
    public const string ToolkitSession = "help\n" +
        "factorial\n12\n" +
        "fib\n10\n" +
        "gcd\n48\n18\n" +
        "power\n2\n10\n" +
        "reverse\n12345\n" +
        "palindrome\n12321\n" +
        "palindrome\n12345\n" +
        "digitsum\n9875\n" +
        "parity\n7\n" +
        "parity\n8\n" +
        "evens\n10\n" +
        "collatz\n6\n" +
        "guess\n10\n80\n42\n" +
        "wrong\n" +
        "exit\n";

    [Fact]
    public void Toolkit_sample_runs()
    {
        var compilation = Compilation.CreateFromFile(TestHost.SamplePath("toolkit.caca"));
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader(ToolkitSession), output);

        Assert.Equal(
            TestHost.Lines(
                "cacalang toolkit - type 'help' for commands, 'exit' to leave",
                "> ",
                "commands:",
                "  factorial   n!, recursively",
                "  fib         the nth Fibonacci number, recursively",
                "  gcd         greatest common divisor of two numbers, recursively",
                "  power       base to the exponent, iteratively",
                "  reverse     a number with its digits reversed",
                "  palindrome  whether a number reads the same reversed",
                "  digitsum    the sum of a number's decimal digits",
                "  parity      even or odd, by mutual recursion",
                "  evens       every even number up to n",
                "  collatz     the Collatz sequence from n down to 1",
                "  guess       a higher-or-lower guessing game",
                "  help        this list",
                "  exit        leave the toolkit",
                "> ", "479001600",
                "> ", "55",
                "> ", "6",
                "> ", "1024",
                "> ", "54321",
                "> ", "palindrome",
                "> ", "not palindrome",
                "> ", "29",
                "> ", "odd",
                "> ", "even",
                "> ", "2", "4", "6", "8", "10",
                "> ", "6", "3", "10", "5", "16", "8", "4", "2", "1", "steps: 8",
                "> ", "guess a number from 1 to 100", "higher", "lower", "correct!",
                "> ", "unknown command 'wrong'; type 'help'",
                "> ", "bye"),
            output.ToString());
    }

    [Fact]
    public void Loop_sample_runs()
    {
        var compilation = Compilation.CreateFromFile(TestHost.SamplePath("loop.caca"));
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader("2\n"), output);

        Assert.Equal(
            TestHost.Lines(
                "How much do you love this company? (1-10) ",
                "Developers!",
                "Developers!",
                "Who said sit down?!!!!!"),
            output.ToString());
    }
}
