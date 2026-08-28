using Gfn.Diagnostics;
using Gfn.Syntax;

namespace Gfn.Binding;

/// <summary>
/// Resolves the type of every expression and reports semantic errors before
/// any code is produced.
/// </summary>
/// <remarks>
/// The original compiler had no separate analysis pass. Types were computed
/// lazily from inside the emitter through a <c>public static</c> symbol table,
/// and arithmetic simply assumed the type of its left operand, so
/// <c>print x + 1</c> and <c>"a" + "b"</c> both emitted IL the runtime rejects
/// or that silently produced garbage.
/// </remarks>
public sealed class TypeChecker
{
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, GfnType> _symbols = new(StringComparer.Ordinal);

    private TypeChecker(DiagnosticBag diagnostics) => _diagnostics = diagnostics;

    /// <summary>Type-checks <paramref name="program"/> in place.</summary>
    public static void Check(BlockStatement program, DiagnosticBag diagnostics)
    {
        var checker = new TypeChecker(diagnostics);
        checker.CheckStatement(program);
    }

    private void CheckStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    CheckStatement(child);
                }

                break;

            case VariableDeclaration declaration:
                CheckDeclaration(declaration);
                break;

            case AssignmentStatement assignment:
                CheckAssignment(assignment);
                break;

            case PrintStatement print:
                CheckExpression(print.Expression);
                break;

            case ReadStatement read:
                CheckRead(read);
                break;

            case ForStatement loop:
                CheckFor(loop);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void CheckDeclaration(VariableDeclaration declaration)
    {
        var type = CheckExpression(declaration.Initializer);

        if (_symbols.ContainsKey(declaration.Name))
        {
            _diagnostics.Report(
                DiagnosticCode.VariableAlreadyDeclared,
                declaration.Location,
                $"variable '{declaration.Name}' is already declared");
            return;
        }

        // A failed initializer still declares the name, which keeps every later
        // use of it from producing a second, redundant error.
        _symbols[declaration.Name] = type;
    }

    private void CheckAssignment(AssignmentStatement assignment)
    {
        var valueType = CheckExpression(assignment.Value);

        if (!TryLookup(assignment.Name, assignment.Location, out var declaredType))
        {
            return;
        }

        if (valueType != GfnType.Error && declaredType != GfnType.Error && valueType != declaredType)
        {
            _diagnostics.Report(
                DiagnosticCode.TypeMismatch,
                assignment.Location,
                $"cannot assign a value of type {valueType.Describe()} to '{assignment.Name}', " +
                $"which is of type {declaredType.Describe()}");
        }
    }

    private void CheckRead(ReadStatement read)
    {
        if (!TryLookup(read.Name, read.Location, out var declaredType))
        {
            return;
        }

        if (declaredType != GfnType.Error && declaredType != read.Type)
        {
            var keyword = read.Type == GfnType.Int ? "read_int" : "read_string";
            _diagnostics.Report(
                DiagnosticCode.TypeMismatch,
                read.Location,
                $"'{keyword}' stores a value of type {read.Type.Describe()}, but '{read.Name}' " +
                $"is of type {declaredType.Describe()}");
        }
    }

    private void CheckFor(ForStatement loop)
    {
        RequireInt(loop.From, "the lower bound of a 'for' loop", DiagnosticCode.LoopBoundMustBeInt);
        RequireInt(loop.To, "the upper bound of a 'for' loop", DiagnosticCode.LoopBoundMustBeInt);

        if (_symbols.TryGetValue(loop.Name, out var existing))
        {
            if (existing != GfnType.Error && existing != GfnType.Int)
            {
                _diagnostics.Report(
                    DiagnosticCode.LoopVariableMustBeInt,
                    loop.Location,
                    $"the loop variable '{loop.Name}' must be of type int, but it is of type {existing.Describe()}");
            }
        }
        else
        {
            // A loop over an undeclared name declares it, so `for i = 1 to 3`
            // works without a preceding `var i = 0;`.
            _symbols[loop.Name] = GfnType.Int;
            loop.DeclaresVariable = true;
        }

        CheckStatement(loop.Body);
    }

    private void RequireInt(Expression expression, string role, DiagnosticCode code)
    {
        var type = CheckExpression(expression);

        if (type is not (GfnType.Int or GfnType.Error))
        {
            _diagnostics.Report(code, expression.Location, $"{role} must be of type int, but this is {type.Describe()}");
        }
    }

    private bool TryLookup(string name, SourceLocation location, out GfnType type)
    {
        if (_symbols.TryGetValue(name, out type))
        {
            return true;
        }

        _diagnostics.Report(
            DiagnosticCode.UndeclaredVariable,
            location,
            $"'{name}' is not declared; use 'var {name} = ...' before using it");

        type = GfnType.Error;
        return false;
    }

    private GfnType CheckExpression(Expression expression)
    {
        var type = expression switch
        {
            LiteralExpression literal => literal.LiteralType,
            ParenthesizedExpression parenthesized => CheckExpression(parenthesized.Expression),
            VariableExpression variable => CheckVariable(variable),
            UnaryExpression unary => CheckUnary(unary),
            BinaryExpression binary => CheckBinary(binary),
            _ => throw new ArgumentOutOfRangeException(
                nameof(expression), expression, $"unhandled expression {expression.GetType().Name}"),
        };

        expression.Type = type;
        return type;
    }

    private GfnType CheckVariable(VariableExpression variable) =>
        TryLookup(variable.Name, variable.Location, out var type) ? type : GfnType.Error;

    private GfnType CheckUnary(UnaryExpression unary)
    {
        var operandType = CheckExpression(unary.Operand);

        if (operandType is GfnType.Int or GfnType.Error)
        {
            return operandType;
        }

        _diagnostics.Report(
            DiagnosticCode.OperatorNotDefined,
            unary.Location,
            $"unary operator '{unary.Operator.Describe()}' is not defined for type {operandType.Describe()}");

        return GfnType.Error;
    }

    private GfnType CheckBinary(BinaryExpression binary)
    {
        var left = CheckExpression(binary.Left);
        var right = CheckExpression(binary.Right);

        if (left == GfnType.Error || right == GfnType.Error)
        {
            return GfnType.Error;
        }

        if (left == GfnType.Int && right == GfnType.Int)
        {
            return GfnType.Int;
        }

        // '+' doubles as string concatenation, and concatenating a string with
        // an int converts the int. Every other combination is an error rather
        // than, as before, unverifiable IL.
        if (binary.Operator == BinaryOperator.Add && (left == GfnType.String || right == GfnType.String))
        {
            return GfnType.String;
        }

        _diagnostics.Report(
            DiagnosticCode.OperatorNotDefined,
            binary.Location,
            $"operator '{binary.Operator.Describe()}' is not defined for types " +
            $"{left.Describe()} and {right.Describe()}");

        return GfnType.Error;
    }
}
