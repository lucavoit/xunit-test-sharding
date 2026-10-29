using Xunit;

namespace XunitTestSharding.Tests;

public sealed class ShardPlannerTests
{
    private static readonly TestCollectionInfo s_serial = new("Serial", DisableParallelization: true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(50)]
    public void EveryTestIsAssignedToExactlyOneShard(int totalShards)
    {
        var tests = CreateTests(classCount: 5, testsPerClass: 7);

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards);

        Assert.Equal(totalShards, plan.Shards.Count);
        var assigned = plan.Shards.SelectMany(shard => shard).Select(test => test.Id).ToList();
        Assert.Equal(tests.Count, assigned.Count);
        Assert.Equal(tests.Select(test => test.Id).Order(StringComparer.Ordinal), assigned.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void ShardsAreBalanced(int totalShards)
    {
        var tests = CreateTests(classCount: 4, testsPerClass: 5);

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards);

        var sizes = plan.Shards.Select(shard => shard.Count).ToList();
        Assert.True(sizes.Max() - sizes.Min() <= 1, $"Unbalanced shards: {string.Join(", ", sizes)}");
    }

    [Fact]
    public void TestsOfCollectionsThatDisableParallelizationAreKeptOnTheFirstShard()
    {
        var tests = CreateTests(classCount: 6, testsPerClass: 4);
        TestCollectionInfo GetCollection(string? className) =>
            className is "Class1" or "Class4" ? s_serial : TestCollectionInfo.Default;

        var plan = ShardPlanner.Plan(tests, GetCollection, totalShards: 3);

        var serialIds = tests.Where(test => test.ClassName is "Class1" or "Class4").Select(test => test.Id).ToHashSet();
        Assert.Equal(serialIds, plan.SerialTests.Select(test => test.Id).ToHashSet());
        Assert.Superset(serialIds, plan.Shards[0].Select(test => test.Id).ToHashSet());
        Assert.All(plan.Shards.Skip(1).SelectMany(shard => shard), test => Assert.DoesNotContain(test.Id, serialIds));
        Assert.Equal(8, plan.SerialCollections["Serial"]);

        // 24 tests in 3 shards: the first shard completes the 8 serial tests with no parallel test
        Assert.Equal([8, 8, 8], plan.Shards.Select(shard => shard.Count));
    }

    [Fact]
    public void SerialTestsLargerThanAShardAreNotSplit()
    {
        var tests = CreateTests(classCount: 2, testsPerClass: 10);
        TestCollectionInfo GetCollection(string? className) => className is "Class0" ? s_serial : TestCollectionInfo.Default;

        var plan = ShardPlanner.Plan(tests, GetCollection, totalShards: 4);

        Assert.Equal([10, 4, 3, 3], plan.Shards.Select(shard => shard.Count));
        Assert.All(plan.Shards[0], test => Assert.Equal("Class0", test.ClassName));
    }

    [Fact]
    public void TestsOfAClassStayTogetherWhenPossible()
    {
        var tests = CreateTests(classCount: 3, testsPerClass: 4);

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 3);

