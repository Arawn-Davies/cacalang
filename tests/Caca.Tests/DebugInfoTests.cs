using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Caca.Tests;

/// <summary>
/// Reads back the symbols the compiler writes, which is the only way to know a
/// debugger will be able to follow a program to its source.
/// </summary>
[Collection(ConsoleCollection.Name)]
public class DebugInfoTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("caca-pdb-").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort.
        }
    }

    /// <summary>Compiles a program from a real file, so there is a source path to record.</summary>
    private (string PdbPath, string SourcePath) Build(string source, string name, bool withDebugInfo = true)
    {
        var sourcePath = Path.Combine(_directory, $"{name}.caca");
        File.WriteAllText(sourcePath, source);

        var compilation = Compilation.CreateFromFile(sourcePath);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var result = compilation.Emit(
            Path.Combine(_directory, $"{name}.exe"),
            withLauncher: false,
            sourcePath: withDebugInfo ? sourcePath : null);

        return (Path.ChangeExtension(result.AssemblyPath, ".pdb"), sourcePath);
    }

    private static T Read<T>(string pdbPath, Func<MetadataReader, T> read)
    {
        using var stream = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        return read(provider.GetMetadataReader());
    }

    /// <summary>Every source line the symbols say a program stops at.</summary>
    private static List<int> LinesOf(string pdbPath) => Read(pdbPath, reader =>
        (from handle in reader.MethodDebugInformation
         let info = reader.GetMethodDebugInformation(handle)
         where !info.SequencePointsBlob.IsNil
         from point in info.GetSequencePoints()
         where !point.IsHidden
         select point.StartLine).Distinct().Order().ToList());

    [Fact]
    public void Building_writes_symbols_beside_the_assembly()
    {
        var (pdbPath, _) = Build("""print "hello";""", "symbols");

        Assert.True(File.Exists(pdbPath));
    }

    [Fact]
    public void Asking_for_no_symbols_writes_none()
    {
        var (pdbPath, _) = Build("""print "hello";""", "nosymbols", withDebugInfo: false);

        Assert.False(File.Exists(pdbPath));
    }

    [Fact]
    public void The_symbols_name_the_source_file()
    {
        var (pdbPath, sourcePath) = Build("""print "hello";""", "document");

        var documents = Read(pdbPath, reader =>
            reader.Documents.Select(h => reader.GetString(reader.GetDocument(h).Name)).ToList());

        Assert.Contains(sourcePath, documents);
    }

    [Fact]
    public void Every_statement_maps_to_the_line_it_was_written_on()
    {
        var (pdbPath, _) = Build("""
            print 1;
            print 2;
            print 3;
            """, "lines");

        Assert.Equal([1, 2, 3], LinesOf(pdbPath));
    }

    [Fact]
    public void A_statement_that_is_not_on_the_first_line_maps_to_its_own_line()
    {
        var (pdbPath, _) = Build("""
            // a comment

            print "after the gap";
            """, "offset");

        Assert.Equal([3], LinesOf(pdbPath));
    }

    [Fact]
    public void A_loop_body_and_its_condition_are_both_stoppable()
    {
        var (pdbPath, _) = Build("""
            var i = 0;
            while i < 3 do
                print i;
                i = i + 1;
            end;
            """, "loop");

        // The declaration, the condition, and the two statements in the body.
        Assert.Equal([1, 2, 3, 4], LinesOf(pdbPath));
    }

    [Fact]
    public void Statements_inside_a_function_are_mapped_too()
    {
        var (pdbPath, _) = Build("""
            func twice(n: int): int do
                return n * 2;
            end

            print twice(21);
            """, "function");

        Assert.Equal([2, 5], LinesOf(pdbPath));
    }

    [Fact]
    public void Local_variables_carry_their_names()
    {
        var (pdbPath, _) = Build("""
            var total = 1;
            var label = "two";
            print total;
            print label;
            """, "locals");

        var names = Read(pdbPath, reader =>
            reader.LocalVariables.Select(h => reader.GetString(reader.GetLocalVariable(h).Name)).ToList());

        Assert.Contains("total", names);
        Assert.Contains("label", names);
    }

    [Fact]
    public void The_assembly_points_a_debugger_at_the_symbols()
    {
        // Writing a .pdb is not enough: the assembly has to carry a debug
        // directory entry naming it, or a debugger never looks for it.
        var (pdbPath, _) = Build("""print "hello";""", "directory");
        var assemblyPath = Path.ChangeExtension(pdbPath, ".dll");

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var entries = peReader.ReadDebugDirectory();
        var codeView = Assert.Single(entries, entry => entry.Type == DebugDirectoryEntryType.CodeView);
        var data = peReader.ReadCodeViewDebugDirectoryData(codeView);

        Assert.Equal(Path.GetFileName(pdbPath), Path.GetFileName(data.Path));
    }

    [Fact]
    public void A_program_built_with_symbols_still_runs()
    {
        // Debug information must not change what the program does.
        var sourcePath = Path.Combine(_directory, "runs.caca");
        File.WriteAllText(sourcePath, "for i = 1 to 3 do print i * i; end;");

        var compilation = Compilation.CreateFromFile(sourcePath);
        var result = compilation.Emit(
            Path.Combine(_directory, "runs.exe"), withLauncher: false, sourcePath: sourcePath);

        var context = new System.Runtime.Loader.AssemblyLoadContext("debug-info", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(result.AssemblyPath)));
            var main = assembly.GetType("Program")!.GetMethod("Main")!;

            var output = new StringWriter { NewLine = "\n" };
            var original = Console.Out;

            try
            {
                Console.SetOut(output);
                main.Invoke(null, null);
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.Equal(TestHost.Lines("1", "4", "9"), output.ToString());
        }
        finally
        {
            context.Unload();
        }
    }
}
