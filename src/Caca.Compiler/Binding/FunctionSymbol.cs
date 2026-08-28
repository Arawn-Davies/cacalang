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
    FunctionDeclaration declaration)
{
    public string Name { get; } = name;

    public IReadOnlyList<(string Name, CacaType Type)> Parameters { get; } = parameters;

    /// <summary><see cref="CacaType.Void"/> for a function that returns nothing.</summary>
    public CacaType ReturnType { get; } = returnType;

    public FunctionDeclaration Declaration { get; } = declaration;

    /// <summary>The signature as it would be written in source, for error messages.</summary>
    public override string ToString()
    {
        var parameters = string.Join(", ", Parameters.Select(p => $"{p.Name}: {p.Type.Describe()}"));
        var returns = ReturnType == CacaType.Void ? string.Empty : $": {ReturnType.Describe()}";
        return $"{Name}({parameters}){returns}";
    }
}
