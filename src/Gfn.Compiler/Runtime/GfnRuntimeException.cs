namespace Gfn.Runtime;

/// <summary>
/// Raised when a program fails while running: division by zero, or input that
/// is not a number.
/// </summary>
/// <remarks>
/// These surface as a one-line message rather than a .NET stack trace, which
/// is all a user of the language can act on.
/// </remarks>
public sealed class GfnRuntimeException(string message) : Exception(message);
