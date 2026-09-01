using System.Reflection;
using Caca.Binding;

namespace Caca.LanguageServer;

/// <summary>
/// The files the editor has open, and the last compilation of each.
/// </summary>
/// <remarks>
/// A language server compiles what is on screen, which has usually not been
/// saved and is usually not valid. The front end already reports every error it
/// can find rather than stopping at the first, which is exactly what an editor
/// needs.
/// </remarks>
public sealed class DocumentStore
{
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

    /// <summary>
    /// Assemblies extern targets resolve against, from the editor's
    /// <c>cacalang.references</c> setting — the counterpart of <c>--ref</c>.
    /// </summary>
    public IReadOnlyList<Assembly> References { get; set; } = [];

    public Document Update(string uri, string text)
    {
        var document = new Document(uri, text, Compilation.Create(text, uri, References));
        _documents[uri] = document;
        return document;
    }

    public void Remove(string uri) => _documents.Remove(uri);

    public Document? Find(string uri) => _documents.GetValueOrDefault(uri);
}

/// <summary>One open file.</summary>
public sealed record Document(string Uri, string Text, Compilation Compilation)
{
    public BindingResult Binding => Compilation.Binding;
}
