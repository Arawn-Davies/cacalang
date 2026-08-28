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
