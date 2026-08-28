using Caca.LanguageServer.Protocol;

namespace Caca.LanguageServer;

internal static class Program
{
    /// <summary>
    /// Runs the language server over standard input and output, which is how
    /// editors start one.
    /// </summary>
    private static async Task Main()
    {
        var connection = new JsonRpcConnection(Console.OpenStandardInput(), Console.OpenStandardOutput());
        await new LanguageServer(connection).RunAsync();
    }
}
