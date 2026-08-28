using Gfn.Diagnostics;
using Gfn.Syntax;

namespace Gfn.Tests;

public class LexerTests
{
    private static IReadOnlyList<Token> Tokenize(string text, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        return Lexer.Tokenize(text, diagnostics);
    }

    [Fact]
    public void Identifiers_may_contain_digits()
    {
        // The original scanner stopped at the first digit, so `x1` lexed as the
        // identifier `x` followed by the integer literal `1`.
        var tokens = Tokenize("x1 count2go _leading", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(
            ["x1", "count2go", "_leading"],
            tokens.Where(t => t.Kind == TokenKind.Identifier).Select(t => t.Text));
    }

    [Fact]
    public void Keywords_are_recognized_and_not_confused_with_identifiers()
    {
        var tokens = Tokenize("for forever to today", out _);

        Assert.Equal(TokenKind.ForKeyword, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal(TokenKind.ToKeyword, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
    }

    [Theory]
    [InlineData(@"""a\nb""", "a\nb")]
    [InlineData(@"""a\tb""", "a\tb")]
    [InlineData(@"""say \""hi\""""", "say \"hi\"")]
    [InlineData(@"""back\\slash""", @"back\slash")]
    public void String_literals_decode_escape_sequences(string source, string expected)
    {
        var tokens = Tokenize(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(expected, tokens[0].StringValue);
    }

    [Fact]
    public void Unrecognized_escape_sequence_is_reported()
    {
        Tokenize(@"""\q""", out var diagnostics);
        Assert.Equal(DiagnosticCode.InvalidEscapeSequence, diagnostics.Single().Code);
    }

    [Fact]
    public void Unterminated_string_is_reported_rather_than_thrown()
    {
        Tokenize("\"no end", out var diagnostics);
        Assert.Equal(DiagnosticCode.UnterminatedString, diagnostics.Single().Code);
    }

    [Fact]
    public void Comments_are_skipped()
    {
        var tokens = Tokenize("""
            // leading comment
            print /* inline */ 1; // trailing
            /* multi
               line */
            """, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(
            [TokenKind.PrintKeyword, TokenKind.IntLiteral, TokenKind.Semicolon, TokenKind.EndOfFile],
            tokens.Select(t => t.Kind));
    }

    [Fact]
    public void Division_is_not_mistaken_for_a_comment()
    {
        var tokens = Tokenize("a / b", out _);
        Assert.Equal(TokenKind.Slash, tokens[1].Kind);
    }

    [Fact]
    public void Integer_literal_out_of_range_is_reported()
    {
        Tokenize("2147483648", out var diagnostics);
        Assert.Equal(DiagnosticCode.IntegerOutOfRange, diagnostics.Single().Code);
    }

    [Fact]
    public void Tokens_carry_line_and_column()
    {
        var tokens = Tokenize("var x = 1;\nprint x;", out _);
        var print = tokens.First(t => t.Kind == TokenKind.PrintKeyword);

        Assert.Equal(2, print.Location.Line);
        Assert.Equal(1, print.Location.Column);
    }

    [Fact]
    public void Unexpected_character_is_reported_and_lexing_continues()
    {
        var tokens = Tokenize("var x @ 1", out var diagnostics);

        Assert.Equal(DiagnosticCode.UnexpectedCharacter, diagnostics.Single().Code);
        Assert.Contains(tokens, t => t.Kind == TokenKind.IntLiteral);
    }
}
