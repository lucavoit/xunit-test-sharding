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
        new[] { "Tests.exe", "--runner", "vstest", "--shard-index", "1", "--total-shards", "2" },
        new[] { "Tests.exe", "--shard-index", "1", "--total-shards", "2", "--", "--filter-uid", "abc" },
        new[] { "Tests.exe", "--shard-index", "1", "--total-shards", "2", "--", "--pre-enumerate-theories", "off" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidArguments(string[] args)
    {
        Assert.Throws<UsageException>(() => ShardOptions.Parse(args, s_noEnvironment));
    }

    [Theory]
    [InlineData(new[] { "Tests.exe" }, "xunit")]
    [InlineData(new[] { "Tests.exe", "--", "-junit", "out.xml" }, "xunit")]
    [InlineData(new[] { "Tests.exe", "--", "--report-junit", "--filter-trait", "a=b" }, "mtp")]
    [InlineData(new[] { "Tests.exe", "--runner", "auto", "--", "-junit", "out.xml" }, "xunit")]
    [InlineData(new[] { "Tests.exe", "--runner", "MTP" }, "mtp")]
    [InlineData(new[] { "Tests.exe", "--runner", "xunit", "--", "--odd-but-forced" }, "xunit")]
    public void SelectsTheRunner(string[] args, string expected)
    {
        var options = ShardOptions.Parse(["--shard-index", "1", "--total-shards", "1", .. args], s_noEnvironment);

        Assert.Equal(expected == "mtp" ? TestRunner.MicrosoftTestingPlatform : TestRunner.Xunit, options.Runner);
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
