# cacalang for Visual Studio Code

Syntax highlighting, live errors, hover types, go to definition, find
references and a document outline for [cacalang](../../README.md).

## Installing

The extension is a thin client: the work is done by `caca-langserver`, which
is part of this repository.

1. Build and publish the server into the repository, where the committed
   workspace settings expect it:

   ```sh
   dotnet publish src/Caca.LanguageServer -c Release -o artifacts/langserver
   ```

2. Install the extension:

   ```sh
   cd editors/vscode
   npm install
   npx @vscode/vsce package
   code --install-extension cacalang-0.1.0.vsix
   ```

   Or, to try it without packaging, open this folder in VS Code and press F5 to
   launch an Extension Development Host.

`.vscode/settings.json` points `cacalang.server.path` at
`${workspaceFolder}/artifacts/langserver/caca-langserver.dll`, so nothing needs
to be on your `PATH` beyond `dotnet` itself. Setting it to an empty string
falls back to `caca-langserver` from `PATH`.

### Why the server is run through `dotnet`

`dotnet publish` produces both a `.dll` and a native launcher beside it. The
extension runs the `.dll` through `dotnet`. The launcher finds the runtime
through the standard install locations and `DOTNET_ROOT`, neither of which
covers a .NET installed under the user's home directory — so it fails to start
in exactly the setup where an editor is most likely to launch it, and with an
error the user never sees. `dotnet` is on `PATH`, so running the `.dll` works
wherever the compiler itself does.

## What the server provides

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |
| Hover showing a name's type | `textDocument/hover` |
| Go to definition | `textDocument/definition` |
| Find all references | `textDocument/references` |
| Outline and breadcrumbs | `textDocument/documentSymbol` |

The server compiles the whole file on every keystroke. Programs in this
language are small enough that this is imperceptible, and it means the editor
sees exactly what the compiler sees.

## Without the server

The extension contributes syntax highlighting, comment and bracket rules and
indentation on its own; if the language client library is missing it says so
and those keep working.

`.vscode/tasks.json` runs, checks and builds the current file, and a problem
matcher puts any errors into the Problems panel, so a build reports the same
diagnostics whether or not the server is running.

`.vscode/launch.json` steps through a `.caca` file itself, using the symbols
`caca build` writes. That needs the C# extension (`ms-dotnettools.csharp`),
which supplies the `coreclr` debug adapter.
