namespace Caca.ReferenceLibrary;

/// <summary>What a .caca program binding to a C# assembly calls in the tests.</summary>
public static class Greetings
{
    public static string Greet(string name) => $"hello from C#, {name}";

    public static int Triple(int number) => number * 3;

    public static void SayHello() => Console.WriteLine("Hello, World from C#!");
}
