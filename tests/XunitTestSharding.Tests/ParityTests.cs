using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace XunitTestSharding.Tests;

// The tests set an environment variable that the sample inherits, so they must not run at the same time as others
[CollectionDefinition(nameof(SampleFailuresCollection), DisableParallelization = true)]
public sealed class SampleFailuresCollection;

/// <summary>
/// Proves that sharding does not change the results, and that both command lines behave the same:
/// <list type="bullet">
/// <item>running every shard gives exactly the results of running the whole sample without xunit-shard: the same
/// tests, each once, with the same outcome;</item>
/// <item>the xUnit.net and Microsoft.Testing.Platform command lines give the same plan, and the same results on
/// every shard.</item>
/// </list>
/// Some tests of the sample fail (XUNIT_SHARD_SAMPLE_FAILURES=1) and some are skipped, so every outcome is compared.
/// </summary>
[Collection(nameof(SampleFailuresCollection))]
public sealed class ParityTests : IDisposable
{
    private readonly DirectoryInfo _resultsDirectory = Directory.CreateTempSubdirectory("xunit-shard-parity-");

    public ParityTests() =>
        Environment.SetEnvironmentVariable(Sample.FailuresVariable, "1");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Sample.FailuresVariable, null);
        _resultsDirectory.Delete(recursive: true);
    }

    [Fact]
    public async Task TheSampleHasTheSameResultsWithBothCommandLines()
    {
        var xunit = await RunUnshardedAsync("xunit");
        var mtp = await RunUnshardedAsync("mtp");

        Assert.Equal(Sample.TestCount, xunit.Results.Count);
        Assert.Equal(3, xunit.Results.Count(result => result.Outcome == Outcome.Failed));
        Assert.Equal(2, xunit.Results.Count(result => result.Outcome == Outcome.Skipped));
        Assert.Equal(xunit.Results, mtp.Results);
        Assert.NotEqual(0, xunit.ExitCode);
        Assert.NotEqual(0, mtp.ExitCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(45)]
    public async Task BothCommandLinesGiveTheSamePlan(int totalShards)
    {
        var xunitPlan = await CreatePlanAsync("xunit", totalShards, []);
        var mtpPlan = await CreatePlanAsync("mtp", totalShards, []);

        AssertSamePlan(xunitPlan, mtpPlan);
    }

    [Fact]
    public async Task BothCommandLinesGiveTheSamePlanWithEquivalentFilters()
    {
        var xunitPlan = await CreatePlanAsync("xunit", totalShards: 3, ["-trait-", "Category=nonparallel", "-class-", "Reqnroll.Sample.UnitTests.ParallelTests"]);
        var mtpPlan = await CreatePlanAsync("mtp", totalShards: 3, ["--filter-not-trait", "Category=nonparallel", "--filter-not-class", "Reqnroll.Sample.UnitTests.ParallelTests"]);

        Assert.Equal(Sample.TestCount - 2 - 5, xunitPlan.TotalTests);
        AssertSamePlan(xunitPlan, mtpPlan);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task ShardedRunsGiveTheResultsOfAnUnshardedRunWithBothCommandLines(int totalShards)
    {
        var unsharded = await RunUnshardedAsync("xunit");
        var xunitShards = await RunAllShardsAsync("xunit", totalShards, []);
        var mtpShards = await RunAllShardsAsync("mtp", totalShards, []);

        // Same tests, each run once, with the same outcome as without sharding
        Assert.Equal(unsharded.Results, Merge(xunitShards));
        Assert.Equal(unsharded.Results, Merge(mtpShards));

        for (var i = 0; i < totalShards; i++)
        {
            // Same tests and outcomes on each shard with both command lines
            Assert.Equal(xunitShards[i].Results, mtpShards[i].Results);

            // A shard fails exactly when one of its tests fails (the exit codes differ: 1 for xUnit.net, 2 for MTP)
            var hasFailures = xunitShards[i].Results.Any(result => result.Outcome == Outcome.Failed);
            Assert.Equal(hasFailures, xunitShards[i].ExitCode != 0);
            Assert.Equal(hasFailures, mtpShards[i].ExitCode != 0);
        }
    }

    [Fact]
    public async Task ShardedRunsGiveTheResultsOfAnUnshardedRunWithEquivalentFilters()
    {
        const int TotalShards = 3;
        var unsharded = await RunUnshardedAsync("xunit", ["-trait-", "Category=nonparallel"]);
        var xunitShards = await RunAllShardsAsync("xunit", TotalShards, ["-trait-", "Category=nonparallel"]);
        var mtpShards = await RunAllShardsAsync("mtp", TotalShards, ["--filter-not-trait", "Category=nonparallel"]);

        Assert.Equal(Sample.TestCount - 2, unsharded.Results.Count);
        Assert.Equal(unsharded.Results, Merge(xunitShards));
        Assert.Equal(unsharded.Results, Merge(mtpShards));
        for (var i = 0; i < TotalShards; i++)
            Assert.Equal(xunitShards[i].Results, mtpShards[i].Results);
    }

    private static void AssertSamePlan(ShardPlan xunitPlan, ShardPlan mtpPlan)
    {
        Assert.Equal(xunitPlan.Fingerprint, mtpPlan.Fingerprint);
        Assert.Equal(
            xunitPlan.Shards.Select(shard => shard.ToList()),
            mtpPlan.Shards.Select(shard => shard.ToList()));
        Assert.Equal(xunitPlan.SerialCollections, mtpPlan.SerialCollections);
    }

    private static async Task<ShardPlan> CreatePlanAsync(string runner, int totalShards, string[] testArguments)
    {
        var options = new ShardOptions(Sample.AssemblyPath, ShardIndex: 1, totalShards, testArguments, DryRun: true, Verbose: false, Sample.ToRunner(runner));
        var discovery = await ShardRunner.CreatePlanAsync(new TestAssembly(Sample.AssemblyPath), options, TestContext.Current.CancellationToken);
        Assert.True(discovery.FiltersApplied);
        return discovery.Plan;
    }

    /// <summary>Runs every shard with xunit-shard, as the agents of a pipeline would.</summary>
    private async Task<List<RunResult>> RunAllShardsAsync(string runner, int totalShards, string[] testArguments)
    {
        var shards = new List<RunResult>();
        for (var shardIndex = 1; shardIndex <= totalShards; shardIndex++)
        {
            var report = NewReportPath();
            var options = new ShardOptions(
                Sample.AssemblyPath, shardIndex, totalShards, [.. ReportArguments(runner, report), .. testArguments], DryRun: false, Verbose: false, Sample.ToRunner(runner));

            var exitCode = await ShardRunner.RunAsync(options, TextWriter.Null, TestContext.Current.CancellationToken);

            // A shard without tests does not run the test assembly, so it has no report
            shards.Add(new RunResult(exitCode, File.Exists(report) ? ReadJUnit(report) : []));
        }

        return shards;
    }

    /// <summary>Runs the whole sample directly, without xunit-shard.</summary>
    private async Task<RunResult> RunUnshardedAsync(string runner, string[]? testArguments = null)
    {
        var report = NewReportPath();
        var arguments = ReportArguments(runner, report).Concat(testArguments ?? []).ToList();
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add(Sample.AssemblyPath);
        if (runner == "xunit")
        {
            // The sample uses Microsoft.Testing.Platform: its executable only runs the xUnit.net runner with '@@ file'
            var responseFile = Path.Combine(_resultsDirectory.FullName, Guid.NewGuid().ToString("N") + ".rsp");
            await File.WriteAllLinesAsync(responseFile, arguments, TestContext.Current.CancellationToken);
            startInfo.ArgumentList.Add("@@");
            startInfo.ArgumentList.Add(responseFile);
        }
        else
        {
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(output, error);

        Assert.True(File.Exists(report), $"No report was generated.{Environment.NewLine}{await output}{await error}");
        return new RunResult(process.ExitCode, ReadJUnit(report));
    }

    private string NewReportPath() =>
        Path.Combine(_resultsDirectory.FullName, Guid.NewGuid().ToString("N") + ".xml");

    private static string[] ReportArguments(string runner, string report) =>
        runner == "xunit"
            ? ["-result-junit", report]
            : ["--report-xunit-junit", "--report-xunit-junit-filename", Path.GetFileName(report), "--results-directory", Path.GetDirectoryName(report)!];

    private static List<TestResult> Merge(IEnumerable<RunResult> shards) =>
        Sort(shards.SelectMany(shard => shard.Results));

    private static List<TestResult> ReadJUnit(string path) =>
        Sort(XDocument.Load(path).Descendants("testcase").Select(testCase => new TestResult(
            (string?)testCase.Attribute("classname") ?? "",
            (string)testCase.Attribute("name")!,
            testCase.Element("failure") is not null || testCase.Element("error") is not null ? Outcome.Failed
                : testCase.Element("skipped") is not null ? Outcome.Skipped
                : Outcome.Passed)));

    private static List<TestResult> Sort(IEnumerable<TestResult> results) =>
        results.OrderBy(result => result.ClassName, StringComparer.Ordinal).ThenBy(result => result.Name, StringComparer.Ordinal).ToList();

    public enum Outcome
    {
        Passed,
        Failed,
        Skipped,
    }

    public sealed record TestResult(string ClassName, string Name, Outcome Outcome);

    private sealed record RunResult(int ExitCode, List<TestResult> Results);
}
