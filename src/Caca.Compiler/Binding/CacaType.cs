namespace Caca.Binding;

/// <summary>The types a Good for Nothing expression can have.</summary>
public enum CacaType
{
    /// <summary>Assigned to expressions whose type could not be determined because of an earlier error.</summary>
    Error,
    Int,
    String,
    Bool,

    /// <summary>A 64-bit IEEE 754 binary floating point number.</summary>
    Float,

    /// <summary>The absence of a value: the result of a function that returns nothing.</summary>
    Void,
}

public static class CacaTypeExtensions
{
    /// <summary>The type a source-level type name refers to, if any.</summary>
    public static CacaType? Parse(string name) => name switch
    {
        "int" => CacaType.Int,
        "string" => CacaType.String,
        "bool" => CacaType.Bool,
        "float" => CacaType.Float,
        _ => null,
    };

    /// <summary>True for types a value can actually have.</summary>
    public static bool IsValue(this CacaType type) =>
        type is CacaType.Int or CacaType.String or CacaType.Bool or CacaType.Float;

    /// <summary>True for the types arithmetic is defined on.</summary>
    public static bool IsNumeric(this CacaType type) => type is CacaType.Int or CacaType.Float;

    /// <summary>
    /// Whether a value of one type can be used where another is expected.
    /// </summary>
    /// <remarks>
    /// An int converts to a float because every int is exactly representable as
    /// a double. Nothing converts the other way: that would lose information,
    /// and silently, which is how `1 / 2` quietly becoming 0.5 or 0 turns into
    /// a bug report.
    /// </remarks>
    public static bool ConvertsTo(this CacaType from, CacaType to) =>
        from == to || (from == CacaType.Int && to == CacaType.Float);

    public static string Describe(this CacaType type) => type switch
    {
        CacaType.Int => "int",
        CacaType.String => "string",
        CacaType.Bool => "bool",
        CacaType.Float => "float",
        CacaType.Void => "void",
        _ => "<error>",
    };

    public static Type ToClrType(this CacaType type) => type switch
    {
        CacaType.Int => typeof(int),
        CacaType.String => typeof(string),
        CacaType.Bool => typeof(bool),
        CacaType.Float => typeof(double),
        CacaType.Void => typeof(void),
        _ => throw new InvalidOperationException("the error type has no CLR representation"),
    };
}
