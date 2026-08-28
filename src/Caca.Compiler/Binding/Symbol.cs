using Caca.Diagnostics;

namespace Caca.Binding;

/// <summary>Something a name in the source refers to.</summary>
/// <remarks>
/// Names were previously resolved by looking them up in a dictionary and
/// throwing the answer away. Keeping a symbol for each one is what lets a tool
/// answer "what is this?" and "where was it declared?" about a position in a
/// file.
/// </remarks>
public interface ISymbol
{
    string Name { get; }

    /// <summary>Where the symbol was declared, for go-to-definition.</summary>
    SourceLocation Declaration { get; }

    /// <summary>A one-line description, as shown on hover.</summary>
    string Describe();
}

/// <summary>A variable or a parameter.</summary>
public sealed class VariableSymbol(
    string name,
    CacaType type,
    SourceLocation declaration,
    VariableKind kind) : ISymbol
{
    public string Name { get; } = name;

    public CacaType Type { get; } = type;

    public SourceLocation Declaration { get; } = declaration;

    public VariableKind Kind { get; } = kind;

    public string Describe() => $"{Keyword()} {Name}: {Type.Describe()}";

    private string Keyword() => Kind switch
    {
        VariableKind.Parameter => "(parameter)",
        VariableKind.LoopVariable => "(loop variable)",
        _ => "var",
    };

    public override string ToString() => Describe();
}

public enum VariableKind
{
    Local,
    Parameter,
    LoopVariable,
}

/// <summary>One place a symbol is named in the source.</summary>
/// <param name="Location">The extent of the name itself.</param>
/// <param name="Symbol">What the name refers to.</param>
/// <param name="IsDefinition">True where the symbol is introduced.</param>
public readonly record struct SymbolReference(SourceLocation Location, ISymbol Symbol, bool IsDefinition);
