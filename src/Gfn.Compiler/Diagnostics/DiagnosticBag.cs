using System.Collections;

namespace Gfn.Diagnostics;

/// <summary>
/// Collects errors as compilation proceeds so that a run can report several
/// problems at once instead of throwing on the first one.
/// </summary>
public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public int Count => _diagnostics.Count;

    public bool HasErrors => _diagnostics.Count > 0;

    public void Report(DiagnosticCode code, SourceLocation location, string message) =>
        _diagnostics.Add(new Diagnostic(code, message, location));

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
