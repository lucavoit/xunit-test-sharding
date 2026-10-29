using Xunit;

namespace XunitTestSharding.Tests;

public sealed class ShardRunnerTests
{
    private static readonly DiscoveredTest[] s_tests =
    [
        new("id1", "Test 1", "Class", "Method"),
        new("id2", "Test 2", "Class", "Method"),
    ];

    [Fact]
    public void XunitRunArguments()
    {
        var options = CreateOptions(TestRunner.Xunit, ["-junit", "out.xml"]);

        Assert.Equal(
            ["-preEnumerateTheories", "-junit", "out.xml", "-id", "id1", "-id", "id2"],
            ShardRunner.BuildRunArguments(options, s_tests, filtersApplied: true));
    }

    [Fact]
    public void MicrosoftTestingPlatformRunArguments()
    {
        var options = CreateOptions(TestRunner.MicrosoftTestingPlatform, ["--report-junit"]);

        Assert.Equal(
            ["--pre-enumerate-theories", "on", "--report-junit", "--filter-uid", "id1", "id2"],
            ShardRunner.BuildRunArguments(options, s_tests, filtersApplied: true));
    }

    [Fact]
    public void ZeroTestsIsNotAnErrorWhenThePlanIgnoredTheFilters()
    {
        var options = CreateOptions(TestRunner.MicrosoftTestingPlatform, ["--filter-trait", "a=b"]);

        Assert.Equal(
            ["--pre-enumerate-theories", "on", "--filter-trait", "a=b", "--ignore-exit-code", "8", "--filter-uid", "id1", "id2"],
            ShardRunner.BuildRunArguments(options, s_tests, filtersApplied: false));
    }

    [Fact]
    public void TheIgnoredExitCodesOfTheUserAreKept()
    {
        var options = CreateOptions(TestRunner.MicrosoftTestingPlatform, ["--ignore-exit-code", "2"]);

        Assert.DoesNotContain("8", ShardRunner.BuildRunArguments(options, s_tests, filtersApplied: false));
    }

    private static ShardOptions CreateOptions(TestRunner runner, string[] testArguments) =>
        new("Tests.exe", ShardIndex: 1, TotalShards: 2, testArguments, DryRun: false, Verbose: false, runner);
}
