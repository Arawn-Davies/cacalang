namespace Caca.Diagnostics;

/// <summary>
/// A half-open range of characters in a source file, together with the
/// one-based line and column of its first character.
/// </summary>
/// <remarks>
/// The original compiler carried no positions at all, so every error was
/// reported as a bare sentence with no way to find the offending code.
/// </remarks>
public readonly record struct SourceLocation(int Start, int Length, int Line, int Column)
{
    public static readonly SourceLocation None = new(0, 0, 0, 0);

    public int End => Start + Length;

    /// <summary>Spans this location through the end of <paramref name="other"/>.</summary>
    public SourceLocation To(SourceLocation other) =>
        other.End <= End ? this : this with { Length = other.End - Start };

    public override string ToString() => Line == 0 ? "<unknown>" : $"({Line},{Column})";
}
