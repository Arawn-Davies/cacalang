namespace Caca.Diagnostics;

/// <summary>
/// A half-open range of characters in a source file, together with the
/// one-based line and column of its first character.
/// </summary>
/// <remarks>
/// The original compiler carried no positions at all, so every error was
/// reported as a bare sentence with no way to find the offending code.
/// </remarks>
public readonly record struct SourceLocation(
    int Start,
    int Length,
    int Line,
    int Column,
    int EndLine,
    int EndColumn)
{
    /// <summary>A span lying on a single line.</summary>
    public SourceLocation(int start, int length, int line, int column)
        : this(start, length, line, column, line, column + length)
    {
    }

    public static readonly SourceLocation None = new(0, 0, 0, 0);

    public int End => Start + Length;

    /// <summary>
    /// Spans this location through the end of <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// The end is carried along, not recomputed from the length, because a span
    /// covering a loop or a function runs over many lines and its last line is
    /// not something a character count can recover.
    /// </remarks>
    public SourceLocation To(SourceLocation other) =>
        other.End <= End
            ? this
            : this with { Length = other.End - Start, EndLine = other.EndLine, EndColumn = other.EndColumn };

    public override string ToString() => Line == 0 ? "<unknown>" : $"({Line},{Column})";
}
