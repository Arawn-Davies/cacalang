namespace Caca.Binding;

/// <summary>The types a Good for Nothing expression can have.</summary>
public enum CacaType
{
    /// <summary>Assigned to expressions whose type could not be determined because of an earlier error.</summary>
    Error,
    Int,
    String,
    Bool,

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
        _ => null,
    };

    /// <summary>True for types a value can actually have.</summary>
    public static bool IsValue(this CacaType type) => type is CacaType.Int or CacaType.String or CacaType.Bool;

    public static string Describe(this CacaType type) => type switch
    {
        CacaType.Int => "int",
        CacaType.String => "string",
        CacaType.Bool => "bool",
        CacaType.Void => "void",
        _ => "<error>",
    };

    public static Type ToClrType(this CacaType type) => type switch
    {
        CacaType.Int => typeof(int),
        CacaType.String => typeof(string),
        CacaType.Bool => typeof(bool),
        CacaType.Void => typeof(void),
        _ => throw new InvalidOperationException("the error type has no CLR representation"),
    };
}
