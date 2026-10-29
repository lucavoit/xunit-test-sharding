using Xunit;

namespace XunitTestSharding.Tests;

public sealed class ShardOptionsTests
{
    private static readonly Func<string, string?> s_noEnvironment = _ => null;

    [Fact]
    public void ParsesOptionsAndTestArguments()
    {
        var options = ShardOptions.Parse(
            ["--shard-index", "2", "--total-shards", "4", "--dry-run", "Tests.exe", "--", "-result-junit", "out.xml", "--dry-run"],
            s_noEnvironment);

        Assert.Equal("Tests.exe", options.TestAssemblyPath);
        Assert.Equal(2, options.ShardIndex);
        Assert.Equal(4, options.TotalShards);
        Assert.True(options.DryRun);
        Assert.False(options.Verbose);
        Assert.Equal(["-result-junit", "out.xml", "--dry-run"], options.TestArguments);
    }

    [Fact]
    public void ReadsTheShardFromAzureDevOpsVariables()
    {
        var environment = new Dictionary<string, string>
        {
            [ShardOptions.AzureDevOpsShardIndexVariable] = "3",
            [ShardOptions.AzureDevOpsTotalShardsVariable] = "5",
        };

        var options = ShardOptions.Parse(["Tests.exe"], environment.GetValueOrDefault);

        Assert.Equal(3, options.ShardIndex);
        Assert.Equal(5, options.TotalShards);
    }

    [Fact]
    public void OptionsTakePrecedenceOverAzureDevOpsVariables()
    {
        var environment = new Dictionary<string, string>
        {
            [ShardOptions.AzureDevOpsShardIndexVariable] = "3",
            [ShardOptions.AzureDevOpsTotalShardsVariable] = "5",
        };

        var options = ShardOptions.Parse(["Tests.exe", "--shard-index", "1", "--total-shards", "2"], environment.GetValueOrDefault);

        Assert.Equal(1, options.ShardIndex);
        Assert.Equal(2, options.TotalShards);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        new[] { "--shard-index", "1", "--total-shards", "2" },
        new[] { "Tests.exe", "--total-shards", "2" },
        new[] { "Tests.exe", "--shard-index", "3", "--total-shards", "2" },
        new[] { "Tests.exe", "--shard-index", "0", "--total-shards", "2" },
        new[] { "Tests.exe", "--shard-index", "x", "--total-shards", "2" },
        new[] { "Tests.exe", "--shard-index", "1", "--total-shards" },
        new[] { "Tests.exe", "Other.exe", "--shard-index", "1", "--total-shards", "2" },
        new[] { "Tests.exe", "-result-junit", "out.xml", "--shard-index", "1", "--total-shards", "2" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidArguments(string[] args)
    {
        Assert.Throws<UsageException>(() => ShardOptions.Parse(args, s_noEnvironment));
    }

    [Fact]
    public void Help()
    {
        Assert.Throws<HelpRequestedException>(() => ShardOptions.Parse(["--help"], s_noEnvironment));
    }

    [Fact]
    public void Version()
    {
        Assert.Throws<VersionRequestedException>(() => ShardOptions.Parse(["--version"], s_noEnvironment));
    }
}
