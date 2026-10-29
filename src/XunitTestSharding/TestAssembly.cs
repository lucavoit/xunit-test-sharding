using System.Diagnostics;
using System.Text;

namespace XunitTestSharding;

/// <summary>
/// An xUnit.net v3 test assembly. v3 test projects are executables: they are either launched directly
/// (app host or .exe) or through 'dotnet' when the path points to the .dll.
/// </summary>
internal sealed class TestAssembly
{
    public TestAssembly(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(Path))
            throw new FileNotFoundException($"The test assembly '{Path}' does not exist.", Path);

        ManagedAssemblyPath = GetManagedAssemblyPath(Path);
        XunitVersion = GetXunitVersion(System.IO.Path.GetDirectoryName(ManagedAssemblyPath)!);
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(ManagedAssemblyPath);

    /// <summary>
    /// The assembly containing the tests. For an app host, this is the .dll next to it; for a
    /// .NET Framework test project, this is the .exe itself.
    /// </summary>
    public string ManagedAssemblyPath { get; }

    /// <summary>The version of xunit.v3.core next to the test assembly, if it can be read.</summary>
    public Version? XunitVersion { get; }

    private bool IsDll => Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the test assembly with the given arguments. They are passed through a response file so that the
    /// length of the command line does not limit the number of tests that can be selected:
    /// '@@ file' for the xUnit.net runner (every xUnit.net v3 version, also when the test project uses
    /// Microsoft.Testing.Platform), '@file' for Microsoft.Testing.Platform.
    /// </summary>
    public async Task<ProcessResult> RunAsync(TestRunner runner, IReadOnlyList<string> arguments, bool captureOutput, bool verbose, CancellationToken cancellationToken)
    {
        var lines = GetResponseFileLines(runner, arguments);
        var responseFile = System.IO.Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(responseFile, lines, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = IsDll ? "dotnet" : Path,
                UseShellExecute = false,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
            };

            if (IsDll)
                startInfo.ArgumentList.Add(Path);

            if (runner is TestRunner.MicrosoftTestingPlatform)
            {
                startInfo.ArgumentList.Add("@" + responseFile);
            }
            else
            {
                startInfo.ArgumentList.Add("@@");
                startInfo.ArgumentList.Add(responseFile);
            }

            if (captureOutput)
            {
                // The output is parsed, so it must not be localized or colored
                startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
                startInfo.Environment["NO_COLOR"] = "1";
            }

            if (verbose)
            {
                Console.WriteLine($"> {string.Join(' ', startInfo.ArgumentList.Prepend(startInfo.FileName).Select(Quote))}");
                foreach (var line in lines)
                    Console.WriteLine($">   {line}");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Cannot start '{startInfo.FileName}'.");

            var standardOutput = captureOutput ? process.StandardOutput.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);
            var standardError = captureOutput ? process.StandardError.ReadToEndAsync(cancellationToken) : Task.FromResult(string.Empty);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        }
        finally
        {
            File.Delete(responseFile);
        }
    }

    internal static List<string> GetResponseFileLines(TestRunner runner, IReadOnlyList<string> arguments)
    {
        var lines = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (argument.Length == 0 || argument.AsSpan().ContainsAny('\r', '\n'))
                throw new ArgumentException($"The argument '{argument}' cannot be passed to the test assembly: arguments must not be empty or contain line breaks.");

            if (runner is TestRunner.MicrosoftTestingPlatform)
            {
                // Microsoft.Testing.Platform splits the lines on whitespace, except between double quotes
                if (argument.Contains('"', StringComparison.Ordinal))
                    throw new ArgumentException($"The argument '{argument}' cannot be passed to the test assembly: Microsoft.Testing.Platform arguments must not contain double quotes.");

                lines.Add(argument.AsSpan().ContainsAny(' ', '\t') ? '"' + argument + '"' : argument);
            }
            else
            {
                // The xUnit.net runner trims each line and ignores empty lines: one line is one argument
                if (argument.Trim().Length != argument.Length)
                    throw new ArgumentException($"The argument '{argument}' cannot be passed to the test assembly: xUnit.net arguments must not start or end with whitespace.");

                lines.Add(argument);
            }
        }

        return lines;
    }

    private static Version? GetXunitVersion(string directory)
    {
        var path = System.IO.Path.Combine(directory, "xunit.v3.core.dll");
        if (!File.Exists(path))
            return null;

        // For instance '4.0.2+0c7aa4991f' or '4.1.0-pre.12'
        var productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        var end = productVersion?.IndexOfAny(['+', '-']) ?? -1;
        return Version.TryParse(end < 0 ? productVersion : productVersion![..end], out var version) ? version : null;
    }

    private static string GetManagedAssemblyPath(string path)
    {
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return path;

        // App host: 'Tests.exe' on Windows, 'Tests' elsewhere
        var dll = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.ChangeExtension(path, ".dll")
            : path + ".dll";

        return File.Exists(dll) ? dll : path;
    }

    private static string Quote(string value) =>
        value.Length > 0 && !value.AsSpan().ContainsAny(' ', '"') ? value : '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string FormatOutput()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(StandardOutput))
            builder.AppendLine(StandardOutput.TrimEnd());

        if (!string.IsNullOrWhiteSpace(StandardError))
            builder.AppendLine(StandardError.TrimEnd());

        return builder.ToString();
    }
}
