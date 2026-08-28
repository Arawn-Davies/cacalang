using System.Text;
using Caca.Runtime;

namespace Caca.Cli;

/// <summary>
/// An interactive prompt: type a statement, see what it does, keep the
/// variables and functions you have defined.
/// </summary>
/// <remarks>
/// The language has no notion of a session, so the REPL keeps the text of
/// everything that has been accepted so far and recompiles the whole of it on
/// each entry. Programs at a prompt are a few lines long, so compiling from
/// scratch every time is imperceptible and there is no separate incremental
/// path that could disagree with the compiler.
/// </remarks>
public sealed class Repl(TextReader input, TextWriter output)
{
    private const string Prompt = "caca> ";
    private const string Continuation = "  ... ";

    private readonly TextReader _input = input;
    private readonly TextWriter _output = output;

    /// <summary>Entries accepted so far, replayed before each new one.</summary>
    private readonly List<Entry> _history = [];

    /// <summary>Input the session has already consumed, so a replay does not consume more.</summary>
    private readonly List<string> _consumed = [];

    /// <summary>Everything the session has printed, so only new output is shown.</summary>
    private string _printed = string.Empty;

    public Repl()
        : this(Console.In, Console.Out)
    {
    }

    public int Run()
    {
        _output.WriteLine("cacalang REPL. Type a statement, or :help for commands.");

        while (true)
        {
            var entry = ReadEntry();

            if (entry is null)
            {
                return 0;
            }

            if (entry.Length == 0)
            {
                continue;
            }

            if (entry.StartsWith(':'))
            {
                if (!RunCommand(entry))
                {
                    return 0;
                }

                continue;
            }

            Evaluate(entry);
        }
    }

    /// <summary>
    /// Reads one entry, continuing across lines while the text is obviously
    /// unfinished, so a function or loop can be typed over several lines.
    /// </summary>
    private string? ReadEntry()
    {
        var entry = new StringBuilder();

        while (true)
        {
            _output.Write(entry.Length == 0 ? Prompt : Continuation);
            _output.Flush();

            var line = _input.ReadLine();

            if (line is null)
            {
                // End of input: finish whatever is pending, then stop.
                return entry.Length == 0 ? null : entry.ToString();
            }

            entry.AppendLine(line);
            var text = entry.ToString();

            if (!IsIncomplete(text))
            {
                return text.Trim();
            }
        }
    }

    /// <summary>
    /// Whether an entry is plainly unfinished, judged by counting the block
    /// openers against the <c>end</c>s that close them.
    /// </summary>
    private static bool IsIncomplete(string text)
    {
        var compilation = Compilation.Create(text);

        // Anything that compiles is finished.
        if (compilation.Succeeded)
        {
            return false;
        }

        var openers = 0;
        var closers = 0;

        foreach (var token in Syntax.Lexer.Tokenize(text, new Diagnostics.DiagnosticBag()))
        {
            switch (token.Kind)
            {
                case Syntax.TokenKind.FuncKeyword:
                case Syntax.TokenKind.ForKeyword:
                case Syntax.TokenKind.WhileKeyword:
                case Syntax.TokenKind.IfKeyword:
                    openers++;
                    break;

                case Syntax.TokenKind.EndKeyword:
                    closers++;
                    break;
            }
        }

        return openers > closers;
    }

    /// <summary>Runs an entry, keeping it if it worked.</summary>
    private void Evaluate(string entry)
    {
        // `print` is how this language produces output, but at a prompt a bare
        // expression should show its value. Rather than guess from the text
        // which is which, offer it to the compiler as an expression first and
        // fall back to running it as a statement.
        var printed = $"print {entry.TrimEnd().TrimEnd(';')};";
        var compilation = Compile(printed);

        if (compilation is null)
        {
            compilation = Compile(entry);

            if (compilation is null)
            {
                ReportBestErrors(entry, printed);
                return;
            }

            printed = entry;
        }

        try
        {
            // The whole session runs again from the top, so only what the new
            // entry added to the output is shown.
            var captured = new StringWriter { NewLine = "\n" };
            compilation.Run(new ReplayingReader(_consumed, _input), captured);

            var text = captured.ToString();
            _output.Write(text.Length >= _printed.Length ? text[_printed.Length..] : text);
            _output.Flush();

            _printed = text;
            _history.Add(new Entry(entry, printed));
        }
        catch (CacaRuntimeException exception)
        {
            _output.WriteLine($"error: {exception.Message}");
        }
    }

    /// <summary>
    /// Reports the errors from whichever reading of the entry went wrong less.
    /// </summary>
    /// <remarks>
    /// A bare name such as <c>total</c> parses as the start of an assignment
    /// and complains about a missing '=', which says nothing useful. Read as an
    /// expression it produces the error worth showing: the name is not
    /// declared. The reading with fewer complaints is the one that was
    /// probably meant, and a tie goes to what was actually typed.
    /// </remarks>
    private void ReportBestErrors(string entry, string printed)
    {
        var asTyped = Compilation.Create(Program(entry)).Diagnostics.ToList();
        var asExpression = Compilation.Create(Program(printed)).Diagnostics.ToList();
        var best = asExpression.Count < asTyped.Count ? asExpression : asTyped;

        foreach (var diagnostic in best)
        {
            _output.WriteLine($"error {diagnostic.Id}: {diagnostic.Message}");
        }
    }

    /// <summary>Compiles the session with one more entry, or null if it does not compile.</summary>
    private Compilation? Compile(string entry)
    {
        var compilation = Compilation.Create(Program(entry));
        return compilation.Succeeded ? compilation : null;
    }

    private string Program(string entry) =>
        string.Join('\n', _history.Select(item => item.Compiled).Append(entry));

    /// <summary>One accepted entry: what was typed, and what was run.</summary>
    private readonly record struct Entry(string Typed, string Compiled);

    /// <summary>
    /// Feeds a replayed session the input it has already consumed, and reads
    /// anything beyond that from the real input.
    /// </summary>
    /// <remarks>
    /// Each entry recompiles and reruns the whole session, so without this a
    /// program that reads input would consume a fresh line every time it was
    /// replayed.
    /// </remarks>
    private sealed class ReplayingReader(List<string> recorded, TextReader underlying) : TextReader
    {
        private int _index;

        public override string? ReadLine()
        {
            if (_index < recorded.Count)
            {
                return recorded[_index++];
            }

            var line = underlying.ReadLine();

            if (line is not null)
            {
                recorded.Add(line);
                _index++;
            }

            return line;
        }
    }

    /// <summary>Runs a <c>:</c> command. Returns false to leave the REPL.</summary>
    private bool RunCommand(string entry)
    {
        var command = entry.Split(' ', 2)[0];

        switch (command)
        {
            case ":quit":
            case ":exit":
                return false;

            case ":help":
                _output.WriteLine("""
                      :help     Show this help
                      :list     Show the session so far
                      :reset    Forget everything defined in this session
                      :quit     Leave

                    Anything else is compiled and run. A bare expression is printed.
                    """);
                return true;

            case ":list":
                _output.WriteLine(_history.Count == 0
                    ? "(nothing yet)"
                    : string.Join('\n', _history.Select(item => item.Typed)));
                return true;

            case ":reset":
                _history.Clear();
                _consumed.Clear();
                _printed = string.Empty;
                _output.WriteLine("Session reset.");
                return true;

            default:
                _output.WriteLine($"unknown command '{command}'; try :help");
                return true;
        }
    }
}
