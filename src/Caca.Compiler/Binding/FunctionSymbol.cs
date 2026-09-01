using System.Reflection;
using Caca.Diagnostics;
using Caca.Syntax;

namespace Caca.Binding;

/// <summary>The signature of a declared function, as the rest of the compiler sees it.</summary>
/// <remarks>
/// Signatures are collected in a first pass over the file so that functions can
/// call one another, and themselves, regardless of the order they are written in.
/// </remarks>
public sealed class FunctionSymbol(
    string name,
    IReadOnlyList<(string Name, CacaType Type)> parameters,
    CacaType returnType,
    FunctionDeclaration declaration,
    SourceLocation nameLocation) : ISymbol
{
    public string Name { get; } = name;

    public IReadOnlyList<(string Name, CacaType Type)> Parameters { get; } = parameters;

    /// <summary><see cref="CacaType.Void"/> for a function that returns nothing.</summary>
    public CacaType ReturnType { get; } = returnType;

    public FunctionDeclaration Declaration { get; } = declaration;

    /// <summary>The extent of the function's name, for go-to-definition.</summary>
    public SourceLocation NameLocation { get; } = nameLocation;

    /// <summary>True for a function bound to a .NET method rather than a body.</summary>
    public bool IsExtern => Declaration.IsExtern;

    /// <summary>
    /// The .NET method an extern function is bound to, resolved by the type
    /// checker, or <see langword="null"/> for a function declared in the program.
    /// </summary>
    /// <remarks>
    /// An instance method is bound with the first parameter as its receiver;
    /// <see cref="MethodBase.IsStatic"/> tells the two backends apart.
    /// </remarks>
    public MethodInfo? ExternMethod { get; internal set; }

    SourceLocation ISymbol.Declaration => NameLocation;

    /// <summary>A one-line description, as shown on hover.</summary>
    public string Describe() => IsExtern
        ? $"extern func {this} from \"{Declaration.ExternTarget}\""
        : $"func {this}";

    /// <summary>The signature as it would be written in source, for error messages.</summary>
    public override string ToString()
    {
        var parameters = string.Join(", ", Parameters.Select(p => $"{p.Name}: {p.Type.Describe()}"));
        var returns = ReturnType == CacaType.Void ? string.Empty : $": {ReturnType.Describe()}";
        return $"{Name}({parameters}){returns}";
    }
}
