using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace XunitTestSharding.Tests;

/// <summary>Shards the Reqnroll sample project.</summary>
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
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(30)]
    public async Task PlansEveryTestOfTheSampleOnce(int totalShards)
    {
        var (plan, warnings) = await CreatePlanAsync(totalShards);

        Assert.Empty(warnings);
        Assert.Equal(SampleTestCount, plan.TotalTests);
        Assert.Equal(SampleTestCount, plan.Shards.SelectMany(shard => shard).Select(test => test.Id).Distinct().Count());
        Assert.Equal(s_serialTests, plan.SerialTests.Select(test => test.DisplayName));
        Assert.Superset(s_serialTests.ToHashSet(), plan.Shards[0].Select(test => test.DisplayName).ToHashSet());
    }

    [Fact]
    public async Task ScenarioOutlineExamplesAreSeparateTests()
    {
        var (plan, _) = await CreatePlanAsync(totalShards: 1);

        Assert.Equal(6, plan.Shards[0].Count(test => test.MethodName == "AddManyNumbers"));
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

            var (plan, _) = await CreatePlanAsync(s_samplePath, totalShards: 3);
            var (copyPlan, _) = await CreatePlanAsync(Path.Combine(copy.FullName, Path.GetFileName(s_samplePath)), totalShards: 3);

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

    [Fact]
    public async Task RunsEachShardWithItsOwnTests()
    {
        const int TotalShards = 3;
        var resultsDirectory = Directory.CreateTempSubdirectory("xunit-shard-tests-");
        try
        {
            var executedTests = new List<string>();
            for (var shardIndex = 1; shardIndex <= TotalShards; shardIndex++)
            {
                var junitPath = Path.Combine(resultsDirectory.FullName, $"shard-{shardIndex}.xml");
                var options = new ShardOptions(s_samplePath, shardIndex, TotalShards, ["-result-junit", junitPath], DryRun: false, Verbose: false);

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

    private static Task<(ShardPlan Plan, IReadOnlyList<string> Warnings)> CreatePlanAsync(int totalShards) =>
        CreatePlanAsync(s_samplePath, totalShards);

    private static Task<(ShardPlan Plan, IReadOnlyList<string> Warnings)> CreatePlanAsync(string samplePath, int totalShards)
    {
        var options = new ShardOptions(samplePath, ShardIndex: 1, totalShards, [], DryRun: true, Verbose: false);
        return ShardRunner.CreatePlanAsync(new TestAssembly(samplePath), options, TestContext.Current.CancellationToken);
    }
}
