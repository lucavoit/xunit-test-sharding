using System.Text.Json;
using System.Text.Json.Serialization;

namespace XunitTestSharding;

/// <summary>A test case reported by '-list full/json'.</summary>
internal sealed record DiscoveredTest(
    string Id,
    string DisplayName,
    string? ClassName,
    string? MethodName);

internal static class TestDiscovery
{
    // Theories are not pre-enumerated by default in xUnit.net v3: without this switch, a theory (and so a
    // Reqnroll scenario outline) is a single test case. It must be used both when listing and when running
    // the tests, otherwise the IDs do not match.
    public const string PreEnumerateTheoriesSwitch = "-preEnumerateTheories";

    public static async Task<IReadOnlyList<DiscoveredTest>> DiscoverAsync(TestAssembly assembly, IReadOnlyList<string> testArguments, bool verbose, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-list", "full/json", "-noColor" };
        arguments.AddRange(WithPreEnumeratedTheories(testArguments));

        var result = await assembly.RunAsync(arguments, captureOutput: true, verbose, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Listing the tests of '{assembly.Path}' failed with exit code {result.ExitCode}.{Environment.NewLine}{result.FormatOutput()}");

        return Parse(result.StandardOutput)
            ?? throw new InvalidOperationException($"Cannot parse the list of tests of '{assembly.Path}'.{Environment.NewLine}{result.FormatOutput()}");
    }

    public static IEnumerable<string> WithPreEnumeratedTheories(IReadOnlyList<string> testArguments) =>
        testArguments.Contains(PreEnumerateTheoriesSwitch, StringComparer.OrdinalIgnoreCase)
            ? testArguments
            : testArguments.Prepend(PreEnumerateTheoriesSwitch);

    /// <summary>
    /// Parses the output of '-list full/json': a single line containing a JSON array. Other lines (written
    /// by the test project itself, for instance) are ignored.
    /// </summary>
    public static IReadOnlyList<DiscoveredTest>? Parse(string output)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var end = line.LastIndexOf(']');
            if (!line.StartsWith('[') || end < 0)
                continue;

            // Ignore what follows the array, such as a trailing color reset sequence
            line = line[..(end + 1)];

            List<ListedTest>? listedTests;
            try
            {
                listedTests = JsonSerializer.Deserialize(line, ListJsonContext.Default.ListListedTest);
            }
            catch (JsonException)
            {
                continue;
            }

            if (listedTests is null)
                continue;

            return listedTests
                .Where(test => !string.IsNullOrEmpty(test.ID))
                .Select(test => new DiscoveredTest(test.ID!, test.DisplayName ?? test.ID!, test.Class, test.Method))
                .ToList();
        }

        return null;
    }

    internal sealed record ListedTest(string? ID, string? DisplayName, string? Class, string? Method);
}

[JsonSerializable(typeof(List<TestDiscovery.ListedTest>))]
internal sealed partial class ListJsonContext : JsonSerializerContext;
