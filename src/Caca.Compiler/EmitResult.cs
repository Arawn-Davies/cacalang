namespace Caca;

/// <summary>What a successful compilation wrote to disk.</summary>
/// <param name="AssemblyPath">The emitted assembly, which holds the IL.</param>
/// <param name="RuntimeConfigPath">
/// The configuration the .NET host reads to decide which runtime to load.
/// </param>
/// <param name="LauncherPath">
/// The native stub that runs the assembly directly, or <see langword="null"/>
/// if none was asked for or none could be produced.
/// </param>
/// <param name="Warning">Why no launcher was produced, when one was wanted.</param>
public sealed record EmitResult(
    string AssemblyPath,
    string RuntimeConfigPath,
    string? LauncherPath,
    string? Warning);
