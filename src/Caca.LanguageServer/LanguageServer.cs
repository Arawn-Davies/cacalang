using System.Reflection;
using System.Text.Json.Nodes;
using Caca.Binding;
using Caca.LanguageServer.Protocol;

namespace Caca.LanguageServer;

/// <summary>
/// A Language Server Protocol server for cacalang: live errors, hover types,
/// go-to-definition, references and document symbols.
/// </summary>
public sealed class LanguageServer(JsonRpcConnection connection)
{
    private const int MethodNotFound = -32601;

    private readonly JsonRpcConnection _connection = connection;
    private readonly DocumentStore _documents = new();
    private bool _shuttingDown;

    /// <summary>Reads and answers messages until the client closes the connection.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await _connection.ReadAsync(cancellationToken);

            if (message is null)
            {
                return;
            }

            await HandleAsync(message);

            if (_shuttingDown && (string?)message["method"] == "exit")
            {
                return;
            }
        }
    }

    private async Task HandleAsync(JsonObject message)
    {
        var method = (string?)message["method"];
        var id = message["id"];
        var parameters = message["params"];

        // A notification has no id and expects no reply.
        switch (method)
        {
            case "initialize":
                LoadReferences(parameters?["initializationOptions"]?["references"] as JsonArray);
                await _connection.SendResponseAsync(id, InitializeResult());
                return;

            case "initialized":
                return;

            case "textDocument/didOpen":
                await OpenAsync(parameters);
                return;

            case "textDocument/didChange":
                await ChangeAsync(parameters);
                return;

            case "textDocument/didClose":
                Close(parameters);
                return;

            case "textDocument/hover":
                await _connection.SendResponseAsync(id, Hover(parameters));
                return;

            case "textDocument/definition":
                await _connection.SendResponseAsync(id, Definition(parameters));
                return;

            case "textDocument/references":
                await _connection.SendResponseAsync(id, References(parameters));
                return;

            case "textDocument/documentSymbol":
                await _connection.SendResponseAsync(id, DocumentSymbols(parameters));
                return;

            case "shutdown":
                _shuttingDown = true;
                await _connection.SendResponseAsync(id, null);
                return;

            case "exit":
                _shuttingDown = true;
                return;

            default:
                if (id is not null)
                {
                    await _connection.SendErrorAsync(id, MethodNotFound, $"unsupported request '{method}'");
                }

                return;
        }
    }

    /// <summary>
    /// Loads the assemblies the client asks for, so extern targets resolve to
    /// the same methods the command line's <c>--ref</c> would find.
    /// </summary>
    /// <remarks>
    /// A path that cannot be loaded — not built yet, not an assembly — is
    /// simply not available to bind against, and the extern that needs it
    /// reports its ordinary diagnostic. An editor session must keep working
    /// either way.
    /// </remarks>
    private void LoadReferences(JsonArray? paths)
    {
        if (paths is null)
        {
            return;
        }

        var assemblies = new List<Assembly>();

        foreach (var path in paths)
        {
            if ((string?)path is not { Length: > 0 } file)
            {
                continue;
            }

            try
            {
                assemblies.Add(Assembly.LoadFrom(Path.GetFullPath(file)));
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException
                or FileLoadException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        _documents.References = assemblies;
    }

    private static JsonObject InitializeResult() => new()
    {
        ["capabilities"] = new JsonObject
        {
            // The whole file is sent on every change. Programs in this language
            // are small, and compiling one is fast enough that tracking
            // incremental edits would buy nothing.
            ["textDocumentSync"] = 1,
            ["hoverProvider"] = true,
            ["definitionProvider"] = true,
            ["referencesProvider"] = true,
            ["documentSymbolProvider"] = true,
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "caca-langserver",
            ["version"] = typeof(LanguageServer).Assembly.GetName().Version?.ToString(3) ?? "unknown",
        },
    };

    private async Task OpenAsync(JsonNode? parameters)
    {
        var document = parameters?["textDocument"];
        var uri = (string?)document?["uri"];

        if (uri is null)
        {
            return;
        }

        await PublishDiagnosticsAsync(_documents.Update(uri, (string?)document?["text"] ?? string.Empty));
    }

    private async Task ChangeAsync(JsonNode? parameters)
    {
        var uri = (string?)parameters?["textDocument"]?["uri"];
        var changes = parameters?["contentChanges"] as JsonArray;

        if (uri is null || changes is null || changes.Count == 0)
        {
            return;
        }

        // With full synchronization the last change carries the whole document.
        var text = (string?)changes[^1]?["text"] ?? string.Empty;
        await PublishDiagnosticsAsync(_documents.Update(uri, text));
    }

    private void Close(JsonNode? parameters)
    {
        if ((string?)parameters?["textDocument"]?["uri"] is { } uri)
        {
            _documents.Remove(uri);
        }
    }

    private Task PublishDiagnosticsAsync(Document document)
    {
        var diagnostics = new JsonArray();

        foreach (var diagnostic in document.Compilation.Diagnostics)
        {
            diagnostics.Add(Lsp.Diagnostic(diagnostic));
        }

        return _connection.SendNotificationAsync("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = document.Uri,
            ["diagnostics"] = diagnostics,
        });
    }

    /// <summary>Finds the name under the cursor, if there is one.</summary>
    private (Document Document, SymbolReference Reference)? Resolve(JsonNode? parameters)
    {
        var uri = (string?)parameters?["textDocument"]?["uri"];

        if (uri is null || _documents.Find(uri) is not { } document)
        {
            return null;
        }

        var (line, column) = Lsp.ToCompilerPosition(parameters?["position"]);
        return document.Binding.FindAt(line, column) is { } reference ? (document, reference) : null;
    }

    private JsonNode? Hover(JsonNode? parameters)
    {
        if (Resolve(parameters) is not var (_, reference))
        {
            return null;
        }

        return new JsonObject
        {
            ["contents"] = Lsp.MarkdownCode(reference.Symbol.Describe()),
            ["range"] = Lsp.Range(reference.Location),
        };
    }

    private JsonNode? Definition(JsonNode? parameters)
    {
        if (Resolve(parameters) is not var (document, reference))
        {
            return null;
        }

        return Lsp.Location(document.Uri, reference.Symbol.Declaration);
    }

    private JsonNode? References(JsonNode? parameters)
    {
        if (Resolve(parameters) is not var (document, reference))
        {
            return null;
        }

        var includeDeclaration = (bool?)parameters?["context"]?["includeDeclaration"] ?? true;
        var locations = new JsonArray();

        foreach (var found in document.Binding.FindReferences(reference.Symbol))
        {
            if (found.IsDefinition && !includeDeclaration)
            {
                continue;
            }

            locations.Add(Lsp.Location(document.Uri, found.Location));
        }

        return locations;
    }

    private JsonNode? DocumentSymbols(JsonNode? parameters)
    {
        var uri = (string?)parameters?["textDocument"]?["uri"];

        if (uri is null || _documents.Find(uri) is not { } document)
        {
            return new JsonArray();
        }

        var symbols = new JsonArray();

        foreach (var definition in document.Binding.Definitions)
        {
            symbols.Add(new JsonObject
            {
                ["name"] = definition.Symbol.Name,
                ["detail"] = definition.Symbol.Describe(),
                ["kind"] = definition.Symbol is FunctionSymbol
                    ? Lsp.SymbolKind.Function
                    : Lsp.SymbolKind.Variable,
                ["range"] = Lsp.Range(definition.Location),
                ["selectionRange"] = Lsp.Range(definition.Location),
            });
        }

        return symbols;
    }
}
