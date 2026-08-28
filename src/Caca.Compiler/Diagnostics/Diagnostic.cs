namespace Caca.Diagnostics;

/// <summary>A single error, addressed to a location in the source file.</summary>
public sealed record Diagnostic(DiagnosticCode Code, string Message, SourceLocation Location)
{
    /// <summary>The <c>CACA0001</c>-style identifier shown to the user.</summary>
    public string Id => $"CACA{(int)Code:D4}";

    /// <summary>
    /// Formats the diagnostic the way compilers conventionally do, so editors
    /// and CI logs can pick the file and position out of the line.
    /// </summary>
    public string Format(string? fileName = null)
    {
        var origin = fileName is null ? Location.ToString() : $"{fileName}{Location}";
        return $"{origin}: error {Id}: {Message}";
    }

    public override string ToString() => Format();
}
