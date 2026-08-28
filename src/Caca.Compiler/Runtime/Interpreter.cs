using System.Globalization;
using System.Runtime.ExceptionServices;
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
    /// <summary>How a statement finished, and with what value.</summary>
    /// <remarks>
    /// Control flow is threaded through return values rather than exceptions,
    /// which keeps <c>break</c> and <c>continue</c> as cheap as the loops that
    /// contain them.
    /// </remarks>
    private enum Flow
    {
        Normal,
        Break,
        Continue,
        Return,
    }

    /// <summary>
    /// How deep calls may nest before the interpreter gives up.
    /// </summary>
    /// <remarks>
    /// Runaway recursion would otherwise overflow the CLR stack, which cannot
    /// be caught and takes the whole process down. This turns it into an error
    /// the program's author can read.
    /// </remarks>
    private const int MaxCallDepth = 2000;

    /// <summary>
    /// The stack the program is given to run on.
    /// </summary>
    /// <remarks>
    /// A deep call chain in the interpreted program becomes a far deeper chain
    /// of Execute and Evaluate frames. How much stack a thread has varies by
    /// platform and by host — a test runner's worker thread has a small
    /// fraction of what a main thread has — so the program is run on a thread
    /// whose stack this compiler chooses. Without that, <see cref="MaxCallDepth"/>
    /// is reached first on some machines and the CLR stack overflows first on
    /// others, and a stack overflow cannot be caught: it takes the process down.
    /// </remarks>
    private const int StackSize = 16 * 1024 * 1024;

    /// <summary>What <c>read_float</c> accepts.</summary>
    private const NumberStyles FloatStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private readonly IReadOnlyDictionary<string, FunctionSymbol> _functions;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    /// <summary>The locals of the call in progress; each call gets its own.</summary>
    private Dictionary<string, object> _variables = new(StringComparer.Ordinal);

    private object? _returnValue;
    private int _depth;

    public Interpreter(
        IReadOnlyDictionary<string, FunctionSymbol>? functions = null,
        TextReader? input = null,
        TextWriter? output = null)
    {
        _functions = functions ?? new Dictionary<string, FunctionSymbol>();
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    /// <summary>Runs a program that has already passed the type checker.</summary>
    /// <exception cref="CacaRuntimeException">The program failed while running.</exception>
    public void Run(CompilationUnit program)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    Execute(program.TopLevel);
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            },
            StackSize);

        thread.Start();
        thread.Join();

        // Rethrow on the caller's thread with the original stack trace intact.
        failure?.Throw();
    }

    private Flow Execute(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    var flow = Execute(child);

                    if (flow != Flow.Normal)
                    {
                        return flow;
                    }
                }

                return Flow.Normal;

            case VariableDeclaration declaration:
                _variables[declaration.Name] = Evaluate(declaration.Initializer);
                return Flow.Normal;

            case AssignmentStatement assignment:
                _variables[assignment.Name] = Evaluate(assignment.Value);
                return Flow.Normal;

            case PrintStatement print:
                _output.WriteLine(Stringify(Evaluate(print.Expression)));
                return Flow.Normal;

            case ReadStatement read:
                _variables[read.Name] = ReadValue(read.Type);
                return Flow.Normal;

            case IfStatement conditional:
                if ((bool)Evaluate(conditional.Condition))
                {
                    return Execute(conditional.ThenBranch);
                }

                return conditional.ElseBranch is null ? Flow.Normal : Execute(conditional.ElseBranch);

            case WhileStatement loop:
                return ExecuteWhile(loop);

            case ForStatement loop:
                return ExecuteFor(loop);

            case ReturnStatement returned:
                _returnValue = returned.Value is null ? null : Evaluate(returned.Value);
                return Flow.Return;

            case CallStatement call:
                Evaluate(call.Call);
                return Flow.Normal;

            case BreakStatement:
                return Flow.Break;

            case ContinueStatement:
                return Flow.Continue;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private Flow ExecuteWhile(WhileStatement loop)
    {
        while ((bool)Evaluate(loop.Condition))
        {
            var flow = Execute(loop.Body);

            if (flow == Flow.Break)
            {
                break;
            }

            // A return travels on out through the loop.
            if (flow == Flow.Return)
            {
                return flow;
            }
        }

        return Flow.Normal;
    }

    private Flow ExecuteFor(ForStatement loop)
    {
        var i = (int)Evaluate(loop.From);

        // The upper bound is evaluated once, before the loop, rather than on
        // every iteration as the original emitter did.
        var to = (int)Evaluate(loop.To);

        if (i > to)
        {
            return Flow.Normal;
        }

        while (true)
        {
            _variables[loop.Name] = i;
            var flow = Execute(loop.Body);

            if (flow == Flow.Break)
            {
                return Flow.Normal;
            }

            if (flow == Flow.Return)
            {
                return flow;
            }

            // The body may reassign the loop variable, so read it back before
            // testing the bound. Testing before incrementing also means a loop
            // ending at int.MaxValue terminates instead of wrapping around.
            i = (int)_variables[loop.Name];

            if (i >= to)
            {
                return Flow.Normal;
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

        var text = line.Trim();

        if (type == CacaType.Float)
        {
            // Parsing is invariant so input means the same thing everywhere.
            if (!double.TryParse(text, FloatStyles, CultureInfo.InvariantCulture, out var real))
            {
                throw new CacaRuntimeException($"'{line}' is not a number");
            }

            return real;
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw new CacaRuntimeException($"'{line}' is not an integer");
        }

        return value;
    }

    private object Evaluate(Expression expression) => Widen(expression, EvaluateCore(expression));

    /// <summary>Applies the widening the type checker decided on.</summary>
    private static object Widen(Expression expression, object value) =>
        expression.ConvertedTo == CacaType.Float && value is int i ? (double)i : value;

    private object EvaluateCore(Expression expression) => expression switch
    {
        LiteralExpression literal => literal.Value,
        ParenthesizedExpression parenthesized => Evaluate(parenthesized.Expression),
        VariableExpression variable => _variables[variable.Name],
        UnaryExpression unary => EvaluateUnary(unary),
        BinaryExpression binary => EvaluateBinary(binary),
        CallExpression call => Invoke(call),
        _ => throw new ArgumentOutOfRangeException(
            nameof(expression), expression, $"unhandled expression {expression.GetType().Name}"),
    };

    /// <summary>Calls a function, giving it a fresh set of locals.</summary>
    private object Invoke(CallExpression call)
    {
        var function = call.Target ?? _functions[call.Name];

        // Arguments are evaluated in the caller's scope, before it is swapped out.
        var arguments = call.Arguments.Select(Evaluate).ToArray();

        if (++_depth > MaxCallDepth)
        {
            _depth--;
            throw new CacaRuntimeException(
                $"call stack depth of {MaxCallDepth} exceeded; '{function.Name}' is most likely recursing forever");
        }

        var caller = _variables;
        var frame = new Dictionary<string, object>(StringComparer.Ordinal);

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            frame[function.Parameters[i].Name] = arguments[i];
        }

        _variables = frame;
        _returnValue = null;

        try
        {
            Execute(function.Declaration.Body);

            // The type checker has already proved that a function with a return
            // type returns on every path.
            return _returnValue ?? Nothing;
        }
        finally
        {
            _variables = caller;
            _returnValue = null;
            _depth--;
        }
    }

    /// <summary>The value standing in for the result of a function that returns nothing.</summary>
    private static readonly object Nothing = new();

    private object EvaluateUnary(UnaryExpression unary)
    {
        var operand = Evaluate(unary.Operand);

        return unary.Operator switch
        {
            UnaryOperator.Identity => operand,
            UnaryOperator.Negate => Negate(operand),
            UnaryOperator.Not => !(bool)operand,
            _ => throw new ArgumentOutOfRangeException(nameof(unary), unary.Operator, "unhandled unary operator"),
        };
    }

    /// <summary>
    /// Negates a number, keeping it the type it already was.
    /// </summary>
    /// <remarks>
    /// Written with separate returns so that each result converts to object on
    /// its own. A conditional or a switch expression would give the arms one
    /// common type, which for an int and a double is double, quietly turning
    /// every negated int into a float.
    /// </remarks>
    private static object Negate(object operand)
    {
        if (operand is double d)
        {
            return -d;
        }

        return unchecked(-(int)operand);
    }

    private object EvaluateBinary(BinaryExpression binary)
    {
        // '&&' and '||' evaluate their right operand only when the left one
        // does not already decide the result.
        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                return (bool)Evaluate(binary.Left) && (bool)Evaluate(binary.Right);
            case BinaryOperator.LogicalOr:
                return (bool)Evaluate(binary.Left) || (bool)Evaluate(binary.Right);
        }

        var left = Evaluate(binary.Left);
        var right = Evaluate(binary.Right);

        switch (binary.Operator)
        {
            case BinaryOperator.Add when binary.Type == CacaType.String:
                return Stringify(left) + Stringify(right);

            case BinaryOperator.Equal when left is not double && right is not double:
                return Equals(left, right);
            case BinaryOperator.NotEqual when left is not double && right is not double:
                return !Equals(left, right);
        }

        if (left is double || right is double)
        {
            return FloatOperation(binary, Convert.ToDouble(left), Convert.ToDouble(right));
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
                return b == 0 ? throw new CacaRuntimeException("attempted to divide by zero") : unchecked(a / b);
            case BinaryOperator.Modulo:
                return b == 0 ? throw new CacaRuntimeException("attempted to divide by zero") : unchecked(a % b);
            case BinaryOperator.Less:
                return a < b;
            case BinaryOperator.LessOrEqual:
                return a <= b;
            case BinaryOperator.Greater:
                return a > b;
            case BinaryOperator.GreaterOrEqual:
                return a >= b;
            default:
                throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator");
        }
    }

    /// <summary>
    /// Arithmetic and comparison on floats.
    /// </summary>
    /// <remarks>
    /// Division by zero is not an error here: IEEE 754 defines it, and the
    /// answer is an infinity. Integer division has no such answer, which is why
    /// that case still fails.
    /// </remarks>
    private static object FloatOperation(BinaryExpression binary, double a, double b) => binary.Operator switch
    {
        BinaryOperator.Add => a + b,
        BinaryOperator.Subtract => a - b,
        BinaryOperator.Multiply => a * b,
        BinaryOperator.Divide => a / b,
        BinaryOperator.Modulo => a % b,
        BinaryOperator.Less => a < b,
        BinaryOperator.LessOrEqual => a <= b,
        BinaryOperator.Greater => a > b,
        BinaryOperator.GreaterOrEqual => a >= b,
        BinaryOperator.Equal => a == b,
        BinaryOperator.NotEqual => a != b,
        _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator"),
    };

    /// <summary>Renders a value the way <c>print</c> does.</summary>
    private static string Stringify(object value) => value switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        double d => FloatFormat.ToText(d),
        _ => (string)value,
    };
}
