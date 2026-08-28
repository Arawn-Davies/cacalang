namespace Gfn.Syntax;

public enum TokenKind
{
    EndOfFile,

    // Literals and names
    Identifier,
    IntLiteral,
    StringLiteral,

    // Keywords
    VarKeyword,
    ForKeyword,
    ToKeyword,
    DoKeyword,
    EndKeyword,
    PrintKeyword,
    ReadIntKeyword,
    ReadStringKeyword,

    // Punctuation and operators
    Plus,
    Minus,
    Star,
    Slash,
    Equals,
    Semicolon,
    OpenParen,
    CloseParen,
}

public static class TokenKindExtensions
{
    /// <summary>The text used to describe a token kind in error messages.</summary>
    public static string Describe(this TokenKind kind) => kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.Identifier => "an identifier",
        TokenKind.IntLiteral => "an integer literal",
        TokenKind.StringLiteral => "a string literal",
        TokenKind.VarKeyword => "'var'",
        TokenKind.ForKeyword => "'for'",
        TokenKind.ToKeyword => "'to'",
        TokenKind.DoKeyword => "'do'",
        TokenKind.EndKeyword => "'end'",
        TokenKind.PrintKeyword => "'print'",
        TokenKind.ReadIntKeyword => "'read_int'",
        TokenKind.ReadStringKeyword => "'read_string'",
        TokenKind.Plus => "'+'",
        TokenKind.Minus => "'-'",
        TokenKind.Star => "'*'",
        TokenKind.Slash => "'/'",
        TokenKind.Equals => "'='",
        TokenKind.Semicolon => "';'",
        TokenKind.OpenParen => "'('",
        TokenKind.CloseParen => "')'",
        _ => kind.ToString(),
    };
}
