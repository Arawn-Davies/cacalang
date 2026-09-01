using System.Text;
using System.Text.Json.Nodes;
using Caca.LanguageServer;
using Caca.LanguageServer.Protocol;

namespace Caca.Tests;

/// <summary>
/// Drives the server the way an editor does: JSON-RPC messages with
/// Content-Length framing, over a pair of streams.
/// </summary>
public class LanguageServerTests
{
    private const string Uri = "file:///test.caca";

    /// <summary>Sends a conversation to the server and returns everything it said back.</summary>
    private static async Task<List<JsonObject>> ConverseAsync(params JsonObject[] messages)
    {
        var input = new MemoryStream();

        foreach (var message in messages)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            input.Write(header);
            input.Write(body);
        }

        input.Position = 0;

        var output = new MemoryStream();
        await new LanguageServer.LanguageServer(new JsonRpcConnection(input, output)).RunAsync();

        return Parse(output.ToArray());
    }

    /// <summary>Reads the framed messages the server produced.</summary>
    private static List<JsonObject> Parse(byte[] bytes)
    {
        var messages = new List<JsonObject>();
        var text = Encoding.UTF8.GetString(bytes);
        var position = 0;

        while (position < text.Length)
        {
            const string marker = "Content-Length: ";
            var start = text.IndexOf(marker, position, StringComparison.Ordinal);

            if (start < 0)
            {
                break;
            }

            var lengthStart = start + marker.Length;
            var lengthEnd = text.IndexOf("\r\n", lengthStart, StringComparison.Ordinal);
            var length = int.Parse(text[lengthStart..lengthEnd]);
            var bodyStart = text.IndexOf("\r\n\r\n", lengthEnd, StringComparison.Ordinal) + 4;

            // Content-Length counts bytes, and the bodies here are ASCII.
            messages.Add((JsonObject)JsonNode.Parse(text.Substring(bodyStart, length))!);
            position = bodyStart + length;
        }

        return messages;
    }

    private static JsonObject Request(int id, string method, JsonObject? parameters = null) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters,
    };

    private static JsonObject Notification(string method, JsonObject parameters) => new()
    {
        ["jsonrpc"] = "2.0",
        ["method"] = method,
        ["params"] = parameters,
    };

    private static JsonObject DidOpen(string text) => Notification("textDocument/didOpen", new JsonObject
    {
        ["textDocument"] = new JsonObject
        {
            ["uri"] = Uri,
            ["languageId"] = "caca",
            ["version"] = 1,
            ["text"] = text,
        },
    });

    /// <summary>A position, given in the editor's zero-based coordinates.</summary>
    private static JsonObject At(int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = Uri },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

    private static JsonObject ResponseTo(List<JsonObject> messages, int id) =>
        Assert.IsType<JsonObject>(messages.Single(m => (int?)m["id"] == id));

    private static JsonObject Notification(List<JsonObject> messages, string method) =>
        Assert.IsType<JsonObject>(messages.Single(m => (string?)m["method"] == method));

    [Fact]
    public async Task Initialize_reports_what_the_server_can_do()
    {
        var messages = await ConverseAsync(Request(1, "initialize"));
        var capabilities = ResponseTo(messages, 1)["result"]?["capabilities"];

        Assert.True((bool?)capabilities?["hoverProvider"]);
        Assert.True((bool?)capabilities?["definitionProvider"]);
        Assert.True((bool?)capabilities?["referencesProvider"]);
        Assert.True((bool?)capabilities?["documentSymbolProvider"]);
    }

    [Fact]
    public async Task Initialize_references_let_extern_targets_resolve()
    {
        // The editor's cacalang.references setting arrives as initialization
        // options, the language server counterpart of the CLI's --ref.
        var initialize = Request(1, "initialize", new JsonObject
        {
            ["initializationOptions"] = new JsonObject
            {
                ["references"] = new JsonArray(
                    Path.Combine(AppContext.BaseDirectory, "Caca.ReferenceLibrary.dll")),
            },
        });

        var program = """
            extern func greet(name: string): string from "Caca.ReferenceLibrary.Greetings.Greet";
            print greet("editor");
            """;

        var messages = await ConverseAsync(initialize, DidOpen(program));
        var diagnostics = Notification(messages, "textDocument/publishDiagnostics")["params"]?["diagnostics"];

        Assert.Empty(Assert.IsType<JsonArray>(diagnostics));
    }

    [Fact]
    public async Task Initialize_references_that_cannot_load_are_ignored()
    {
        var initialize = Request(1, "initialize", new JsonObject
        {
            ["initializationOptions"] = new JsonObject
            {
                ["references"] = new JsonArray("/no/such/assembly.dll"),
            },
        });

        var messages = await ConverseAsync(initialize, DidOpen("""print "still working";"""));
        var diagnostics = Notification(messages, "textDocument/publishDiagnostics")["params"]?["diagnostics"];

        Assert.Empty(Assert.IsType<JsonArray>(diagnostics));
    }

    [Fact]
    public async Task Opening_a_valid_document_publishes_no_errors()
    {
        var messages = await ConverseAsync(DidOpen("""print "hello";"""));
        var diagnostics = Notification(messages, "textDocument/publishDiagnostics")["params"]?["diagnostics"];

        Assert.Empty(Assert.IsType<JsonArray>(diagnostics));
    }

    [Fact]
    public async Task Opening_a_broken_document_publishes_errors_with_positions()
    {
        var messages = await ConverseAsync(DidOpen("var x = 1;\nprint y;"));
        var published = Notification(messages, "textDocument/publishDiagnostics")["params"];
        var diagnostics = Assert.IsType<JsonArray>(published?["diagnostics"]);
        var error = Assert.Single(diagnostics);

        Assert.Equal(Uri, (string?)published?["uri"]);
        Assert.Equal("CACA0008", (string?)error?["code"]);
        Assert.Equal("caca", (string?)error?["source"]);
        Assert.Contains("'y' is not declared", (string?)error?["message"]);

        // The compiler counts lines from one, the protocol from zero.
        Assert.Equal(1, (int?)error?["range"]?["start"]?["line"]);
        Assert.Equal(6, (int?)error?["range"]?["start"]?["character"]);
    }

    [Fact]
    public async Task Editing_a_document_republishes_its_errors()
    {
        var messages = await ConverseAsync(
            DidOpen("print y;"),
            Notification("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri, ["version"] = 2 },
                ["contentChanges"] = new JsonArray { new JsonObject { ["text"] = "print 1;" } },
            }));

        var published = messages.Where(m => (string?)m["method"] == "textDocument/publishDiagnostics").ToList();

        Assert.Equal(2, published.Count);
        Assert.Single(Assert.IsType<JsonArray>(published[0]["params"]?["diagnostics"]));
        Assert.Empty(Assert.IsType<JsonArray>(published[1]["params"]?["diagnostics"]));
    }

    [Fact]
    public async Task Hover_over_a_variable_shows_its_type()
    {
        var messages = await ConverseAsync(
            DidOpen("var count = 42;\nprint count;"),
            Request(2, "textDocument/hover", At(1, 6)));

        var contents = ResponseTo(messages, 2)["result"]?["contents"];

        Assert.Contains("var count: int", (string?)contents?["value"]);
    }

    [Fact]
    public async Task Hover_over_a_function_shows_its_signature()
    {
        var messages = await ConverseAsync(
            DidOpen("""
                func add(a: int, b: int): int do return a + b; end
                print add(1, 2);
                """),
            Request(2, "textDocument/hover", At(1, 6)));

        var contents = ResponseTo(messages, 2)["result"]?["contents"];

        Assert.Contains("func add(a: int, b: int): int", (string?)contents?["value"]);
    }

    [Fact]
    public async Task Hover_over_a_parameter_says_so()
    {
        var messages = await ConverseAsync(
            DidOpen("func double(n: int): int do return n * 2; end double(1);"),
            Request(2, "textDocument/hover", At(0, 35)));

        Assert.Contains("(parameter) n: int", (string?)ResponseTo(messages, 2)["result"]?["contents"]?["value"]);
    }

    [Fact]
    public async Task Hover_over_nothing_returns_nothing()
    {
        var messages = await ConverseAsync(
            DidOpen("print 1;"),
            Request(2, "textDocument/hover", At(0, 6)));

        Assert.Null(ResponseTo(messages, 2)["result"]);
    }

    [Fact]
    public async Task Definition_of_a_variable_points_at_its_declaration()
    {
        var messages = await ConverseAsync(
            DidOpen("var count = 42;\nprint count;"),
            Request(2, "textDocument/definition", At(1, 6)));

        var location = ResponseTo(messages, 2)["result"];

        Assert.Equal(Uri, (string?)location?["uri"]);
        Assert.Equal(0, (int?)location?["range"]?["start"]?["line"]);
        Assert.Equal(4, (int?)location?["range"]?["start"]?["character"]);
    }

    [Fact]
    public async Task Definition_of_a_function_works_from_a_call_above_it()
    {
        var messages = await ConverseAsync(
            DidOpen("""
                print later(1);
                func later(n: int): int do return n; end
                """),
            Request(2, "textDocument/definition", At(0, 7)));

        Assert.Equal(1, (int?)ResponseTo(messages, 2)["result"]?["range"]?["start"]?["line"]);
    }

    [Fact]
    public async Task References_finds_every_use_of_a_variable()
    {
        var messages = await ConverseAsync(
            DidOpen("""
                var n = 1;
                n = n + 1;
                print n;
                """),
            Request(2, "textDocument/references", At(0, 4)));

        var locations = Assert.IsType<JsonArray>(ResponseTo(messages, 2)["result"]);

        // The declaration, the assignment target, the read in `n + 1`, and the print.
        Assert.Equal(4, locations.Count);
    }

    [Fact]
    public async Task References_can_leave_out_the_declaration()
    {
        var messages = await ConverseAsync(
            DidOpen("var n = 1;\nprint n;"),
            Request(2, "textDocument/references", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["position"] = new JsonObject { ["line"] = 0, ["character"] = 4 },
                ["context"] = new JsonObject { ["includeDeclaration"] = false },
            }));

        Assert.Single(Assert.IsType<JsonArray>(ResponseTo(messages, 2)["result"]));
    }

    [Fact]
    public async Task Document_symbols_lists_functions_and_variables()
    {
        var messages = await ConverseAsync(
            DidOpen("""
                func add(a: int, b: int): int do return a + b; end
                var total = add(1, 2);
                """),
            Request(2, "textDocument/documentSymbol", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
            }));

        var symbols = Assert.IsType<JsonArray>(ResponseTo(messages, 2)["result"]);
        var names = symbols.Select(s => (string?)s?["name"]).ToList();

        Assert.Contains("add", names);
        Assert.Contains("total", names);
        Assert.Contains("a", names);
    }

    [Fact]
    public async Task An_unsupported_request_is_answered_with_an_error()
    {
        var messages = await ConverseAsync(Request(1, "textDocument/formatting"));

        Assert.Equal(-32601, (int?)ResponseTo(messages, 1)["error"]?["code"]);
    }

    [Fact]
    public async Task Shutdown_is_answered_and_exit_ends_the_session()
    {
        var messages = await ConverseAsync(
            Request(1, "shutdown"),
            Notification("exit", []));

        Assert.True(ResponseTo(messages, 1).ContainsKey("result"));
    }

    [Fact]
    public async Task A_closed_document_is_forgotten()
    {
        var messages = await ConverseAsync(
            DidOpen("var n = 1;"),
            Notification("textDocument/didClose", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
            }),
            Request(2, "textDocument/hover", At(0, 4)));

        Assert.Null(ResponseTo(messages, 2)["result"]);
    }
}
