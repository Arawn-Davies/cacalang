namespace Caca.Tests;

/// <summary>
/// Tests that redirect the process-wide <see cref="Console"/> streams.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel, and Console.SetOut affects the whole
/// process, so classes that use it have to be kept out of each other's way.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ConsoleCollection
{
    public const string Name = "console";
}