        Assert.All(plan.Shards, shard => Assert.Single(shard.Select(test => test.ClassName).Distinct()));
    }

    [Fact]
    public void ThePlanDoesNotDependOnTheDiscoveryOrder()
    {
        var tests = CreateTests(classCount: 4, testsPerClass: 6);
        var shuffled = tests.OrderBy(test => test.Id.GetHashCode(StringComparison.Ordinal)).Reverse().ToList();

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 5);
        var shuffledPlan = ShardPlanner.Plan(shuffled, ParallelOnly, totalShards: 5);

        Assert.Equal(plan.Fingerprint, shuffledPlan.Fingerprint);
        Assert.Equal(
            plan.Shards.Select(shard => shard.Select(test => test.Id).ToList()),
            shuffledPlan.Shards.Select(shard => shard.Select(test => test.Id).ToList()));
    }

    [Fact]
    public void ThePlanDoesNotDependOnTheIds()
    {
        // xUnit.net computes the IDs from the path of the test assembly, so they differ between agents
        var tests = CreateTests(classCount: 3, testsPerClass: 5);
        var otherAgentTests = tests.Select(test => test with { Id = "other-" + new string(test.Id.Reverse().ToArray()) }).ToList();

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 4);
        var otherAgentPlan = ShardPlanner.Plan(otherAgentTests, ParallelOnly, totalShards: 4);

        Assert.Equal(plan.Fingerprint, otherAgentPlan.Fingerprint);
        Assert.Equal(
            plan.Shards.Select(shard => shard.Select(test => test.DisplayName + test.ClassName).ToList()),
            otherAgentPlan.Shards.Select(shard => shard.Select(test => test.DisplayName + test.ClassName).ToList()));
    }

    [Fact]
    public void TestsWithTheSameNameAreKeptTogether()
    {
        // Without their ID, these tests cannot be ordered the same way on every agent
        List<DiscoveredTest> tests =
        [
            new("a", "Test", "Class", "Method"),
            new("b", "Same", "Class", "Method"),
            new("c", "Same", "Class", "Method"),
            new("d", "Same", "Class", "Method"),
            new("e", "Zzz", "Class", "Method"),
            new("f", "Zzz", "Class", "Other"),
        ];

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 3);

        // Sorted: b, c, d (Same), a (Test), e (Zzz), f (Other method). The first shard should get 2 tests,
        // but the 3 tests named 'Same' are not split.
        Assert.Equal(["b", "c", "d"], plan.Shards[0].Select(test => test.Id));
        Assert.Equal(["a"], plan.Shards[1].Select(test => test.Id));
        Assert.Equal(["e", "f"], plan.Shards[2].Select(test => test.Id));
    }

    [Fact]
    public void TheFingerprintChangesWithTheTests()
    {
        var tests = CreateTests(classCount: 2, testsPerClass: 2);

        var plan = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 2);
        var otherPlan = ShardPlanner.Plan(tests.Skip(1).ToList(), ParallelOnly, totalShards: 2);
        var otherShardCount = ShardPlanner.Plan(tests, ParallelOnly, totalShards: 3);

        Assert.NotEqual(plan.Fingerprint, otherPlan.Fingerprint);
        Assert.NotEqual(plan.Fingerprint, otherShardCount.Fingerprint);
    }

    [Fact]
    public void DuplicateIdsAreRunOnce()
    {
        var test = new DiscoveredTest("id", "Test", "Class", "Method");

        var plan = ShardPlanner.Plan([test, test], ParallelOnly, totalShards: 2);

        Assert.Equal(1, plan.TotalTests);
    }

    [Theory]
    [InlineData(0, 10, 1, new[] { 10 })]
    [InlineData(0, 10, 3, new[] { 3, 4, 3 })]
    [InlineData(2, 10, 3, new[] { 2, 4, 4 })]
    [InlineData(5, 10, 3, new[] { 0, 5, 5 })]
    [InlineData(20, 10, 3, new[] { 0, 5, 5 })]
    [InlineData(0, 2, 4, new[] { 0, 1, 1, 0 })]
    public void ParallelTestCounts(int serialCount, int parallelCount, int totalShards, int[] expected)
    {
        Assert.Equal(expected, ShardPlanner.GetParallelTestCounts(serialCount, parallelCount, totalShards));
    }

    private static TestCollectionInfo ParallelOnly(string? className) => TestCollectionInfo.Default;

    private static List<DiscoveredTest> CreateTests(int classCount, int testsPerClass) =>
        Enumerable.Range(0, classCount)
            .SelectMany(c => Enumerable.Range(0, testsPerClass).Select(t => new DiscoveredTest($"id-{c}-{t}", $"Test {t}", $"Class{c}", "Method")))
            .ToList();
}
