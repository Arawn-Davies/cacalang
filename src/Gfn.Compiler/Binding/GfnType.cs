namespace Gfn.Binding;

/// <summary>The types a Good for Nothing expression can have.</summary>
public enum GfnType
{
    /// <summary>Assigned to expressions whose type could not be determined because of an earlier error.</summary>
    Error,
    Int,
    String,
}

public static class GfnTypeExtensions
{
    public static string Describe(this GfnType type) => type switch
    {
        GfnType.Int => "int",
        GfnType.String => "string",
        _ => "<error>",
    };

    public static Type ToClrType(this GfnType type) => type switch
    {
        GfnType.Int => typeof(int),
        GfnType.String => typeof(string),
        _ => throw new InvalidOperationException("the error type has no CLR representation"),
    };
}
