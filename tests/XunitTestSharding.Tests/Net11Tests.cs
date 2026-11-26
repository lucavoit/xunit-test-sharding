using System.Reflection;
using Xunit;

namespace XunitTestSharding.Tests;

/// <summary>
/// Test assemblies that target a newer .NET than xunit-shard, here .NET 11 with C# unions: their attributes reference
/// types that do not exist in the .NET 10 framework assemblies xunit-shard runs on.
/// </summary>
public sealed class Net11Tests
{
    private static readonly string s_fixturePath = typeof(Net11Tests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "Net11FixtureAssemblyPath")
        .Value!;

    [Fact]
    public void ReadsTheCollectionsOfAnAssemblyThatUsesUnions()
    {
        var inspector = CollectionInspector.Inspect(s_fixturePath, ["Net11Fixture.SerialTests", "Net11Fixture.UnionTests", "Net11Fixture.ParallelTests"]);

        Assert.True(inspector.GetCollection("Net11Fixture.SerialTests").DisableParallelization);
        Assert.True(inspector.GetCollection("Net11Fixture.UnionTests").DisableParallelization);
        Assert.False(inspector.GetCollection("Net11Fixture.ParallelTests").DisableParallelization);
        Assert.Empty(inspector.Warnings);
    }
}
