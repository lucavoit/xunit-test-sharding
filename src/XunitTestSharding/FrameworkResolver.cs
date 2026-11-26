using System.Runtime.InteropServices;
using System.Text.Json;

namespace XunitTestSharding;

/// <summary>A shared framework that the test assembly runs on, found on this machine.</summary>
internal sealed record FrameworkDirectory(string Name, string RequestedVersion, string Version, string Path);

/// <summary>
/// Finds the shared frameworks a framework-dependent test assembly runs on, as declared in its runtimeconfig.json
/// (for instance Microsoft.NETCore.App 11.0.0 for a net11.0 test project), in the .NET installations of this
/// machine. Its metadata is then read with the framework assemblies it was built for, rather than with those of the
/// .NET that xunit-shard runs on, which can be older and miss types (UnionAttribute of .NET 11, for instance).
/// </summary>
internal static class FrameworkResolver
{
    public static (IReadOnlyList<FrameworkDirectory> Directories, IReadOnlyList<string> Warnings) Resolve(string assemblyPath, IEnumerable<string>? dotnetRoots = null)
    {
        var frameworks = ReadFrameworks(assemblyPath);
        if (frameworks.Count == 0)
            return ([], []);

        var roots = (dotnetRoots ?? GetDotnetRoots()).ToList();
        var directories = new List<FrameworkDirectory>();
        var warnings = new List<string>();
        foreach (var (name, requestedVersion) in frameworks)
        {
            var installed = roots
                .Select(root => System.IO.Path.Combine(root, "shared", name))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Select(path => (Version: System.IO.Path.GetFileName(path), Path: path))
                .ToList();

            var selected = SelectVersion(requestedVersion, installed.Select(candidate => candidate.Version));
            if (selected is null)
            {
                warnings.Add($"the test assembly runs on {name} {requestedVersion}, which is not installed " +
                    $"(searched: {(roots.Count == 0 ? "no .NET installation found" : string.Join(", ", roots))}). " +
                    "Its metadata is read with the framework assemblies of the .NET xunit-shard runs on.");
                continue;
            }

            directories.Add(new FrameworkDirectory(name, requestedVersion, selected, installed.First(candidate => candidate.Version == selected).Path));
        }

        return (directories, warnings);
    }

    /// <summary>
    /// The frameworks in the runtimeconfig.json next to the assembly. None for a self-contained test assembly (its
    /// framework assemblies are next to it) or when there is no runtimeconfig.json (.NET Framework).
    /// </summary>
    internal static List<(string Name, string Version)> ReadFrameworks(string assemblyPath)
    {
        var runtimeConfigPath = System.IO.Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        if (!File.Exists(runtimeConfigPath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions))
                return [];

            var frameworks = new List<(string, string)>();
            if (runtimeOptions.TryGetProperty("framework", out var framework))
                AddFramework(framework);

            if (runtimeOptions.TryGetProperty("frameworks", out var frameworkArray) && frameworkArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in frameworkArray.EnumerateArray())
                    AddFramework(item);
            }

            return frameworks;

            void AddFramework(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } frameworkName
                    && element.TryGetProperty("version", out var version) && version.GetString() is { Length: > 0 } frameworkVersion)
                {
                    frameworks.Add((frameworkName, frameworkVersion));
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Selects the installed version like the .NET host does by default (roll forward to the lowest higher minor
    /// version, then to its latest patch), with a fallback to the lowest higher major version, which is better than
    /// no framework at all for reading metadata. Pre-release versions are only used when a pre-release is requested.
    /// </summary>
    internal static string? SelectVersion(string requestedVersion, IEnumerable<string> installedVersions)
    {
        if (!TryParse(requestedVersion, out var requested, out var requestedIsPrerelease))
            return null;

        var candidates = installedVersions
            .Select(text => (Text: text, Parsed: TryParse(text, out var version, out var isPrerelease) ? version : null, IsPrerelease: isPrerelease))
            .Where(candidate => candidate.Parsed is not null && candidate.Parsed >= requested && (requestedIsPrerelease || !candidate.IsPrerelease))
            .Select(candidate => (candidate.Text, Version: candidate.Parsed!, candidate.IsPrerelease))
            .ToList();

        var sameMajor = candidates.Where(candidate => candidate.Version.Major == requested.Major).ToList();
        var pool = sameMajor.Count > 0
            ? sameMajor
            : candidates.Where(candidate => candidate.Version.Major == candidates.Min(other => other.Version.Major)).ToList();

        if (pool.Count == 0)
            return null;

        var minor = pool.Min(candidate => candidate.Version.Minor);
        return pool
            .Where(candidate => candidate.Version.Minor == minor)
            .OrderBy(candidate => candidate.Version)
            .ThenBy(candidate => candidate.IsPrerelease ? 0 : 1)
            .ThenBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Last()
            .Text;
    }

    /// <summary>
    /// The .NET installations of this machine: DOTNET_ROOT, the 'dotnet' on the PATH and the default locations.
    /// </summary>
    internal static IEnumerable<string> GetDotnetRoots()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT_" + RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()),
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
        };

        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = System.IO.Path.Combine(directory.Trim('"'), executable);
            if (!File.Exists(path))
                continue;

            // /usr/bin/dotnet is usually a link to the installation
            try
            {
                path = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
            }
            catch (IOException)
            {
            }

            candidates.Add(System.IO.Path.GetDirectoryName(path));
        }

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
            candidates.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"));
        }
        else
        {
            candidates.AddRange(["/usr/share/dotnet", "/usr/lib/dotnet", "/usr/local/share/dotnet", "/opt/dotnet"]);
        }

        if (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } userProfile)
            candidates.Add(System.IO.Path.Combine(userProfile, ".dotnet"));

        return candidates
            .Where(candidate => !string.IsNullOrEmpty(candidate) && Directory.Exists(System.IO.Path.Combine(candidate, "shared")))
            .Select(candidate => System.IO.Path.GetFullPath(candidate!))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryParse(string text, out Version version, out bool isPrerelease)
    {
        var end = text.IndexOfAny(['-', '+']);
        isPrerelease = end >= 0 && text[end] == '-';
        if (Version.TryParse(end < 0 ? text : text[..end], out var parsed))
        {
            // Compare 11.0 and 11.0.0 the same way
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }

        version = new Version();
        return false;
    }
}
