using System.Diagnostics;

namespace Caca.Tests;

/// <summary>
/// Compiles programs to a launcher and runs the launcher as a separate
/// process, which is the only way to prove the produced executable actually
/// starts on the platform the tests are running on.
/// </summary>
public class AppHostTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("caca-apphost-").FullName;

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

    private EmitResult Build(string source, string name, bool withLauncher = true)
    {
        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));
        return compilation.Emit(Path.Combine(_directory, $"{name}.exe"), withLauncher);
    }

    [Fact]
    public void Build_writes_a_launcher_an_assembly_and_a_runtime_config()
    {
        var result = Build("""print "hello, world";""", "hello");

        Assert.NotNull(result.LauncherPath);
        Assert.True(File.Exists(result.LauncherPath));
        Assert.True(File.Exists(result.AssemblyPath));
        Assert.True(File.Exists(result.RuntimeConfigPath));

        // The runnable file keeps the .exe name on every platform; the assembly
        // beside it has to be a .dll.
        Assert.Equal(".exe", Path.GetExtension(result.LauncherPath));
        Assert.Equal(".dll", Path.GetExtension(result.AssemblyPath));
    }

    [Fact]
    public void The_launcher_runs_the_program()
    {
        var result = Build("""print "hello, world";""", "greet");
        Assert.NotNull(result.LauncherPath);

        var (output, exitCode) = Execute(result.LauncherPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("hello, world", output.Trim());
    }

    [Fact]
    public void The_launcher_runs_a_program_that_reads_input()
    {
        var result = Build("""
            func triple(n: int): int do return n * 3; end
            var n = 0;
            read_int n;
            print triple(n);
            """, "triple");

        Assert.NotNull(result.LauncherPath);

        var (output, exitCode) = Execute(result.LauncherPath, "7\n");

        Assert.Equal(0, exitCode);
        Assert.Equal("21", output.Trim());
    }

    [Fact]
    public void Asking_for_no_launcher_emits_only_the_assembly()
    {
        var result = Build("""print 1;""", "bare", withLauncher: false);

        Assert.Null(result.LauncherPath);
        Assert.Null(result.Warning);
        Assert.True(File.Exists(result.AssemblyPath));
        Assert.False(File.Exists(Path.Combine(_directory, "bare.exe")));
    }

    [Fact]
    public void The_launcher_is_executable_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = Build("""print 1;""", "mode");
        Assert.NotNull(result.LauncherPath);

        var mode = File.GetUnixFileMode(result.LauncherPath);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute), $"expected the launcher to be executable, got {mode}");
    }

    private static (string Output, int ExitCode) Execute(string launcher, string input = "")
    {
        var startInfo = new ProcessStartInfo(launcher)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(launcher),
        };

        // The launcher probes for a runtime, and a .NET installed somewhere
        // other than the system location is found through this variable.
        if (DotnetRoot() is { } root)
        {
            startInfo.Environment["DOTNET_ROOT"] = root;
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        process.StandardInput.Write(input);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "the launcher did not exit within 30 seconds");
        Assert.True(string.IsNullOrWhiteSpace(error), $"the launcher wrote to standard error: {error}");

        return (output, process.ExitCode);
    }

    /// <summary>The .NET installation this test run is using.</summary>
    private static string? DotnetRoot()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } configured)
        {
            return configured;
        }

        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return framework is null ? null : Path.GetFullPath(Path.Combine(framework, "..", "..", ".."));
    }
}
