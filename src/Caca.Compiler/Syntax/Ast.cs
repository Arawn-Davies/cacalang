using Caca.Binding;
using Caca.Diagnostics;

namespace Caca.Syntax;

/// <summary>Base class for every node in the syntax tree.</summary>
public abstract class SyntaxNode(SourceLocation location)
{
    public SourceLocation Location { get; } = location;
}

// ---------------------------------------------------------------- statements

public abstract class Statement(SourceLocation location) : SyntaxNode(location);

/// <summary>
/// A sequence of statements: <c>&lt;stmt&gt; ; &lt;stmt&gt;</c>.
/// </summary>
/// <remarks>
/// The original AST modelled this as a right-leaning binary <c>Sequence</c>
/// node, which made a program of N statements N levels deep. A flat list is
/// both cheaper and far easier to walk.
/// </remarks>
public sealed class BlockStatement(IReadOnlyList<Statement> statements, SourceLocation location)
    : Statement(location)
{
    public IReadOnlyList<Statement> Statements { get; } = statements;
}

/// <summary><c>var &lt;ident&gt; = &lt;expr&gt;</c></summary>
public sealed class VariableDeclaration(string name, Expression initializer, SourceLocation location)
    : Statement(location)
{
    public string Name { get; } = name;

    public Expression Initializer { get; } = initializer;
}

/// <summary><c>&lt;ident&gt; = &lt;expr&gt;</c></summary>
public sealed class AssignmentStatement(string name, Expression value, SourceLocation location)
    : Statement(location)
{
    public string Name { get; } = name;

    public Expression Value { get; } = value;
}

/// <summary><c>print &lt;expr&gt;</c></summary>
public sealed class PrintStatement(Expression expression, SourceLocation location) : Statement(location)
{
    public Expression Expression { get; } = expression;
}

/// <summary><c>read_int &lt;ident&gt;</c> and <c>read_string &lt;ident&gt;</c>.</summary>
public sealed class ReadStatement(string name, CacaType type, SourceLocation location) : Statement(location)
{
    public string Name { get; } = name;

    /// <summary>The type of value read: <see cref="CacaType.Int"/> or <see cref="CacaType.String"/>.</summary>
    public CacaType Type { get; } = type;
}

/// <summary><c>for &lt;ident&gt; = &lt;expr&gt; to &lt;expr&gt; do &lt;stmt&gt; end</c></summary>
public sealed class ForStatement(
    string name,
    Expression from,
    Expression to,
    Statement body,
    SourceLocation location) : Statement(location)
{
    public string Name { get; } = name;

    public Expression From { get; } = from;

    public Expression To { get; } = to;

    public Statement Body { get; } = body;

    /// <summary>
    /// True when the loop variable was not previously declared and the loop
    /// therefore introduces it.
    /// </summary>
    public bool DeclaresVariable { get; internal set; }
}

/// <summary><c>if &lt;expr&gt; then &lt;stmts&gt; (else &lt;stmts&gt;)? end</c></summary>
public sealed class IfStatement(
    Expression condition,
    Statement thenBranch,
    Statement? elseBranch,
    SourceLocation location) : Statement(location)
{
    public Expression Condition { get; } = condition;

    public Statement ThenBranch { get; } = thenBranch;

    /// <summary>The <c>else</c> branch, or <see langword="null"/> if there is none.</summary>
    public Statement? ElseBranch { get; } = elseBranch;
}

/// <summary><c>while &lt;expr&gt; do &lt;stmts&gt; end</c></summary>
public sealed class WhileStatement(Expression condition, Statement body, SourceLocation location)
    : Statement(location)
{
    public Expression Condition { get; } = condition;

    public Statement Body { get; } = body;
}

/// <summary><c>break</c> — leaves the innermost enclosing loop.</summary>
public sealed class BreakStatement(SourceLocation location) : Statement(location);

/// <summary><c>continue</c> — starts the next iteration of the innermost enclosing loop.</summary>
public sealed class ContinueStatement(SourceLocation location) : Statement(location);

// --------------------------------------------------------------- expressions

public abstract class Expression(SourceLocation location) : SyntaxNode(location)
{
    private CacaType? _type;

    /// <summary>
    /// The type assigned by <see cref="Binding.TypeChecker"/>.
    /// </summary>
    /// <remarks>
    /// The original code computed types by overriding <c>object.GetType()</c> on
    /// AST nodes, which shadowed a member every .NET type has and forced the
    /// symbol table to be a public static field so nodes could reach it.
    /// </remarks>
    public CacaType Type
    {
        get => _type ?? throw new InvalidOperationException(
            $"the type of this {GetType().Name} has not been resolved; run the type checker first");
        internal set => _type = value;
    }

    public bool HasType => _type is not null;
}

/// <summary>An integer or string literal.</summary>
public sealed class LiteralExpression(object value, CacaType type, SourceLocation location) : Expression(location)
{
    public object Value { get; } = value;

    /// <summary>The literal's type, known without any analysis.</summary>
    public CacaType LiteralType { get; } = type;

    public int IntValue => (int)Value;

    public string StringValue => (string)Value;

    public bool BoolValue => (bool)Value;
}

/// <summary>A reference to a variable.</summary>
public sealed class VariableExpression(string name, SourceLocation location) : Expression(location)
{
    public string Name { get; } = name;
}

/// <summary><c>( &lt;expr&gt; )</c> — retained so error locations cover the parentheses.</summary>
public sealed class ParenthesizedExpression(Expression expression, SourceLocation location) : Expression(location)
{
    public Expression Expression { get; } = expression;
}

/// <summary><c>- &lt;expr&gt;</c> or <c>+ &lt;expr&gt;</c></summary>
public sealed class UnaryExpression(UnaryOperator op, Expression operand, SourceLocation location)
    : Expression(location)
{
    public UnaryOperator Operator { get; } = op;

    public Expression Operand { get; } = operand;
}

/// <summary><c>&lt;expr&gt; &lt;arith_op&gt; &lt;expr&gt;</c></summary>
public sealed class BinaryExpression(
    Expression left,
    BinaryOperator op,
    Expression right,
    SourceLocation location) : Expression(location)
{
    public Expression Left { get; } = left;

    public BinaryOperator Operator { get; } = op;

    public Expression Right { get; } = right;
}

public enum UnaryOperator
{
    Identity,
    Negate,
    Not,
}

public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    LogicalAnd,
    LogicalOr,
}

public static class OperatorExtensions
{
    public static string Describe(this BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.LogicalAnd => "&&",
        BinaryOperator.LogicalOr => "||",
        _ => op.ToString(),
    };

    public static string Describe(this UnaryOperator op) => op switch
    {
        UnaryOperator.Identity => "+",
        UnaryOperator.Negate => "-",
        UnaryOperator.Not => "!",
        _ => op.ToString(),
    };
}
