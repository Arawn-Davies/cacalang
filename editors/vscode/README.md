# cacalang for Visual Studio Code

Syntax highlighting, live errors, hover types, go to definition, find
references and a document outline for [cacalang](../../README.md).

## Installing

The extension is a thin client: the work is done by `caca-langserver`, which
is part of this repository.

1. Build and publish the server:

   ```sh
   dotnet publish src/Caca.LanguageServer -c Release -o ~/.caca/bin
   ```

2. Put it on your `PATH`, or set `cacalang.server.path` in your VS Code
   settings to the executable's full path.

3. Install the extension:

   ```sh
   cd editors/vscode
   npm install
   npx vsce package
   code --install-extension cacalang-0.1.0.vsix
   ```

   Or, to try it without packaging, open this folder in VS Code and press F5 to
   launch an Extension Development Host.

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
