using System.Text;
using Caca;
using Caca.Diagnostics;

namespace Caca.Tests;

/// <summary>Helpers for compiling and running snippets inside a test.</summary>
internal static class TestHost
{
    /// <summary>Runs a program with the interpreter and returns everything it printed.</summary>
    public static string Run(string source, string? input = null)
    {
        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var output = new StringWriter { NewLine = "\n" };
        compilation.Run(new StringReader(input ?? string.Empty), output);
        return output.ToString();
    }

    /// <summary>Compiles a program that is expected to fail and returns its diagnostics.</summary>
    public static IReadOnlyList<Diagnostic> Errors(string source)
    {
        var compilation = Compilation.Create(source);
        Assert.False(compilation.Succeeded, "expected the program to fail, but it compiled");
        return [.. compilation.Diagnostics];
    }

    /// <summary>Asserts that a program fails with exactly one diagnostic of the given code.</summary>
    public static Diagnostic SingleError(string source, DiagnosticCode code)
    {
        var errors = Errors(source);
        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
        return error;
    }

    public static string Lines(params string[] lines) =>
        lines.Length == 0 ? string.Empty : string.Concat(lines.Select(l => l + "\n"));

    public static string SamplePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "samples", name);
}
