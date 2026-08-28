using Caca.Diagnostics;

namespace Caca.Syntax;

/// <summary>
/// A lexeme together with its source location and, for literals, its decoded value.
/// </summary>
/// <remarks>
/// The original scanner produced a bare <c>IList&lt;object&gt;</c> in which
/// identifiers were <see cref="string"/> and string literals were
/// <see cref="System.Text.StringBuilder"/> — the only thing distinguishing the
/// two. A real token type removes that trap and carries positions for errors.
/// </remarks>
public readonly record struct Token(TokenKind Kind, string Text, object? Value, SourceLocation Location)
{
    public int IntValue => (int)(Value ?? 0);

    public string StringValue => (string)(Value ?? string.Empty);

    public override string ToString() => Kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.StringLiteral => $"\"{Value}\"",
        _ => Text,
    };
}
