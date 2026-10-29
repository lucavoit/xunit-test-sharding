using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace XunitTestSharding.Tests;

/// <summary>
/// Shards the Reqnroll sample project, with both command lines: the sample uses Microsoft.Testing.Platform, and its
/// executable also runs the xUnit.net runner when it gets '@@ response-file'.
/// </summary>
public sealed class EndToEndTests
{
    private const int SampleTestCount = 24;

    private static readonly string s_samplePath = typeof(EndToEndTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "SampleAssemblyPath")
        .Value!;

    private static readonly string[] s_serialTests =
    [
        "Insert a row",
        "Insert two rows",
        "Reqnroll.Sample.UnitTests.ExclusiveTests.UsesExclusiveResource",
        "Reqnroll.Sample.UnitTests.SerialTests.UsesSharedResource(value: \"a\")",
        "Reqnroll.Sample.UnitTests.SerialTests.UsesSharedResource(value: \"b\")",
    ];

    [Theory]
    [InlineData(1, "xunit")]
    [InlineData(2, "xunit")]
    [InlineData(5, "xunit")]
    [InlineData(30, "xunit")]
    [InlineData(1, "mtp")]
    [InlineData(5, "mtp")]
    public async Task PlansEveryTestOfTheSampleOnce(int totalShards, string runner)
    {
        var (plan, warnings, _) = await CreatePlanAsync(s_samplePath, totalShards, runner);

        Assert.Empty(warnings);
        Assert.Equal(SampleTestCount, plan.TotalTests);
        Assert.Equal(SampleTestCount, plan.Shards.SelectMany(shard => shard).Select(test => test.Id).Distinct().Count());
        Assert.Equal(s_serialTests, plan.SerialTests.Select(test => test.DisplayName));
        Assert.Superset(s_serialTests.ToHashSet(), plan.Shards[0].Select(test => test.DisplayName).ToHashSet());
    }

    [Theory]
    [InlineData("xunit")]
    [InlineData("mtp")]
    public async Task ScenarioOutlineExamplesAreSeparateTests(string runner)
    {
        var (plan, _, _) = await CreatePlanAsync(s_samplePath, totalShards: 1, runner);

        Assert.Equal(6, plan.Shards[0].Count(test => test.MethodName == "AddManyNumbers"));
    }

    [Fact]
    public async Task BothCommandLinesGiveTheSamePlan()
    {
        var (xunitPlan, _, _) = await CreatePlanAsync(s_samplePath, totalShards: 3, "xunit");
        var (mtpPlan, _, _) = await CreatePlanAsync(s_samplePath, totalShards: 3, "mtp");

        Assert.Equal(xunitPlan.Fingerprint, mtpPlan.Fingerprint);
        Assert.Equal(
            xunitPlan.Shards.Select(shard => shard.Select(test => (test.Id, test.DisplayName, test.ClassName, test.MethodName)).ToList()),
            mtpPlan.Shards.Select(shard => shard.Select(test => (test.Id, test.DisplayName, test.ClassName, test.MethodName)).ToList()));
    }

    [Theory]
    [InlineData("xunit", new[] { "-trait-", "Category=nonparallel" })]
    [InlineData("mtp", new[] { "--filter-not-trait", "Category=nonparallel" })]
    public async Task FiltersApplyToThePlan(string runner, string[] filter)
    {
        var (plan, _, filtersApplied) = await CreatePlanAsync(s_samplePath, totalShards: 2, runner, filter);

        Assert.True(filtersApplied);
        Assert.Equal(SampleTestCount - 2, plan.TotalTests);
        Assert.DoesNotContain(plan.Shards.SelectMany(shard => shard), test => test.ClassName == "Reqnroll.Sample.Features.DatabaseFeature");
        Assert.Equal(3, plan.SerialTests.Count);
    }

    [Fact]
    public async Task ThePlanIsTheSameWhenTheTestsAreInAnotherDirectory()
    {
        // Each agent downloads the tests to its own directory, which changes the IDs of the tests
        var copy = Directory.CreateTempSubdirectory("xunit-shard-sample-");
        try
        {
            var sampleDirectory = Path.GetDirectoryName(s_samplePath)!;
            foreach (var file in Directory.EnumerateFiles(sampleDirectory, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(copy.FullName, Path.GetRelativePath(sampleDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }

            var (plan, _, _) = await CreatePlanAsync(s_samplePath, totalShards: 3, "xunit");
            var (copyPlan, _, _) = await CreatePlanAsync(Path.Combine(copy.FullName, Path.GetFileName(s_samplePath)), totalShards: 3, "xunit");

            Assert.NotEqual(plan.Shards[0][0].Id, copyPlan.Shards[0][0].Id);
            Assert.Equal(plan.Fingerprint, copyPlan.Fingerprint);
            Assert.Equal(
                plan.Shards.Select(shard => shard.Select(test => test.DisplayName).ToList()),
                copyPlan.Shards.Select(shard => shard.Select(test => test.DisplayName).ToList()));
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("xunit")]
    [InlineData("mtp")]
    public async Task RunsEachShardWithItsOwnTests(string runner)
    {
        const int TotalShards = 3;
        var resultsDirectory = Directory.CreateTempSubdirectory("xunit-shard-tests-");
        try
        {
            var executedTests = new List<string>();
            for (var shardIndex = 1; shardIndex <= TotalShards; shardIndex++)
            {
                var junitPath = Path.Combine(resultsDirectory.FullName, $"shard-{shardIndex}.xml");
                string[] reportArguments = runner == "xunit"
                    ? ["-result-junit", junitPath]
                    : ["--report-xunit-junit", "--report-xunit-junit-filename", Path.GetFileName(junitPath), "--results-directory", resultsDirectory.FullName];
                var options = new ShardOptions(s_samplePath, shardIndex, TotalShards, reportArguments, DryRun: false, Verbose: false, ToRunner(runner));

                var exitCode = await ShardRunner.RunAsync(options, TextWriter.Null, TestContext.Current.CancellationToken);

                Assert.Equal(0, exitCode);
                var testCases = XDocument.Load(junitPath).Descendants("testcase").Select(testCase => (string)testCase.Attribute("name")!).ToList();
                Assert.Equal(8, testCases.Count);
                if (shardIndex == 1)
                    Assert.Superset(s_serialTests.ToHashSet(), testCases.ToHashSet());

                executedTests.AddRange(testCases);
            }

            Assert.Equal(SampleTestCount, executedTests.Count);
            Assert.Equal(SampleTestCount, executedTests.Distinct().Count());
        }
        finally
        {
            resultsDirectory.Delete(recursive: true);
        }
    }

    private static TestRunner ToRunner(string runner) =>
        runner == "xunit" ? TestRunner.Xunit : TestRunner.MicrosoftTestingPlatform;

    private static Task<DiscoveryResult> CreatePlanAsync(string samplePath, int totalShards, string runner, string[]? testArguments = null)
    {
        var options = new ShardOptions(samplePath, ShardIndex: 1, totalShards, testArguments ?? [], DryRun: true, Verbose: false, ToRunner(runner));
        return ShardRunner.CreatePlanAsync(new TestAssembly(samplePath), options, TestContext.Current.CancellationToken);
    }
}
