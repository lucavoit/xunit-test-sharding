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
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(ManagedAssemblyPath);

    /// <summary>
    /// The assembly containing the tests. For an app host, this is the .dll next to it; for a
    /// .NET Framework test project, this is the .exe itself.
    /// </summary>
    public string ManagedAssemblyPath { get; }

    private bool IsDll => Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the test assembly with the given arguments. They are passed through a response file
    /// ('@@ file', supported by every xUnit.net v3 version) so that the length of the command line
    /// does not limit the number of tests that can be selected.
    /// </summary>
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, bool captureOutput, bool verbose, CancellationToken cancellationToken)
    {
        var responseFile = System.IO.Path.GetTempFileName();
        try
        {
            // The runner trims each line and ignores empty lines, so every argument must be a single, trimmed line
            foreach (var argument in arguments)
            {
                if (argument.Length == 0 || argument.Trim().Length != argument.Length || argument.AsSpan().ContainsAny('\r', '\n'))
                    throw new ArgumentException($"The argument '{argument}' cannot be passed to the test assembly: arguments must not be empty, contain line breaks, or start or end with whitespace.");
            }

            await File.WriteAllLinesAsync(responseFile, arguments, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = IsDll ? "dotnet" : Path,
                UseShellExecute = false,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
            };

            if (IsDll)
                startInfo.ArgumentList.Add(Path);

            startInfo.ArgumentList.Add("@@");
            startInfo.ArgumentList.Add(responseFile);

            if (captureOutput)
            {
                // The output is parsed, so it must not be localized or colored
                startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
                startInfo.Environment["NO_COLOR"] = "1";
            }

            if (verbose)
            {
                Console.WriteLine($"> {string.Join(' ', startInfo.ArgumentList.Prepend(startInfo.FileName).Select(Quote))}");
                foreach (var argument in arguments)
                    Console.WriteLine($">   {argument}");
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
