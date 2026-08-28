const { workspace, window } = require('vscode');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

let client;

/**
 * Starts the language server and connects it to every .caca file.
 *
 * The server is a .NET executable that speaks LSP over stdin and stdout, so
 * there is nothing to run here beyond pointing the client at it.
 */
function activate(context) {
    const configured = workspace.getConfiguration('cacalang').get('server.path');
    const command = configured && configured.length > 0 ? configured : 'caca-langserver';

    const serverOptions = {
        run: { command, transport: TransportKind.stdio },
        debug: { command, transport: TransportKind.stdio },
    };

    const clientOptions = {
        documentSelector: [{ scheme: 'file', language: 'caca' }],
        synchronize: {
            fileEvents: workspace.createFileSystemWatcher('**/*.caca'),
        },
    };

    client = new LanguageClient('cacalang', 'cacalang', serverOptions, clientOptions);

    client.start().catch((error) => {
        window.showErrorMessage(
            `cacalang: could not start '${command}'. Build it with ` +
            `'dotnet publish src/Caca.LanguageServer' and set cacalang.server.path. (${error.message})`);
    });

    context.subscriptions.push(client);
}

function deactivate() {
    return client ? client.stop() : undefined;
}

module.exports = { activate, deactivate };
