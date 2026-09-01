namespace Interop;

/// <summary>What interop.caca calls into.</summary>
public static class Hello
{
    public static void SayHello() => Console.WriteLine("Hello, World from C#!");

    public static string Reverse(string text)
    {
        var characters = text.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }
}
