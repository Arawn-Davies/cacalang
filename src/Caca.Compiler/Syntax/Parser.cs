using Caca.Binding;
using Caca.Diagnostics;

namespace Caca.Syntax;

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

    public static CompilationUnit Parse(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        var parser = new Parser(tokens, diagnostics);
        return parser.ParseCompilationUnit();
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

    /// <summary>
    /// Parses a whole file: function declarations, which may appear anywhere at
    /// the top level, and the statements between them.
    /// </summary>
    private CompilationUnit ParseCompilationUnit()
    {
        var start = Current.Location;
        var functions = new List<FunctionDeclaration>();
        var statements = new List<Statement>();

        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (Current.Kind == TokenKind.FuncKeyword)
            {
                functions.Add(ParseFunction());
                Match(TokenKind.Semicolon);
                continue;
            }

            var before = _position;
            var block = ParseStatements(TokenKind.FuncKeyword, TokenKind.EndOfFile);
            statements.AddRange(block.Statements);

            // Guarantee forward progress even when nothing could be parsed.
            if (_position == before)
            {
                Advance();
            }
        }

        var end = Expect(TokenKind.EndOfFile);
        var location = start.To(end.Location);
        return new CompilationUnit(functions, new BlockStatement(statements, location), location);
    }

    private FunctionDeclaration ParseFunction()
    {
        var keyword = Advance();
        var name = Expect(TokenKind.Identifier, "after 'func'");
        var parameters = ParseParameterList();

        // A function with no written return type returns nothing.
        var returnType = Match(TokenKind.Colon) ? ParseTypeReference() : null;

        Expect(TokenKind.DoKeyword, "before the function body");
        var body = ParseStatements(TokenKind.EndKeyword);
        var end = Expect(TokenKind.EndKeyword, "to close the function body");

        return new FunctionDeclaration(name.Text, parameters, returnType, body, keyword.Location.To(end.Location));
    }

    private List<ParameterDeclaration> ParseParameterList()
    {
        var parameters = new List<ParameterDeclaration>();
        Expect(TokenKind.OpenParen, "to open the parameter list");

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
        {
            var name = Expect(TokenKind.Identifier, "as a parameter name");
            Expect(TokenKind.Colon, $"after the parameter '{name.Text}'");
            var type = ParseTypeReference();
            parameters.Add(new ParameterDeclaration(name.Text, type, name.Location.To(type.Location)));

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }

        Expect(TokenKind.CloseParen, "to close the parameter list");
        return parameters;
    }

    private TypeReference ParseTypeReference()
    {
        var name = Expect(TokenKind.Identifier, "as a type name");
        return new TypeReference(name.Text, name.Location);
    }

    private static bool Contains(TokenKind[] kinds, TokenKind kind) => Array.IndexOf(kinds, kind) >= 0;

    /// <summary>
    /// Parses statements until <paramref name="terminator"/> or end of file.
    /// </summary>
    /// <remarks>
    /// Semicolons separate statements; a trailing one is allowed but no longer
    /// required, so a program may end with <c>print x</c>.
    /// </remarks>
    private BlockStatement ParseStatements(params TokenKind[] terminators)
    {
        var start = Current.Location;
        var statements = new List<Statement>();

        while (!Contains(terminators, Current.Kind) && Current.Kind != TokenKind.EndOfFile)
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
        TokenKind.ReadIntKeyword => ParseRead(CacaType.Int),
        TokenKind.ReadStringKeyword => ParseRead(CacaType.String),
        TokenKind.ForKeyword => ParseFor(),
        TokenKind.IfKeyword => ParseIf(consumeEnd: true),
        TokenKind.WhileKeyword => ParseWhile(),
        TokenKind.BreakKeyword => ParseBreak(),
        TokenKind.ContinueKeyword => ParseContinue(),
        TokenKind.ReturnKeyword => ParseReturn(),
        TokenKind.Identifier when Peek(1).Kind == TokenKind.OpenParen => ParseCallStatement(),
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
        while (Current.Kind is not (TokenKind.Semicolon or TokenKind.EndKeyword
            or TokenKind.ElseKeyword or TokenKind.EndOfFile))
        {
            Advance();
        }
    }

    private Statement ParseVariableDeclaration()
    {
        var keyword = Advance();
        var name = Expect(TokenKind.Identifier, "after 'var'");

        // The type may be written, as in `var x: int = 1`, or left to be
        // inferred from the initializer.
        var declaredType = Match(TokenKind.Colon) ? ParseTypeReference() : null;

        Expect(TokenKind.Equals, $"after 'var {name.Text}'");
        var initializer = ParseExpression();

        return new VariableDeclaration(
            name.Text, declaredType, initializer, keyword.Location.To(initializer.Location));
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

    private Statement ParseRead(CacaType type)
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

    /// <summary>
    /// Parses <c>if c then A else B end</c>.
    /// </summary>
    /// <param name="consumeEnd">
    /// False when this <c>if</c> is the <c>else if</c> of an enclosing one, in
    /// which case the enclosing <c>if</c> owns the single closing <c>end</c>.
    /// </param>
    private Statement ParseIf(bool consumeEnd)
    {
        var keyword = Advance();
        var condition = ParseExpression();
        Expect(TokenKind.ThenKeyword, "after the condition");
        var thenBranch = ParseStatements(TokenKind.ElseKeyword, TokenKind.EndKeyword);

        Statement? elseBranch = null;

        if (Match(TokenKind.ElseKeyword))
        {
            // `else if` chains without stacking up an `end` for each link.
            elseBranch = Current.Kind == TokenKind.IfKeyword
                ? ParseIf(consumeEnd: false)
                : ParseStatements(TokenKind.EndKeyword);
        }

        var end = consumeEnd
            ? Expect(TokenKind.EndKeyword, "to close the 'if'")
            : Current;

        return new IfStatement(condition, thenBranch, elseBranch, keyword.Location.To(end.Location));
    }

    private Statement ParseWhile()
    {
        var keyword = Advance();
        var condition = ParseExpression();
        Expect(TokenKind.DoKeyword, "after the condition");
        var body = ParseStatements(TokenKind.EndKeyword);
        var end = Expect(TokenKind.EndKeyword, "to close the loop body");
        return new WhileStatement(condition, body, keyword.Location.To(end.Location));
    }

    private Statement ParseReturn()
    {
        var keyword = Advance();

        // A bare `return;` ends a function that produces no value.
        var value = Current.Kind is TokenKind.Semicolon or TokenKind.EndKeyword
            or TokenKind.ElseKeyword or TokenKind.EndOfFile
            ? null
            : ParseExpression();

        return new ReturnStatement(value, value is null ? keyword.Location : keyword.Location.To(value.Location));
    }

    private Statement ParseCallStatement()
    {
        var call = ParseCall(Advance());
        return new CallStatement(call, call.Location);
    }

    private CallExpression ParseCall(Token name)
    {
        Expect(TokenKind.OpenParen, $"after '{name.Text}'");
        var arguments = new List<Expression>();

        while (Current.Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
        {
            arguments.Add(ParseExpression());

            if (!Match(TokenKind.Comma))
            {
                break;
            }
        }

        var close = Expect(TokenKind.CloseParen, $"to close the arguments to '{name.Text}'");
        return new CallExpression(name.Text, arguments, name.Location.To(close.Location));
    }

    private Statement ParseBreak() => new BreakStatement(Advance().Location);

    private Statement ParseContinue() => new ContinueStatement(Advance().Location);

    // Expression grammar, lowest precedence first:
    //   <expr>       := <additive>
    //   <additive>   := <multiplicative> (('+' | '-') <multiplicative>)*
    //   <multiplicative> := <unary> (('*' | '/') <unary>)*
    //   <unary>      := ('+' | '-') <unary> | <primary>
    //   <primary>    := <int> | <string> | <ident> | '(' <expr> ')'
    private Expression ParseExpression() => ParseBinary(0);

    private static int PrecedenceOf(TokenKind kind) => kind switch
    {
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 6,
        TokenKind.Plus or TokenKind.Minus => 5,
        TokenKind.Less or TokenKind.LessOrEquals
            or TokenKind.Greater or TokenKind.GreaterOrEquals => 4,
        TokenKind.EqualsEquals or TokenKind.BangEquals => 3,
        TokenKind.AmpersandAmpersand => 2,
        TokenKind.PipePipe => 1,
        _ => 0,
    };

    private static BinaryOperator OperatorOf(TokenKind kind) => kind switch
    {
        TokenKind.Plus => BinaryOperator.Add,
        TokenKind.Minus => BinaryOperator.Subtract,
        TokenKind.Star => BinaryOperator.Multiply,
        TokenKind.Slash => BinaryOperator.Divide,
        TokenKind.Percent => BinaryOperator.Modulo,
        TokenKind.EqualsEquals => BinaryOperator.Equal,
        TokenKind.BangEquals => BinaryOperator.NotEqual,
        TokenKind.Less => BinaryOperator.Less,
        TokenKind.LessOrEquals => BinaryOperator.LessOrEqual,
        TokenKind.Greater => BinaryOperator.Greater,
        TokenKind.GreaterOrEquals => BinaryOperator.GreaterOrEqual,
        TokenKind.AmpersandAmpersand => BinaryOperator.LogicalAnd,
        TokenKind.PipePipe => BinaryOperator.LogicalOr,
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
        if (Current.Kind is TokenKind.Minus or TokenKind.Plus or TokenKind.Bang)
        {
            var op = Advance();
            var operand = ParseUnary();
            var kind = op.Kind switch
            {
                TokenKind.Minus => UnaryOperator.Negate,
                TokenKind.Bang => UnaryOperator.Not,
                _ => UnaryOperator.Identity,
            };

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
                return new LiteralExpression(token.IntValue, CacaType.Int, token.Location);

            case TokenKind.StringLiteral:
                Advance();
                return new LiteralExpression(token.StringValue, CacaType.String, token.Location);

            case TokenKind.TrueKeyword:
            case TokenKind.FalseKeyword:
                Advance();
                return new LiteralExpression(
                    token.Kind == TokenKind.TrueKeyword, CacaType.Bool, token.Location);

            case TokenKind.Identifier when Peek(1).Kind == TokenKind.OpenParen:
                Advance();
                return ParseCall(token);

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
                return new LiteralExpression(0, CacaType.Error, token.Location);
        }
    }
}
