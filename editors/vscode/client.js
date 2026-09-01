const { workspace, window } = require('vscode');

let client;

/**
 * Loads the language client library.
 *
 * The library is a dependency fetched with npm. If it is missing the extension
 * still contributes syntax highlighting and the language configuration, so
 * editing works; only the server-backed features are unavailable.
 */
function loadLanguageClient() {
    try {
        return require('vscode-languageclient/node');
    } catch (error) {
        window.showWarningMessage(
            'cacalang: run "npm install" in editors/vscode to enable errors, hover and go to definition. ' +
            'Syntax highlighting works without it.');
        return undefined;
    }
}

/**
 * Starts the language server and connects it to every .caca file.
 *
 * The server is a .NET executable that speaks LSP over stdin and stdout, so
 * there is nothing to run here beyond pointing the client at it.
 */
function activate(context) {
    const languageClient = loadLanguageClient();

    if (!languageClient) {
        return;
    }

    const { LanguageClient, TransportKind } = languageClient;
    const server = resolveServer();

    const execution = {
        command: server.command,
        args: server.args,
        transport: TransportKind.stdio,
        options: { env: { ...process.env, ...server.env } },
    };

    const serverOptions = { run: execution, debug: execution };

    const clientOptions = {
        documentSelector: [{ scheme: 'file', language: 'caca' }],
        synchronize: {
            fileEvents: workspace.createFileSystemWatcher('**/*.caca'),
        },
        initializationOptions: {
            // Assemblies extern functions may bind to, matching the command
            // line's --ref. Read once at startup: reload the window after
            // changing the setting or building a listed assembly.
            references: (workspace.getConfiguration('cacalang').get('references') || [])
                .map((reference) => reference.replace(/\$\{workspaceFolder\}/g, workspaceRoot())),
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

/**
 * Works out how to start the language server.
 *
 * The configured path may use ${workspaceFolder}, so a setting committed to a
 * repository works for anyone who builds the server into it.
 *
 * A .dll is run through `dotnet`, which is found on PATH. The native launcher
 * beside it is not used by default: it locates the runtime through the standard
 * install locations and an explicit DOTNET_ROOT, neither of which covers a .NET
 * installed under the user's home directory, so it fails to start in exactly
 * the setup where an editor is most likely to launch it.
 */
/** The first workspace folder, which ${workspaceFolder} in settings expands to. */
function workspaceRoot() {
    return workspace.workspaceFolders && workspace.workspaceFolders.length > 0
        ? workspace.workspaceFolders[0].uri.fsPath
        : '';
}

function resolveServer() {
    const fs = require('fs');
    const path = require('path');
    const os = require('os');

    const configured = workspace.getConfiguration('cacalang').get('server.path');
    const expanded = (configured || '').replace(/\$\{workspaceFolder\}/g, workspaceRoot());
    const env = dotnetRoot() ? { DOTNET_ROOT: dotnetRoot() } : {};

    if (expanded.length === 0) {
        return { command: 'caca-langserver', args: [], env };
    }

    if (expanded.endsWith('.dll')) {
        if (!fs.existsSync(expanded)) {
            reportMissingServer(expanded);
        }

        return { command: 'dotnet', args: [expanded], env };
    }

    for (const candidate of [expanded, expanded + '.exe']) {
        if (fs.existsSync(candidate)) {
            return { command: candidate, args: [], env };
        }
    }

    reportMissingServer(expanded);
    return { command: 'caca-langserver', args: [], env };

    /** A .NET installed under the home directory, which the launcher will not find on its own. */
    function dotnetRoot() {
        const home = path.join(os.homedir(), '.dotnet');
        return fs.existsSync(path.join(home, 'dotnet')) ? home : undefined;
    }
}

function reportMissingServer(expected) {
    window.showWarningMessage(
        `cacalang: no language server at ${expected}. Build it with ` +
        '"dotnet publish src/Caca.LanguageServer -c Release -o artifacts/langserver".');
}

function deactivate() {
    return client ? client.stop() : undefined;
}

module.exports = { activate, deactivate };
