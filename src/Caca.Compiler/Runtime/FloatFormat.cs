using System.Globalization;

namespace Caca.Runtime;

/// <summary>
/// How a float is written out.
/// </summary>
/// <remarks>
/// The rule lives here, in one place, because the interpreter calls it directly
/// while the emitter generates a method that does the same thing. The two must
/// agree exactly or the same program prints differently depending on how it was
/// run.
/// </remarks>
public static class FloatFormat
{
    /// <summary>Renders a float the way <c>print</c> does.</summary>
    /// <remarks>
    /// .NET's shortest round-trippable form writes 1.0 as "1", which reads as
    /// an int. A trailing ".0" keeps a float looking like one, and is only
    /// added when the text has neither a point nor an exponent.
    /// </remarks>
    public static string ToText(double value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        if (!double.IsFinite(value) || text.Contains('.') || text.Contains('E') || text.Contains('e'))
        {
            return text;
        }

        return text + ".0";
    }
}
