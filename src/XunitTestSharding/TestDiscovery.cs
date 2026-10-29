using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XunitTestSharding;

/// <summary>A test case reported by the listing of the test assembly.</summary>
internal sealed record DiscoveredTest(
    string Id,
    string DisplayName,
    string? ClassName,
    string? MethodName);

internal static class TestDiscovery
{
    // Theories are not pre-enumerated by default by the xUnit.net runner: without this switch, a theory (and so a
    // Reqnroll scenario outline) is a single test case. It must be used both when listing and when running the
    // tests, otherwise the IDs do not match.
    public const string PreEnumerateTheoriesSwitch = "-preEnumerateTheories";
    public const string MtpPreEnumerateTheoriesOption = "--pre-enumerate-theories";

    // Microsoft.Testing.Platform exit code when no test ran, which is expected when the tests are only listed
    // https://aka.ms/testingplatform/exitcodes
    public const int MtpZeroTestsExitCode = 8;

    /// <summary>
    /// Lists the tests with the xUnit.net runner ('-list full/json'). This works with every xUnit.net v3 version,
    /// including the test projects that use Microsoft.Testing.Platform, but only with xUnit.net arguments.
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredTest>> DiscoverWithXunitAsync(TestAssembly assembly, IReadOnlyList<string> testArguments, bool verbose, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-list", "full/json", "-noColor" };
        arguments.AddRange(WithPreEnumeratedTheories(testArguments));

        var result = await assembly.RunAsync(TestRunner.Xunit, arguments, captureOutput: true, verbose, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Listing the tests of '{assembly.Path}' failed with exit code {result.ExitCode}.{Environment.NewLine}{result.FormatOutput()}");

        return Parse(result.StandardOutput)
            ?? throw new InvalidOperationException($"Cannot parse the list of tests of '{assembly.Path}'.{Environment.NewLine}{result.FormatOutput()}");
    }

    /// <summary>
    /// Lists the tests with Microsoft.Testing.Platform ('--xunit-list full', xUnit.net v3 4.0 and later), so that
    /// Microsoft.Testing.Platform arguments, such as filters, apply.
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredTest>> DiscoverWithMtpAsync(TestAssembly assembly, IReadOnlyList<string> testArguments, bool verbose, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "--xunit-list", "full", "--no-ansi", MtpPreEnumerateTheoriesOption, "on" };
        arguments.AddRange(testArguments);

        var result = await assembly.RunAsync(TestRunner.MicrosoftTestingPlatform, arguments, captureOutput: true, verbose, cancellationToken);
        var tests = ParseMtpList(result.StandardOutput);
        if (result.ExitCode is not (0 or MtpZeroTestsExitCode) || tests is null)
        {
            throw new InvalidOperationException(
                $"Listing the tests of '{assembly.Path}' with Microsoft.Testing.Platform failed with exit code {result.ExitCode}. " +
                $"The test project must be built with <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>; " +
                $"use '--runner xunit' to pass xUnit.net arguments instead.{Environment.NewLine}{result.FormatOutput()}");
        }

        return tests;
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

    /// <summary>
    /// Parses the output of '--xunit-list full', which only exists as text:
    /// <code>
    /// Assembly: Tests
    ///   - Display name: "Add(a: \"1\")"
    ///     Test method:  Namespace.Class.Add
    ///     ID:           0123abcd...
    /// </code>
    /// Returns null when the output does not contain any listing.
    /// </summary>
    public static IReadOnlyList<DiscoveredTest>? ParseMtpList(string output)
    {
        const string AssemblyPrefix = "Assembly: ";
        const string DisplayNamePrefix = "- Display name: ";
        const string TestMethodPrefix = "Test method:";
        const string TestClassPrefix = "Test class:";
        const string IdPrefix = "ID:";

        List<DiscoveredTest>? tests = null;
        string? displayName = null;
        string? className = null;
        string? methodName = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
            {
                tests ??= [];
            }
            else if (tests is null)
            {
                continue;
            }
            else if (line.StartsWith(DisplayNamePrefix, StringComparison.Ordinal))
            {
                displayName = Unescape(line[DisplayNamePrefix.Length..]);
                className = null;
                methodName = null;
            }
            else if (line.StartsWith(TestMethodPrefix, StringComparison.Ordinal))
            {
                var testMethod = line[TestMethodPrefix.Length..].Trim();
                var separator = testMethod.LastIndexOf('.');
                (className, methodName) = separator < 0 ? (null, testMethod) : (testMethod[..separator], testMethod[(separator + 1)..]);
            }
            else if (line.StartsWith(TestClassPrefix, StringComparison.Ordinal))
            {
                className = line[TestClassPrefix.Length..].Trim();
            }
            else if (line.StartsWith(IdPrefix, StringComparison.Ordinal) && displayName is not null)
            {
                tests.Add(new DiscoveredTest(line[IdPrefix.Length..].Trim(), displayName, className, methodName));
                displayName = null;
            }
        }

        return tests;
    }

    /// <summary>Reverses the escaping of a quoted value of '--xunit-list full' (\\, \", \r and \n).</summary>
    private static string Unescape(string quoted)
    {
        var value = quoted.Length >= 2 && quoted[0] == '"' && quoted[^1] == '"' ? quoted[1..^1] : quoted;
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            builder.Append(value[++i] switch
            {
                'r' => '\r',
                'n' => '\n',
                var escaped => escaped,
            });
        }

        return builder.ToString();
    }

    internal sealed record ListedTest(string? ID, string? DisplayName, string? Class, string? Method);
}

[JsonSerializable(typeof(List<TestDiscovery.ListedTest>))]
internal sealed partial class ListJsonContext : JsonSerializerContext;
