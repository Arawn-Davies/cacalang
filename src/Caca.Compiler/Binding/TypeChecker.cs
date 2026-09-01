using System.Reflection;
using Caca.Diagnostics;
using Caca.Syntax;

namespace Caca.Binding;

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
    private readonly IReadOnlyList<Assembly> _referencedAssemblies;
    private readonly Dictionary<string, FunctionSymbol> _functions = new(StringComparer.Ordinal);

    /// <summary>
    /// Every place a name is used, and what it refers to, in source order.
    /// </summary>
    /// <remarks>
    /// This is what the language server answers hover, go-to-definition and
    /// document-symbol requests from. The compiler itself does not need it.
    /// </remarks>
    private readonly List<SymbolReference> _references = [];

    private Dictionary<string, VariableSymbol> _symbols = new(StringComparer.Ordinal);
    private FunctionSymbol? _currentFunction;
    private int _loopDepth;

    private TypeChecker(DiagnosticBag diagnostics, IReadOnlyList<Assembly>? references)
    {
        _diagnostics = diagnostics;
        _referencedAssemblies = references ?? [];
    }

    /// <summary>Type-checks <paramref name="unit"/> in place.</summary>
    /// <param name="references">
    /// Assemblies extern targets are resolved against, before the ones already
    /// loaded into this process.
    /// </param>
    public static BindingResult Check(
        CompilationUnit unit,
        DiagnosticBag diagnostics,
        IReadOnlyList<Assembly>? references = null)
    {
        var checker = new TypeChecker(diagnostics, references);
        checker.CheckCompilationUnit(unit);
        return new BindingResult(checker._functions, checker._references);
    }

    private void Reference(SourceLocation location, ISymbol symbol, bool isDefinition = false) =>
        _references.Add(new SymbolReference(location, symbol, isDefinition));

    /// <summary>
    /// Records that an expression's value is widened to <paramref name="target"/>,
    /// and reports whether it is usable there at all.
    /// </summary>
    private static bool Accepts(Expression expression, CacaType target)
    {
        if (expression.Type == CacaType.Error || target == CacaType.Error)
        {
            return true;
        }

        if (!expression.Type.ConvertsTo(target))
        {
            return false;
        }

        if (expression.Type != target)
        {
            expression.ConvertedTo = target;
        }

        return true;
    }

    private void CheckCompilationUnit(CompilationUnit unit)
    {
        // Signatures are collected before any body is checked, so a function
        // may call one declared further down the file, or call itself.
        foreach (var function in unit.Functions)
        {
            DeclareFunction(function);
        }

        foreach (var function in unit.Functions)
        {
            CheckFunctionBody(function);
        }

        // Top-level code has its own scope and cannot see a function's locals,
        // nor a function the top-level variables.
        _symbols = new Dictionary<string, VariableSymbol>(StringComparer.Ordinal);
        _currentFunction = null;
        CheckStatement(unit.TopLevel);
    }

    private void DeclareFunction(FunctionDeclaration function)
    {
        var parameters = new List<(string Name, CacaType Type)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in function.Parameters)
        {
            var type = ResolveType(parameter.Type);

            if (!seen.Add(parameter.Name))
            {
                _diagnostics.Report(
                    DiagnosticCode.VariableAlreadyDeclared,
                    parameter.Location,
                    $"'{function.Name}' already has a parameter named '{parameter.Name}'");
            }

            parameters.Add((parameter.Name, type));
        }

        var returnType = function.ReturnType is null ? CacaType.Void : ResolveType(function.ReturnType);
        var symbol = new FunctionSymbol(function.Name, parameters, returnType, function, function.NameLocation);

        if (function.IsExtern)
        {
            symbol.ExternMethod = ResolveExtern(function, parameters, returnType);
        }

        if (_functions.TryAdd(function.Name, symbol))
        {
            Reference(function.NameLocation, symbol, isDefinition: true);
        }
        else
        {
            _diagnostics.Report(
                DiagnosticCode.FunctionAlreadyDeclared,
                function.Location,
                $"a function named '{function.Name}' is already declared");
        }
    }

    private void CheckFunctionBody(FunctionDeclaration function)
    {
        // An extern function has no body; its signature was checked against the
        // .NET method when it was declared.
        if (function.IsExtern)
        {
            return;
        }

        if (!_functions.TryGetValue(function.Name, out var symbol) || symbol.Declaration != function)
        {
            // A duplicate declaration; the first one owns the name.
            return;
        }

        _symbols = new Dictionary<string, VariableSymbol>(StringComparer.Ordinal);
        _currentFunction = symbol;

        for (var i = 0; i < symbol.Parameters.Count; i++)
        {
            var declaration = function.Parameters[i];
            var parameter = new VariableSymbol(
                declaration.Name, symbol.Parameters[i].Type, declaration.NameLocation, VariableKind.Parameter);

            _symbols[declaration.Name] = parameter;
            Reference(declaration.NameLocation, parameter, isDefinition: true);
        }

        CheckStatement(function.Body);

        // Falling off the end of a function that owes a value is an error, not
        // a silently returned zero.
        if (symbol.ReturnType != CacaType.Void && !AlwaysReturns(function.Body))
        {
            _diagnostics.Report(
                DiagnosticCode.NotAllPathsReturn,
                function.Location,
                $"'{function.Name}' must return a value of type {symbol.ReturnType.Describe()} " +
                "on every path, but at least one path reaches the end without returning");
        }
    }

    /// <summary>
    /// Resolves an extern function's target to the .NET method it names,
    /// reporting a diagnostic and returning <see langword="null"/> when it
    /// cannot.
    /// </summary>
    /// <remarks>
    /// The target is <c>"Namespace.Type.Method"</c>. A public static method
    /// whose signature matches the declared one is preferred; failing that, an
    /// instance method whose receiver is the first declared parameter, which is
    /// what makes <c>System.String.Substring</c> callable. Instance binding is
    /// limited to reference-type receivers: a value-type receiver would need
    /// its address rather than its value, and no interesting target needs it.
    /// </remarks>
    private MethodInfo? ResolveExtern(
        FunctionDeclaration function,
        IReadOnlyList<(string Name, CacaType Type)> parameters,
        CacaType returnType)
    {
        // A signature that failed to resolve has already been diagnosed.
        if (returnType == CacaType.Error || parameters.Any(p => p.Type == CacaType.Error))
        {
            return null;
        }

        var target = function.ExternTarget!;
        var location = function.ExternTargetLocation!.Value;
        var lastDot = target.LastIndexOf('.');

        if (lastDot <= 0 || lastDot == target.Length - 1)
        {
            _diagnostics.Report(
                DiagnosticCode.ExternTargetInvalid,
                location,
                $"'{target}' does not name a .NET method; write it as \"Namespace.Type.Method\"");
            return null;
        }

        var typeName = target[..lastDot];
        var methodName = target[(lastDot + 1)..];
        var type = FindType(typeName);

        if (type is null)
        {
            _diagnostics.Report(
                DiagnosticCode.ExternTargetNotFound,
                location,
                $"no type named '{typeName}' was found; " +
                "it must live in the core library, a loaded assembly, or one passed with --ref");
            return null;
        }

        var parameterTypes = parameters.Select(p => p.Type.ToClrType()).ToArray();
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, parameterTypes);

        // An instance method is callable when the first declared parameter is
        // the receiver, so `substring(s: string, start: int)` finds
        // System.String.Substring(int).
        if (method is null && parameterTypes.Length > 0
            && parameterTypes[0] == type && !type.IsValueType)
        {
            method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, parameterTypes[1..]);
        }

        if (method is null)
        {
            var signature = string.Join(", ", parameters.Select(p => p.Type.Describe()));
            _diagnostics.Report(
                DiagnosticCode.ExternTargetNotFound,
                location,
                $"'{typeName}' has no public method '{methodName}' taking ({signature})");
            return null;
        }

        if (method.ReturnType != returnType.ToClrType())
        {
            _diagnostics.Report(
                DiagnosticCode.ExternReturnTypeMismatch,
                location,
                $"'{target}' returns {method.ReturnType.Name}, " +
                $"but '{function.Name}' is declared to return {returnType.Describe()}");
            return null;
        }

        return method;
    }

    /// <summary>
    /// Finds a type by its namespace-qualified name: in the referenced
    /// assemblies first, then the core library, then whatever else is loaded.
    /// </summary>
    private Type? FindType(string name)
    {
        foreach (var assembly in _referencedAssemblies)
        {
            if (assembly.GetType(name) is { } fromReference)
            {
                return fromReference;
            }
        }

        if (Type.GetType(name) is { } fromCore)
        {
            return fromCore;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && assembly.GetType(name) is { } fromLoaded)
            {
                return fromLoaded;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a statement returns on every path through it.
    /// </summary>
    /// <remarks>
    /// Loops are deliberately not counted: proving that a loop always runs, and
    /// always returns when it does, is more analysis than this needs.
    /// </remarks>
    private static bool AlwaysReturns(Statement statement) => statement switch
    {
        ReturnStatement => true,
        BlockStatement block => block.Statements.Any(AlwaysReturns),
        IfStatement conditional => conditional.ElseBranch is not null
            && AlwaysReturns(conditional.ThenBranch)
            && AlwaysReturns(conditional.ElseBranch),
        _ => false,
    };

    private CacaType ResolveType(TypeReference reference)
    {
        var type = CacaTypeExtensions.Parse(reference.Name);

        if (type is null)
        {
            _diagnostics.Report(
                DiagnosticCode.UnknownType,
                reference.Location,
                $"'{reference.Name}' is not a type; the types are int, string and bool");

            reference.Type = CacaType.Error;
            return CacaType.Error;
        }

        reference.Type = type.Value;
        return type.Value;
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
                RequireValue(print.Expression, "'print'");
                break;

            case ReadStatement read:
                CheckRead(read);
                break;

            case ForStatement loop:
                CheckFor(loop);
                break;

            case IfStatement conditional:
                RequireBool(conditional.Condition, "the condition of an 'if'");
                CheckStatement(conditional.ThenBranch);

                if (conditional.ElseBranch is not null)
                {
                    CheckStatement(conditional.ElseBranch);
                }

                break;

            case WhileStatement loop:
                RequireBool(loop.Condition, "the condition of a 'while'");
                _loopDepth++;
                CheckStatement(loop.Body);
                _loopDepth--;
                break;

            case ReturnStatement returned:
                CheckReturn(returned);
                break;

            case CallStatement call:
                CheckExpression(call.Call);
                break;

            case BreakStatement:
                RequireInsideLoop(statement, "break");
                break;

            case ContinueStatement:
                RequireInsideLoop(statement, "continue");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void CheckReturn(ReturnStatement returned)
    {
        if (_currentFunction is null)
        {
            if (returned.Value is not null)
            {
                CheckExpression(returned.Value);
            }

            _diagnostics.Report(
                DiagnosticCode.ReturnOutsideFunction,
                returned.Location,
                "'return' can only appear inside a function");
            return;
        }

        var expected = _currentFunction.ReturnType;

        if (returned.Value is null)
        {
            if (expected != CacaType.Void)
            {
                _diagnostics.Report(
                    DiagnosticCode.TypeMismatch,
                    returned.Location,
                    $"'{_currentFunction.Name}' must return a value of type {expected.Describe()}");
            }

            return;
        }

        var actual = CheckExpression(returned.Value);

        if (expected == CacaType.Void)
        {
            _diagnostics.Report(
                DiagnosticCode.TypeMismatch,
                returned.Location,
                $"'{_currentFunction.Name}' returns nothing, so 'return' cannot be given a value");
            return;
        }

        if (!Accepts(returned.Value, expected))
        {
            _diagnostics.Report(
                DiagnosticCode.TypeMismatch,
                returned.Location,
                $"'{_currentFunction.Name}' returns {expected.Describe()}, " +
                $"but this returns {actual.Describe()}");
        }
    }

    private void CheckDeclaration(VariableDeclaration declaration)
    {
        var type = CheckExpression(declaration.Initializer);

        if (declaration.DeclaredType is not null)
        {
            var written = ResolveType(declaration.DeclaredType);

            if (!Accepts(declaration.Initializer, written))
            {
                _diagnostics.Report(
                    DiagnosticCode.TypeMismatch,
                    declaration.Location,
                    $"'{declaration.Name}' is declared as {written.Describe()}, " +
                    $"but its value is of type {type.Describe()}");
            }

            // The written type wins, so one mistake does not cascade.
            type = written;
        }
        else if (type == CacaType.Void)
        {
            _diagnostics.Report(
                DiagnosticCode.NoValueProduced,
                declaration.Initializer.Location,
                $"'{declaration.Name}' cannot be initialized with an expression that produces no value");

            type = CacaType.Error;
        }

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
        var symbol = new VariableSymbol(declaration.Name, type, declaration.NameLocation, VariableKind.Local);
        _symbols[declaration.Name] = symbol;
        Reference(declaration.NameLocation, symbol, isDefinition: true);
    }

    private void CheckAssignment(AssignmentStatement assignment)
    {
        var valueType = CheckExpression(assignment.Value);

        if (!TryLookup(assignment.Name, assignment.NameLocation, out var symbol))
        {
            return;
        }

        var declaredType = symbol.Type;

        if (!Accepts(assignment.Value, declaredType))
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
        if (!TryLookup(read.Name, read.NameLocation, out var symbol))
        {
            return;
        }

        var declaredType = symbol.Type;

        if (declaredType != CacaType.Error && declaredType != read.Type)
        {
            var keyword = read.Type switch
            {
                CacaType.Int => "read_int",
                CacaType.Float => "read_float",
                _ => "read_string",
            };
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
            if (existing.Type != CacaType.Error && existing.Type != CacaType.Int)
            {
                _diagnostics.Report(
                    DiagnosticCode.LoopVariableMustBeInt,
                    loop.Location,
                    $"the loop variable '{loop.Name}' must be of type int, " +
                    $"but it is of type {existing.Type.Describe()}");
            }

            Reference(loop.NameLocation, existing);
        }
        else
        {
            // A loop over an undeclared name declares it, so `for i = 1 to 3`
            // works without a preceding `var i = 0;`.
            var symbol = new VariableSymbol(
                loop.Name, CacaType.Int, loop.NameLocation, VariableKind.LoopVariable);

            _symbols[loop.Name] = symbol;
            loop.DeclaresVariable = true;
            Reference(loop.NameLocation, symbol, isDefinition: true);
        }

        _loopDepth++;
        CheckStatement(loop.Body);
        _loopDepth--;
    }

    private void RequireInsideLoop(Statement statement, string keyword)
    {
        if (_loopDepth == 0)
        {
            _diagnostics.Report(
                DiagnosticCode.NotInsideALoop,
                statement.Location,
                $"'{keyword}' can only appear inside a 'for' or 'while' loop");
        }
    }

    /// <summary>Checks an expression that is used for its value.</summary>
    private CacaType RequireValue(Expression expression, string role)
    {
        var type = CheckExpression(expression);

        if (type == CacaType.Void)
        {
            _diagnostics.Report(
                DiagnosticCode.NoValueProduced,
                expression.Location,
                $"{role} needs a value, but this expression produces none");

            return CacaType.Error;
        }

        return type;
    }

    private void RequireBool(Expression expression, string role)
    {
        var type = CheckExpression(expression);

        if (type is not (CacaType.Bool or CacaType.Error))
        {
            _diagnostics.Report(
                DiagnosticCode.TypeMismatch,
                expression.Location,
                $"{role} must be of type bool, but this is {type.Describe()}");
        }
    }

    private void RequireInt(Expression expression, string role, DiagnosticCode code)
    {
        var type = CheckExpression(expression);

        if (type is not (CacaType.Int or CacaType.Error))
        {
            _diagnostics.Report(code, expression.Location, $"{role} must be of type int, but this is {type.Describe()}");
        }
    }

    private bool TryLookup(string name, SourceLocation location, out VariableSymbol symbol)
    {
        if (_symbols.TryGetValue(name, out symbol!))
        {
            Reference(location, symbol);
            return true;
        }

        _diagnostics.Report(
            DiagnosticCode.UndeclaredVariable,
            location,
            $"'{name}' is not declared; use 'var {name} = ...' before using it");

        symbol = null!;
        return false;
    }

    private CacaType CheckExpression(Expression expression)
    {
        var type = expression switch
        {
            LiteralExpression literal => literal.LiteralType,
            ParenthesizedExpression parenthesized => CheckExpression(parenthesized.Expression),
            VariableExpression variable => CheckVariable(variable),
            UnaryExpression unary => CheckUnary(unary),
            BinaryExpression binary => CheckBinary(binary),
            CallExpression call => CheckCall(call),
            _ => throw new ArgumentOutOfRangeException(
                nameof(expression), expression, $"unhandled expression {expression.GetType().Name}"),
        };

        expression.Type = type;
        return type;
    }

    private CacaType CheckVariable(VariableExpression variable) =>
        TryLookup(variable.Name, variable.Location, out var symbol) ? symbol.Type : CacaType.Error;

    private CacaType CheckCall(CallExpression call)
    {
        var argumentTypes = call.Arguments.Select(CheckExpression).ToArray();

        if (!_functions.TryGetValue(call.Name, out var function))
        {
            _diagnostics.Report(
                DiagnosticCode.UndeclaredFunction,
                call.Location,
                $"no function named '{call.Name}' is declared");

            return CacaType.Error;
        }

        call.Target = function;
        Reference(call.NameLocation, function);

        if (argumentTypes.Length != function.Parameters.Count)
        {
            _diagnostics.Report(
                DiagnosticCode.WrongArgumentCount,
                call.Location,
                $"'{function}' takes {Count(function.Parameters.Count)}, " +
                $"but {argumentTypes.Length} were given");

            return function.ReturnType;
        }

        for (var i = 0; i < argumentTypes.Length; i++)
        {
            var expected = function.Parameters[i].Type;
            var actual = argumentTypes[i];

            if (!Accepts(call.Arguments[i], expected))
            {
                _diagnostics.Report(
                    DiagnosticCode.TypeMismatch,
                    call.Arguments[i].Location,
                    $"the parameter '{function.Parameters[i].Name}' of '{function.Name}' is of type " +
                    $"{expected.Describe()}, but this argument is of type {actual.Describe()}");
            }
        }

        return function.ReturnType;
    }

    private static string Count(int arguments) =>
        arguments == 1 ? "1 argument" : $"{arguments} arguments";

    private CacaType CheckUnary(UnaryExpression unary)
    {
        var operandType = CheckExpression(unary.Operand);

        if (operandType == CacaType.Error)
        {
            return CacaType.Error;
        }

        if (unary.Operator == UnaryOperator.Not)
        {
            if (operandType == CacaType.Bool)
            {
                return CacaType.Bool;
            }
        }
        else if (operandType.IsNumeric())
        {
            return operandType;
        }

        _diagnostics.Report(
            DiagnosticCode.OperatorNotDefined,
            unary.Location,
            $"unary operator '{unary.Operator.Describe()}' is not defined for type {operandType.Describe()}");

        return CacaType.Error;
    }

    private CacaType CheckBinary(BinaryExpression binary)
    {
        var left = CheckExpression(binary.Left);
        var right = CheckExpression(binary.Right);

        if (left == CacaType.Error || right == CacaType.Error)
        {
            return CacaType.Error;
        }

        var result = ResultOf(binary.Operator, left, right);

        if (result is null)
        {
            _diagnostics.Report(
                DiagnosticCode.OperatorNotDefined,
                binary.Location,
                $"operator '{binary.Operator.Describe()}' is not defined for types " +
                $"{left.Describe()} and {right.Describe()}");

            return CacaType.Error;
        }

        // Mixing an int with a float widens the int, so both sides of the
        // operation are of one type by the time either backend sees them.
        if (left.IsNumeric() && right.IsNumeric() && left != right)
        {
            Accepts(binary.Left, CacaType.Float);
            Accepts(binary.Right, CacaType.Float);
        }

        return result.Value;
    }

    /// <summary>
    /// The type an operator produces for a pair of operand types, or
    /// <see langword="null"/> if it does not accept them.
    /// </summary>
    private static CacaType? ResultOf(BinaryOperator op, CacaType left, CacaType right)
    {
        // An operation on an int and a float is carried out in float.
        var numeric = left.IsNumeric() && right.IsNumeric();
        var common = left == right ? left : CacaType.Float;

        return op switch
        {
            // '+' doubles as string concatenation, and concatenating a string
            // with another type converts that operand. Every other arithmetic
            // operator is defined on numbers alone, rather than, as before,
            // silently emitting unverifiable IL.
            BinaryOperator.Add when left == CacaType.String || right == CacaType.String => CacaType.String,

            BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply
                or BinaryOperator.Divide or BinaryOperator.Modulo =>
                numeric ? common : null,

            // Ordering compares numbers; equality compares any two values of
            // one type, or any two numbers.
            BinaryOperator.Less or BinaryOperator.LessOrEqual
                or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual =>
                numeric ? CacaType.Bool : null,

            BinaryOperator.Equal or BinaryOperator.NotEqual =>
                numeric || left == right ? CacaType.Bool : null,

            BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr =>
                left == CacaType.Bool && right == CacaType.Bool ? CacaType.Bool : null,

            _ => null,
        };
    }
}
