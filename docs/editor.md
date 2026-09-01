# Editor support

`caca-langserver` implements the Language Server Protocol, so any editor that
speaks it can offer the same features.

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |
| Hover showing a name's type | `textDocument/hover` |
| Go to definition | `textDocument/definition` |
| Find all references | `textDocument/references` |
| Outline and breadcrumbs | `textDocument/documentSymbol` |

## Visual Studio Code

[`editors/vscode`](../editors/vscode) is an extension that uses the server.
Its [README](../editors/vscode/README.md) has the install steps; in short:

```sh
dotnet publish src/Caca.LanguageServer -c Release -o artifacts/langserver
cd editors/vscode && npm install && npx @vscode/vsce package
code --install-extension cacalang-0.2.0.vsix
```

The repository's `.vscode` folder is set up to match: settings pointing at that
server, tasks that run, check and build the current file into the Problems
panel, and a launch configuration that steps through a `.caca` file using the
symbols `caca build` writes. That last one needs the C# extension, which
supplies the `coreclr` debug adapter.

## Another editor

Start `caca-langserver` and talk to it over stdin and stdout. It needs no
arguments and no configuration.

Run the published `.dll` through `dotnet` rather than the native launcher beside
it. The launcher locates the runtime through the standard install locations and
`DOTNET_ROOT`, neither of which covers a .NET installed under the user's home
directory, so it fails to start in exactly the setup where an editor is most
likely to launch it — and with an error nobody sees.

## How it behaves

The server compiles the whole file on every keystroke. Programs in this language
are small enough that this is imperceptible, and it means the editor sees
exactly what the compiler sees, with no second, incremental analysis that could
drift out of agreement with it.

It answers questions about names from the symbols the type checker records while
it works: what a name refers to, what type it has, and where it was declared.

The protocol framing is written out in
[`Protocol/JsonRpc.cs`](../src/Caca.LanguageServer/Protocol/JsonRpc.cs) rather
than taken from a package, because it is only a few dozen lines and this project
is meant to be read.
