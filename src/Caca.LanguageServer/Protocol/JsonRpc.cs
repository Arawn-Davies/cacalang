using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Caca.LanguageServer.Protocol;

/// <summary>
/// The Language Server Protocol's wire format: JSON-RPC messages, each preceded
/// by a <c>Content-Length</c> header.
/// </summary>
/// <remarks>
/// This is written out rather than taken from a package, both because the
/// framing is only a few dozen lines and because this project is meant to be
/// read.
/// </remarks>
public sealed class JsonRpcConnection(Stream input, Stream output)
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>Reads the next message, or null at end of input.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken)
    {
        var length = await ReadHeadersAsync(cancellationToken);

        if (length is null)
        {
            return null;
        }

        var buffer = new byte[length.Value];
        var read = 0;

        while (read < buffer.Length)
        {
            var count = await _input.ReadAsync(buffer.AsMemory(read), cancellationToken);

            if (count == 0)
            {
                return null;
            }

            read += count;
        }

        return JsonNode.Parse(buffer) as JsonObject;
    }

    /// <summary>Reads the header block and returns the body length it declares.</summary>
    private async Task<int?> ReadHeadersAsync(CancellationToken cancellationToken)
    {
        int? length = null;
        var line = new StringBuilder();
        var single = new byte[1];

        while (true)
        {
            var count = await _input.ReadAsync(single.AsMemory(), cancellationToken);

            if (count == 0)
            {
                return null;
            }

            if (single[0] != (byte)'\n')
            {
                if (single[0] != (byte)'\r')
                {
                    line.Append((char)single[0]);
                }

                continue;
            }

            // A blank line ends the header block.
            if (line.Length == 0)
            {
                return length;
            }

            var header = line.ToString();
            line.Clear();

            var separator = header.IndexOf(':');

            if (separator > 0 &&
                header.AsSpan(0, separator).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(header.AsSpan(separator + 1).Trim(), out var parsed))
            {
                length = parsed;
            }
        }
    }

    public Task SendResponseAsync(JsonNode? id, JsonNode? result) =>
        SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        });

    public Task SendErrorAsync(JsonNode? id, int code, string message) =>
        SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });

    public Task SendNotificationAsync(string method, JsonNode? parameters) =>
        SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
        });

    private async Task SendAsync(JsonObject message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString(SerializerOptions));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        // One writer at a time: diagnostics are published while requests are
        // being answered.
        await _writeLock.WaitAsync();

        try
        {
            await _output.WriteAsync(header);
            await _output.WriteAsync(body);
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
