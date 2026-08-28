using System.Text.Json.Nodes;
using Caca.Diagnostics;

namespace Caca.LanguageServer.Protocol;

/// <summary>
/// The handful of Language Server Protocol shapes this server uses.
/// </summary>
/// <remarks>
/// Protocol positions are zero-based in both line and character; the compiler's
/// are one-based. Every conversion between the two lives here.
/// </remarks>
public static class Lsp
{
    /// <summary>The protocol's severity for an error.</summary>
    private const int SeverityError = 1;

    public static JsonObject Position(int line, int column) => new()
    {
        ["line"] = Math.Max(0, line - 1),
        ["character"] = Math.Max(0, column - 1),
    };

    public static JsonObject Range(SourceLocation location)
    {
        // A zero-length span still needs to be visible, so it covers one character.
        var length = Math.Max(1, location.Length);

        return new JsonObject
        {
            ["start"] = Position(location.Line, location.Column),
            ["end"] = Position(location.Line, location.Column + length),
        };
    }

    public static JsonObject Diagnostic(Diagnostic diagnostic) => new()
    {
        ["range"] = Range(diagnostic.Location),
        ["severity"] = SeverityError,
        ["code"] = diagnostic.Id,
        ["source"] = "caca",
        ["message"] = diagnostic.Message,
    };

    public static JsonObject Location(string uri, SourceLocation location) => new()
    {
        ["uri"] = uri,
        ["range"] = Range(location),
    };

    /// <summary>Wraps text so an editor renders it as a code block.</summary>
    public static JsonObject MarkdownCode(string text) => new()
    {
        ["kind"] = "markdown",
        ["value"] = $"```caca\n{text}\n```",
    };

    /// <summary>Converts a protocol position to the compiler's one-based line and column.</summary>
    public static (int Line, int Column) ToCompilerPosition(JsonNode? position) =>
        (((int?)position?["line"] ?? 0) + 1, ((int?)position?["character"] ?? 0) + 1);

    /// <summary>The protocol's symbol kinds, of which this language needs three.</summary>
    public static class SymbolKind
    {
        public const int Function = 12;
        public const int Variable = 13;
    }
}
