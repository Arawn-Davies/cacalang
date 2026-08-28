using System.Globalization;
using Caca.Binding;
using Caca.Syntax;

namespace Caca.Runtime;

/// <summary>
/// Executes a type-checked program directly, without emitting an assembly.
/// </summary>
/// <remarks>
/// This backend exists so that <c>caca run</c> behaves identically on Windows,
/// macOS and Linux and so that the language can be tested in-process. It is
/// expected to agree with <see cref="Emit.IlEmitter"/> on every program.
/// </remarks>
public sealed class Interpreter
{
    private readonly Dictionary<string, object> _variables = new(StringComparer.Ordinal);
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public Interpreter(TextReader? input = null, TextWriter? output = null)
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    /// <summary>Runs a program that has already passed the type checker.</summary>
    /// <exception cref="CacaRuntimeException">The program failed while running.</exception>
    public void Run(BlockStatement program) => Execute(program);

    private void Execute(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    Execute(child);
                }

                break;

            case VariableDeclaration declaration:
                _variables[declaration.Name] = Evaluate(declaration.Initializer);
                break;

            case AssignmentStatement assignment:
                _variables[assignment.Name] = Evaluate(assignment.Value);
                break;

            case PrintStatement print:
                _output.WriteLine(Stringify(Evaluate(print.Expression)));
                break;

            case ReadStatement read:
                _variables[read.Name] = ReadValue(read.Type);
                break;

            case ForStatement loop:
                ExecuteFor(loop);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void ExecuteFor(ForStatement loop)
    {
        var i = (int)Evaluate(loop.From);

        // The upper bound is evaluated once, before the loop, rather than on
        // every iteration as the original emitter did.
        var to = (int)Evaluate(loop.To);

        if (i > to)
        {
            return;
        }

        while (true)
        {
            _variables[loop.Name] = i;
            Execute(loop.Body);

            // The body may reassign the loop variable, so read it back before
            // testing the bound. Testing before incrementing also means a loop
            // ending at int.MaxValue terminates instead of wrapping around.
            i = (int)_variables[loop.Name];

            if (i >= to)
            {
                return;
            }

            i++;
        }
    }

    private object ReadValue(CacaType type)
    {
        var line = _input.ReadLine() ?? string.Empty;

        if (type == CacaType.String)
        {
            return line;
        }

        if (!int.TryParse(line.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw new CacaRuntimeException($"'{line}' is not an integer");
        }

        return value;
    }

    private object Evaluate(Expression expression) => expression switch
    {
        LiteralExpression literal => literal.Value,
        ParenthesizedExpression parenthesized => Evaluate(parenthesized.Expression),
        VariableExpression variable => _variables[variable.Name],
        UnaryExpression unary => EvaluateUnary(unary),
        BinaryExpression binary => EvaluateBinary(binary),
        _ => throw new ArgumentOutOfRangeException(
            nameof(expression), expression, $"unhandled expression {expression.GetType().Name}"),
    };

    private object EvaluateUnary(UnaryExpression unary)
    {
        var operand = (int)Evaluate(unary.Operand);
        return unary.Operator switch
        {
            UnaryOperator.Identity => operand,
            UnaryOperator.Negate => unchecked(-operand),
            _ => throw new ArgumentOutOfRangeException(nameof(unary), unary.Operator, "unhandled unary operator"),
        };
    }

    private object EvaluateBinary(BinaryExpression binary)
    {
        var left = Evaluate(binary.Left);
        var right = Evaluate(binary.Right);

        if (binary.Type == CacaType.String)
        {
            return Stringify(left) + Stringify(right);
        }

        var a = (int)left;
        var b = (int)right;

        switch (binary.Operator)
        {
            case BinaryOperator.Add:
                return unchecked(a + b);
            case BinaryOperator.Subtract:
                return unchecked(a - b);
            case BinaryOperator.Multiply:
                return unchecked(a * b);
            case BinaryOperator.Divide:
                if (b == 0)
                {
                    throw new CacaRuntimeException("attempted to divide by zero");
                }

                return unchecked(a / b);
            default:
                throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator");
        }
    }

    private static string Stringify(object value) =>
        value is int i ? i.ToString(CultureInfo.InvariantCulture) : (string)value;
}
