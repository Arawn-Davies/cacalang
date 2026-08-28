using Gfn.Binding;
using Gfn.Diagnostics;

namespace Gfn.Syntax;

/// <summary>
/// A recursive-descent parser with precedence climbing for expressions.
/// </summary>
/// <remarks>
/// The original parser decided whether an expression was arithmetic by peeking
/// at a single token and comparing it against a hard-coded set of followers
/// (<c>to</c>, <c>do</c>, <c>;</c>), then built every operator right
/// associatively. That made <c>10 - 2 - 3</c> evaluate to 11 and
/// <c>10 / 2 * 5</c> evaluate to 1, and it read past the end of the token list
/// whenever a program ended without a trailing semicolon.
/// </remarks>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    private Parser(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    public static BlockStatement Parse(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        var parser = new Parser(tokens, diagnostics);
        return parser.ParseProgram();
    }

    private Token Current => Peek(0);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
    }

    private Token Advance()
    {
        var token = Current;

        if (token.Kind != TokenKind.EndOfFile)
        {
            _position++;
        }

        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string? context = null)
    {
        if (Current.Kind == kind)
        {
            return Advance();
        }

        var where = context is null ? string.Empty : $" {context}";
        _diagnostics.Report(
            DiagnosticCode.UnexpectedToken,
            Current.Location,
            $"expected {kind.Describe()}{where}, but found {Current}");

        // Synthesize the token so that parsing can continue and report more errors.
        return new Token(kind, string.Empty, null, Current.Location);
    }

    private BlockStatement ParseProgram()
    {
        var block = ParseStatements(TokenKind.EndOfFile);
        Expect(TokenKind.EndOfFile);
        return block;
    }

    /// <summary>
    /// Parses statements until <paramref name="terminator"/> or end of file.
    /// </summary>
    /// <remarks>
    /// Semicolons separate statements; a trailing one is allowed but no longer
    /// required, so a program may end with <c>print x</c>.
    /// </remarks>
    private BlockStatement ParseStatements(TokenKind terminator)
    {
        var start = Current.Location;
        var statements = new List<Statement>();

        while (Current.Kind != terminator && Current.Kind != TokenKind.EndOfFile)
        {
            var before = _position;
            statements.Add(ParseStatement());

            // Guarantee forward progress even if a statement failed to consume anything.
            if (_position == before)
            {
                Advance();
            }

            if (!Match(TokenKind.Semicolon))
            {
                break;
            }
        }

        var end = statements.Count > 0 ? statements[^1].Location : start;
        return new BlockStatement(statements, start.To(end));
    }

    private Statement ParseStatement() => Current.Kind switch
    {
        TokenKind.VarKeyword => ParseVariableDeclaration(),
        TokenKind.PrintKeyword => ParsePrint(),
        TokenKind.ReadIntKeyword => ParseRead(GfnType.Int),
        TokenKind.ReadStringKeyword => ParseRead(GfnType.String),
        TokenKind.ForKeyword => ParseFor(),
        TokenKind.Identifier => ParseAssignment(),
        _ => ParseUnexpectedStatement(),
    };

    private Statement ParseUnexpectedStatement()
    {
        var token = Current;
        _diagnostics.Report(
            DiagnosticCode.UnexpectedToken,
            token.Location,
            $"expected a statement, but found {token}");

        SkipToStatementBoundary();
        return new BlockStatement([], token.Location);
    }

    /// <summary>Discards tokens up to the next statement boundary after an error.</summary>
    private void SkipToStatementBoundary()
    {
        while (Current.Kind is not (TokenKind.Semicolon or TokenKind.EndKeyword or TokenKind.EndOfFile))
        {
            Advance();
        }
    }

    private Statement ParseVariableDeclaration()
    {
        var keyword = Advance();
        var name = Expect(TokenKind.Identifier, "after 'var'");
        Expect(TokenKind.Equals, $"after 'var {name.Text}'");
        var initializer = ParseExpression();
        return new VariableDeclaration(name.Text, initializer, keyword.Location.To(initializer.Location));
    }

    private Statement ParseAssignment()
    {
        var name = Advance();
        Expect(TokenKind.Equals, $"after '{name.Text}'");
        var value = ParseExpression();
        return new AssignmentStatement(name.Text, value, name.Location.To(value.Location));
    }

    private Statement ParsePrint()
    {
        var keyword = Advance();
        var expression = ParseExpression();
        return new PrintStatement(expression, keyword.Location.To(expression.Location));
    }

    private Statement ParseRead(GfnType type)
    {
        var keyword = Advance();
        var name = Expect(TokenKind.Identifier, $"after '{keyword.Text}'");
        return new ReadStatement(name.Text, type, keyword.Location.To(name.Location));
    }

    private Statement ParseFor()
    {
        var keyword = Advance();
        var name = Expect(TokenKind.Identifier, "after 'for'");
        Expect(TokenKind.Equals, "after the loop variable");
        var from = ParseExpression();
        Expect(TokenKind.ToKeyword, "after the first loop bound");
        var to = ParseExpression();
        Expect(TokenKind.DoKeyword, "after the second loop bound");
        var body = ParseStatements(TokenKind.EndKeyword);
        var end = Expect(TokenKind.EndKeyword, "to close the loop body");
        return new ForStatement(name.Text, from, to, body, keyword.Location.To(end.Location));
    }

    // Expression grammar, lowest precedence first:
    //   <expr>       := <additive>
    //   <additive>   := <multiplicative> (('+' | '-') <multiplicative>)*
    //   <multiplicative> := <unary> (('*' | '/') <unary>)*
    //   <unary>      := ('+' | '-') <unary> | <primary>
    //   <primary>    := <int> | <string> | <ident> | '(' <expr> ')'
    private Expression ParseExpression() => ParseBinary(0);

    private static int PrecedenceOf(TokenKind kind) => kind switch
    {
        TokenKind.Star or TokenKind.Slash => 2,
        TokenKind.Plus or TokenKind.Minus => 1,
        _ => 0,
    };

    private static BinaryOperator OperatorOf(TokenKind kind) => kind switch
    {
        TokenKind.Plus => BinaryOperator.Add,
        TokenKind.Minus => BinaryOperator.Subtract,
        TokenKind.Star => BinaryOperator.Multiply,
        TokenKind.Slash => BinaryOperator.Divide,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a binary operator"),
    };

    private Expression ParseBinary(int parentPrecedence)
    {
        var left = ParseUnary();

        while (true)
        {
            var precedence = PrecedenceOf(Current.Kind);

            // Every operator is left associative, so an operator of equal
            // precedence terminates the loop and re-enters with `left` folded in.
            if (precedence == 0 || precedence <= parentPrecedence)
            {
                return left;
            }

            var op = Advance();
            var right = ParseBinary(precedence);
            left = new BinaryExpression(left, OperatorOf(op.Kind), right, left.Location.To(right.Location));
        }
    }

    private Expression ParseUnary()
    {
        if (Current.Kind is TokenKind.Minus or TokenKind.Plus)
        {
            var op = Advance();
            var operand = ParseUnary();
            var kind = op.Kind == TokenKind.Minus ? UnaryOperator.Negate : UnaryOperator.Identity;
            return new UnaryExpression(kind, operand, op.Location.To(operand.Location));
        }

        return ParsePrimary();
    }

    private Expression ParsePrimary()
    {
        var token = Current;

        switch (token.Kind)
        {
            case TokenKind.IntLiteral:
                Advance();
                return new LiteralExpression(token.IntValue, GfnType.Int, token.Location);

            case TokenKind.StringLiteral:
                Advance();
                return new LiteralExpression(token.StringValue, GfnType.String, token.Location);

            case TokenKind.Identifier:
                Advance();
                return new VariableExpression(token.Text, token.Location);

            case TokenKind.OpenParen:
                Advance();
                var inner = ParseExpression();
                var close = Expect(TokenKind.CloseParen, "to close the parenthesized expression");
                return new ParenthesizedExpression(inner, token.Location.To(close.Location));

            default:
                _diagnostics.Report(
                    DiagnosticCode.ExpectedExpression,
                    token.Location,
                    $"expected an expression, but found {token}");

                // An error literal keeps the tree shaped correctly for later passes.
                return new LiteralExpression(0, GfnType.Error, token.Location);
        }
    }
}
