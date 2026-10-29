using System.Security.Cryptography;
using System.Text;

namespace XunitTestSharding;

internal sealed record ShardPlan(
    IReadOnlyList<IReadOnlyList<DiscoveredTest>> Shards,
    IReadOnlyList<DiscoveredTest> SerialTests,
    IReadOnlyDictionary<string, int> SerialCollections,
    string Fingerprint)
{
    public int TotalTests => Shards.Sum(shard => shard.Count);
}

/// <summary>
/// Splits the tests into shards. Every agent computes the plan on its own from the same test assembly,
/// so the plan must only depend on the discovered tests, and not on their IDs: xUnit.net computes the IDs
/// from the path of the test assembly, which differs between agents. The IDs are only used to run the
/// selected tests, on the agent that discovered them.
/// <list type="bullet">
/// <item>The tests of collections that disable parallelization must never run concurrently with each other,
/// so they are all assigned to the first shard, where xUnit.net runs them one at a time.</item>
/// <item>The other tests are sorted by class, method, display name and ID, and the sorted list is cut into
/// contiguous slices of (almost) equal size. Each test case can go to any shard, but the tests of a class
/// (a Reqnroll feature) mostly stay together, so its class fixture and [BeforeFeature] hooks run on as few
/// shards as possible. Tests that cannot be told apart without their ID (same class, method and display
/// name) are kept in the same slice.</item>
/// </list>
/// </summary>
internal static class ShardPlanner
{
    public static ShardPlan Plan(IReadOnlyList<DiscoveredTest> tests, Func<string?, TestCollectionInfo> getCollection, int totalShards)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalShards, 1);

        var ordered = tests
            .DistinctBy(test => test.Id, StringComparer.Ordinal)
            .OrderBy(test => test.ClassName, StringComparer.Ordinal)
            .ThenBy(test => test.MethodName, StringComparer.Ordinal)
            .ThenBy(test => test.DisplayName, StringComparer.Ordinal)
            .ThenBy(test => test.Id, StringComparer.Ordinal)
            .ToList();

        var serialTests = new List<DiscoveredTest>();
        var parallelTests = new List<DiscoveredTest>();
        var serialCollections = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var test in ordered)
        {
            var collection = getCollection(test.ClassName);
            if (collection.DisableParallelization)
            {
                serialTests.Add(test);
                serialCollections[collection.Name] = serialCollections.GetValueOrDefault(collection.Name) + 1;
            }
            else
            {
                parallelTests.Add(test);
            }
        }

        var sizes = GetParallelTestCounts(serialTests.Count, parallelTests.Count, totalShards);
        var shards = new List<IReadOnlyList<DiscoveredTest>>(totalShards);
        var start = 0;
        var target = 0;
        for (var i = 0; i < totalShards; i++)
        {
            target += sizes[i];
            var end = Math.Max(start, target);
            while (end > 0 && end < parallelTests.Count && HaveSameName(parallelTests[end - 1], parallelTests[end]))
                end++;

            var shard = new List<DiscoveredTest>();
            if (i == 0)
                shard.AddRange(serialTests);

            shard.AddRange(parallelTests.GetRange(start, end - start));
            start = end;
            shards.Add(shard);
        }

        return new ShardPlan(shards, serialTests, serialCollections, ComputeFingerprint(ordered, totalShards));
    }

    /// <summary>
    /// Number of parallel tests per shard so that the largest shard is as small as possible, knowing that the
    /// first shard also runs the serial tests.
    /// </summary>
    internal static int[] GetParallelTestCounts(int serialCount, int parallelCount, int totalShards)
    {
        var counts = new int[totalShards];
        if (totalShards == 1)
        {
            counts[0] = parallelCount;
            return counts;
        }

        var total = serialCount + parallelCount;
        var firstShard = Math.Max(0, Math.Min(parallelCount, total / totalShards - serialCount));
        counts[0] = firstShard;

        var remaining = parallelCount - firstShard;
        var others = totalShards - 1;
        for (var i = 1; i < totalShards; i++)
            counts[i] = remaining / others + (i - 1 < remaining % others ? 1 : 0);

        return counts;
    }

    private static bool HaveSameName(DiscoveredTest x, DiscoveredTest y) =>
        string.Equals(x.ClassName, y.ClassName, StringComparison.Ordinal)
        && string.Equals(x.MethodName, y.MethodName, StringComparison.Ordinal)
        && string.Equals(x.DisplayName, y.DisplayName, StringComparison.Ordinal);

    /// <summary>
    /// Identifies the plan, so that it is easy to check that all the shards worked from the same list of tests.
    /// </summary>
    private static string ComputeFingerprint(IReadOnlyList<DiscoveredTest> orderedTests, int totalShards)
    {
        var builder = new StringBuilder();
        builder.Append(totalShards).Append('\n');
        foreach (var test in orderedTests)
            builder.Append(test.ClassName).Append('\0').Append(test.MethodName).Append('\0').Append(test.DisplayName).Append('\n');

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..12];
    }
}
