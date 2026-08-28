using System.Globalization;
using System.Text;
using Gfn.Diagnostics;

namespace Gfn.Syntax;

/// <summary>
/// Turns source text into a list of <see cref="Token"/>s.
/// </summary>
/// <remarks>
/// Unlike the original scanner this one reads from a string rather than a
/// <see cref="TextReader"/>, which is what makes lookahead and source positions
/// possible, and it reports problems as diagnostics instead of throwing.
/// </remarks>
public sealed class Lexer
{
    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["var"] = TokenKind.VarKeyword,
        ["for"] = TokenKind.ForKeyword,
        ["to"] = TokenKind.ToKeyword,
        ["do"] = TokenKind.DoKeyword,
        ["end"] = TokenKind.EndKeyword,
        ["print"] = TokenKind.PrintKeyword,
        ["read_int"] = TokenKind.ReadIntKeyword,
        ["read_string"] = TokenKind.ReadStringKeyword,
    };

    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _position;
    private int _line = 1;
    private int _lineStart;

    private Lexer(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public static IReadOnlyList<Token> Tokenize(string text, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(text, diagnostics);
        return lexer.Run();
    }

    private char Current => Peek(0);

    private char Lookahead => Peek(1);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index >= _text.Length ? '\0' : _text[index];
    }

    private int Column => _position - _lineStart + 1;

    private List<Token> Run()
    {
        var tokens = new List<Token>();

        while (true)
        {
            var token = NextToken();
            tokens.Add(token);

            if (token.Kind == TokenKind.EndOfFile)
            {
                return tokens;
            }
        }
    }

    private Token NextToken()
    {
        while (true)
        {
            SkipTrivia();

            var start = _position;
            var line = _line;
            var column = Column;

            if (_position >= _text.Length)
            {
                return Make(TokenKind.EndOfFile, start, line, column, string.Empty);
            }

            var ch = Current;

            if (char.IsLetter(ch) || ch == '_')
            {
                return LexIdentifierOrKeyword(start, line, column);
            }

            if (char.IsDigit(ch))
            {
                return LexIntLiteral(start, line, column);
            }

            if (ch == '"')
            {
                return LexStringLiteral(start, line, column);
            }

            var kind = ch switch
            {
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '=' => TokenKind.Equals,
                ';' => TokenKind.Semicolon,
                '(' => TokenKind.OpenParen,
                ')' => TokenKind.CloseParen,
                _ => (TokenKind?)null,
            };

            if (kind is null)
            {
                _diagnostics.Report(
                    DiagnosticCode.UnexpectedCharacter,
                    new SourceLocation(start, 1, line, column),
                    $"unexpected character '{ch}' in source");

                // Consume it and keep going, so one stray character does not
                // hide every later error in the file.
                _position++;
                continue;
            }

            _position++;
            return Make(kind.Value, start, line, column);
        }
    }

    /// <summary>Skips whitespace, <c>// line</c> comments and <c>/* block */</c> comments.</summary>
    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            var ch = Current;

            if (ch == '\n')
            {
                _position++;
                _line++;
                _lineStart = _position;
            }
            else if (char.IsWhiteSpace(ch))
            {
                _position++;
            }
            else if (ch == '/' && Lookahead == '/')
            {
                while (_position < _text.Length && Current != '\n')
                {
                    _position++;
                }
            }
            else if (ch == '/' && Lookahead == '*')
            {
                SkipBlockComment();
            }
            else
            {
                return;
            }
        }
    }

    private void SkipBlockComment()
    {
        var start = _position;
        var line = _line;
        var column = Column;

        _position += 2;

        while (true)
        {
            if (_position >= _text.Length)
            {
                _diagnostics.Report(
                    DiagnosticCode.UnexpectedCharacter,
                    new SourceLocation(start, 2, line, column),
                    "unterminated block comment");
                return;
            }

            if (Current == '*' && Lookahead == '/')
            {
                _position += 2;
                return;
            }

            if (Current == '\n')
            {
                _line++;
                _lineStart = _position + 1;
            }

            _position++;
        }
    }

    private Token LexIdentifierOrKeyword(int start, int line, int column)
    {
        // <ident> := <char> <ident_rest>*  where <ident_rest> := <char> | <digit>
        // The original scanner stopped at the first digit, so `x1` lexed as `x`
        // followed by the integer 1 and every use of it was a syntax error.
        while (_position < _text.Length && (char.IsLetterOrDigit(Current) || Current == '_'))
        {
            _position++;
        }

        var text = _text[start.._position];
        var kind = Keywords.GetValueOrDefault(text, TokenKind.Identifier);
        return Make(kind, start, line, column, text);
    }

    private Token LexIntLiteral(int start, int line, int column)
    {
        while (_position < _text.Length && char.IsDigit(Current))
        {
            _position++;
        }

        var text = _text[start.._position];

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            _diagnostics.Report(
                DiagnosticCode.IntegerOutOfRange,
                new SourceLocation(start, text.Length, line, column),
                $"the integer literal '{text}' is outside the range of int ({int.MinValue} to {int.MaxValue})");
        }

        return Make(TokenKind.IntLiteral, start, line, column, text, value);
    }

    private Token LexStringLiteral(int start, int line, int column)
    {
        _position++; // opening quote
        var value = new StringBuilder();

        while (true)
        {
            if (_position >= _text.Length || Current == '\n')
            {
                _diagnostics.Report(
                    DiagnosticCode.UnterminatedString,
                    new SourceLocation(start, _position - start, line, column),
                    "unterminated string literal");
                break;
            }

            if (Current == '"')
            {
                _position++;
                break;
            }

            if (Current == '\\')
            {
                AppendEscape(value);
                continue;
            }

            value.Append(Current);
            _position++;
        }

        return Make(TokenKind.StringLiteral, start, line, column, _text[start.._position], value.ToString());
    }

    private void AppendEscape(StringBuilder value)
    {
        var escapeStart = _position;
        var escapeLine = _line;
        var escapeColumn = Column;
        _position++; // backslash

        var escaped = _position < _text.Length ? Current : '\0';
        var decoded = escaped switch
        {
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            '0' => "\0",
            '\\' => "\\",
            '"' => "\"",
            _ => null,
        };

        if (decoded is null)
        {
            _diagnostics.Report(
                DiagnosticCode.InvalidEscapeSequence,
                new SourceLocation(escapeStart, 2, escapeLine, escapeColumn),
                $"unrecognized escape sequence '\\{escaped}'");

            value.Append('\\');
            return;
        }

        value.Append(decoded);
        _position++;
    }

    private Token Make(TokenKind kind, int start, int line, int column, string? text = null, object? value = null)
    {
        text ??= _text[start.._position];
        return new Token(kind, text, value, new SourceLocation(start, _position - start, line, column));
    }
}
