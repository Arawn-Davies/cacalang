using Gfn.Binding;
using Gfn.Diagnostics;

namespace Gfn.Syntax;

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
public sealed class ReadStatement(string name, GfnType type, SourceLocation location) : Statement(location)
{
    public string Name { get; } = name;

    /// <summary>The type of value read: <see cref="GfnType.Int"/> or <see cref="GfnType.String"/>.</summary>
    public GfnType Type { get; } = type;
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

// --------------------------------------------------------------- expressions

public abstract class Expression(SourceLocation location) : SyntaxNode(location)
{
    private GfnType? _type;

    /// <summary>
    /// The type assigned by <see cref="Binding.TypeChecker"/>.
    /// </summary>
    /// <remarks>
    /// The original code computed types by overriding <c>object.GetType()</c> on
    /// AST nodes, which shadowed a member every .NET type has and forced the
    /// symbol table to be a public static field so nodes could reach it.
    /// </remarks>
    public GfnType Type
    {
        get => _type ?? throw new InvalidOperationException(
            $"the type of this {GetType().Name} has not been resolved; run the type checker first");
        internal set => _type = value;
    }

    public bool HasType => _type is not null;
}

/// <summary>An integer or string literal.</summary>
public sealed class LiteralExpression(object value, GfnType type, SourceLocation location) : Expression(location)
{
    public object Value { get; } = value;

    /// <summary>The literal's type, known without any analysis.</summary>
    public GfnType LiteralType { get; } = type;

    public int IntValue => (int)Value;

    public string StringValue => (string)Value;
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
}

public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
}

public static class OperatorExtensions
{
    public static string Describe(this BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => op.ToString(),
    };

    public static string Describe(this UnaryOperator op) => op switch
    {
        UnaryOperator.Identity => "+",
        UnaryOperator.Negate => "-",
        _ => op.ToString(),
    };
}
