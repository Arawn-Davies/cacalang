namespace Caca.Binding;

/// <summary>The types a Good for Nothing expression can have.</summary>
public enum CacaType
{
    /// <summary>Assigned to expressions whose type could not be determined because of an earlier error.</summary>
    Error,
    Int,
    String,
}

public static class CacaTypeExtensions
{
    public static string Describe(this CacaType type) => type switch
    {
        CacaType.Int => "int",
        CacaType.String => "string",
        _ => "<error>",
    };

    public static Type ToClrType(this CacaType type) => type switch
    {
        CacaType.Int => typeof(int),
        CacaType.String => typeof(string),
        _ => throw new InvalidOperationException("the error type has no CLR representation"),
    };
}
