using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Caca.Emit;

/// <summary>
/// Produces the small native launcher that lets a compiled program be run
/// directly, rather than through <c>dotnet program.dll</c>.
/// </summary>
/// <remarks>
/// <para>
/// On .NET Framework an <c>.exe</c> was the assembly itself. On modern .NET an
/// assembly is always a <c>.dll</c>, and the <c>.exe</c> beside it is an
/// "apphost": a native stub that locates the runtime and hands it the assembly.
/// The SDK ships a template of that stub with a 1024-byte region holding a
/// known placeholder string; writing the assembly's file name over the
/// placeholder is all it takes to turn the template into a launcher for one
/// particular program. This is what <c>dotnet build</c> does.
/// </para>
/// <para>
/// The template is platform specific, so the launcher produced here runs on the
/// machine that produced it.
/// </para>
/// </remarks>
public static class AppHost
{
    /// <summary>
    /// The placeholder the template holds, which is the SHA-256 of "foobar".
    /// The native stub reads the assembly path from this location.
    /// </summary>
    private const string PathPlaceholder = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";

    /// <summary>The space the template reserves for the assembly path.</summary>
    private const int MaxPathLength = 1024;

    /// <summary>Creates a launcher next to an emitted assembly.</summary>
    /// <param name="assemblyPath">The emitted assembly the launcher should start.</param>
    /// <param name="launcherPath">Where to write the launcher.</param>
    /// <param name="warning">Why no launcher could be produced, when that happens.</param>
    /// <returns>False if the launcher could not be produced, which is not fatal.</returns>
    public static bool TryCreate(string assemblyPath, string launcherPath, out string? warning)
    {
        var template = FindTemplate();

        if (template is null)
        {
            warning = "could not find the apphost template that ships with the .NET SDK, so no launcher " +
                      $"was written; run the program with 'dotnet {Path.GetFileName(assemblyPath)}'";
            return false;
        }

        // The launcher finds its assembly by name, beside itself.
        var assemblyName = Encoding.UTF8.GetBytes(Path.GetFileName(assemblyPath));

        if (assemblyName.Length >= MaxPathLength)
        {
            warning = "the output file name is too long to embed in a launcher";
            return false;
        }

        var image = File.ReadAllBytes(template);
        var placeholder = Encoding.UTF8.GetBytes(PathPlaceholder);
        var offset = IndexOf(image, placeholder);

        if (offset < 0)
        {
            warning = "the apphost template does not contain the expected placeholder, so no launcher was written";
            return false;
        }

        assemblyName.CopyTo(image, offset);

        // A null terminates the path, and the rest of the placeholder is cleared
        // so no fragment of it survives.
        for (var i = assemblyName.Length; i < placeholder.Length; i++)
        {
            image[offset + i] = 0;
        }

        File.WriteAllBytes(launcherPath, image);
        return MakeRunnable(launcherPath, out warning);
    }

    /// <summary>Gives the launcher whatever the platform needs before it will run.</summary>
    private static bool MakeRunnable(string launcherPath, out string? warning)
    {
        warning = null;

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        File.SetUnixFileMode(
            launcherPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        // Editing the template invalidated its signature, and macOS on Apple
        // silicon refuses to start a binary whose signature does not match. An
        // ad-hoc signature is enough to run locally.
        if (TryRun("codesign", ["--sign", "-", "--force", launcherPath]))
        {
            return true;
        }

        warning = "the launcher could not be re-signed with 'codesign', so macOS may refuse to run it";
        return false;
    }

    private static bool TryRun(string fileName, string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is SystemException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Locates the apphost template for the current platform inside the
    /// installed SDK's packs directory.
    /// </summary>
    private static string? FindTemplate()
    {
        var runtimeIdentifier = CurrentRuntimeIdentifier();

        if (runtimeIdentifier is null)
        {
            return null;
        }

        var fileName = OperatingSystem.IsWindows() ? "apphost.exe" : "apphost";

        foreach (var root in DotnetRoots())
        {
            var pack = Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{runtimeIdentifier}");

            if (!Directory.Exists(pack))
            {
                continue;
            }

            foreach (var version in VersionsNewestFirst(pack))
            {
                var candidate = Path.Combine(
                    version, "runtimes", runtimeIdentifier, "native", fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Version directories, preferring the running runtime's own version.</summary>
    private static IEnumerable<string> VersionsNewestFirst(string pack)
    {
        var running = Environment.Version;

        return Directory.EnumerateDirectories(pack)
            .Select(directory => (Path: directory, Version: ParseVersion(Path.GetFileName(directory))))
            .OrderByDescending(entry => entry.Version?.Major == running.Major)
            .ThenByDescending(entry => entry.Version)
            .Select(entry => entry.Path);
    }

    private static Version? ParseVersion(string? name)
    {
        // Directory names may carry a prerelease suffix, as in "10.0.0-rc.1".
        var numeric = name?.Split('-')[0];
        return Version.TryParse(numeric, out var version) ? version : null;
    }

    /// <summary>Where a .NET installation might be, most likely first.</summary>
    private static IEnumerable<string> DotnetRoots()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } configured)
        {
            yield return configured;
        }

        // The framework this compiler is running on lives at
        // <root>/shared/Microsoft.NETCore.App/<version>, so the root is three
        // directories above it.
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location);

        if (framework is not null)
        {
            var root = Path.GetFullPath(Path.Combine(framework, "..", "..", ".."));

            if (Directory.Exists(root))
            {
                yield return root;
            }
        }

        foreach (var wellKnown in WellKnownRoots())
        {
            if (Directory.Exists(wellKnown))
            {
                yield return wellKnown;
            }
        }
    }

    private static IEnumerable<string> WellKnownRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files\dotnet";
            yield break;
        }

        yield return "/usr/share/dotnet";
        yield return "/usr/local/share/dotnet";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
    }

    private static string? CurrentRuntimeIdentifier()
    {
        var os =
            OperatingSystem.IsWindows() ? "win" :
            OperatingSystem.IsMacOS() ? "osx" :
            OperatingSystem.IsLinux() ? "linux" :
            null;

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => null,
        };

        return os is null || architecture is null ? null : $"{os}-{architecture}";
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        var limit = haystack.Length - needle.Length;

        for (var i = 0; i <= limit; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
