using Caca.Diagnostics;

namespace Caca.Binding;

/// <summary>What the type checker learned about a program.</summary>
/// <param name="Functions">The functions the program declares, by name.</param>
/// <param name="References">
/// Every place a name is used and what it refers to, which is what editor
/// tooling answers its questions from.
/// </param>
public sealed record BindingResult(
    IReadOnlyDictionary<string, FunctionSymbol> Functions,
    IReadOnlyList<SymbolReference> References)
{
    public static BindingResult Empty { get; } =
        new(new Dictionary<string, FunctionSymbol>(), []);

    /// <summary>
    /// The name at a one-based line and column, or <see langword="null"/> if the
    /// position is not on one.
    /// </summary>
    public SymbolReference? FindAt(int line, int column)
    {
        foreach (var reference in References)
        {
            var at = reference.Location;

            // The end is exclusive, but a position just past the last character
            // is still "on" the name, which is where a caret usually sits.
            if (at.Line == line && column >= at.Column && column <= at.Column + at.Length)
            {
                return reference;
            }
        }

        return null;
    }

    /// <summary>Every place one symbol is named, including where it is declared.</summary>
    public IEnumerable<SymbolReference> FindReferences(ISymbol symbol) =>
        References.Where(reference => ReferenceEquals(reference.Symbol, symbol));

    /// <summary>The places symbols are introduced, in source order.</summary>
    public IEnumerable<SymbolReference> Definitions =>
        References.Where(reference => reference.IsDefinition);
}
