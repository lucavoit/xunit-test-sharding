using Xunit;

namespace XunitTestSharding.Tests;

public sealed class FrameworkResolverTests
{
    [Theory]
    [InlineData("11.0.0", new[] { "10.0.13", "11.0.1", "11.0.2", "11.1.0" }, "11.0.2")]
    [InlineData("11.0.0", new[] { "11.2.0", "11.1.3", "11.1.1" }, "11.1.3")]
    [InlineData("11.0.5", new[] { "11.0.2", "11.0.7" }, "11.0.7")]
    [InlineData("11.0.0", new[] { "10.0.13" }, null)]
    [InlineData("11.0.0", new[] { "11.0.1-rc.1.123", "11.0.0" }, "11.0.0")]
    [InlineData("11.0.0-rc.2.1", new[] { "11.0.0-rc.2.1" }, "11.0.0-rc.2.1")]
    [InlineData("11.0.0-rc.2.1", new[] { "11.0.0-rc.2.1", "11.0.0" }, "11.0.0")]
    [InlineData("10.0.0", new[] { "12.0.1", "11.0.3", "11.0.1" }, "11.0.3")]
    [InlineData("11.0", new[] { "11.0.0" }, "11.0.0")]
    [InlineData("not a version", new[] { "11.0.0" }, null)]
    [InlineData("11.0.0", new[] { "not a version" }, null)]
    public void SelectsTheVersionTheHostWouldRun(string requested, string[] installed, string? expected)
    {
        Assert.Equal(expected, FrameworkResolver.SelectVersion(requested, installed));
    }

    [Fact]
    public void ReadsTheFrameworksOfTheRuntimeConfiguration()
    {
        var assembly = WriteRuntimeConfig("""
            {
              // Comments are allowed
              "runtimeOptions": {
                "tfm": "net11.0",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "11.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "11.0.0" },
                ]
              }
            }
            """);

        Assert.Equal([("Microsoft.NETCore.App", "11.0.0"), ("Microsoft.AspNetCore.App", "11.0.0")], FrameworkResolver.ReadFrameworks(assembly));
    }

    [Theory]
    [InlineData("""{ "runtimeOptions": { "tfm": "net11.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "11.0.0" } ] } }""")]
    [InlineData("""{ "runtimeOptions": { } }""")]
    [InlineData("""not json""")]
    [InlineData(null)]
    public void NoFrameworksForSelfContainedOrUnknownConfigurations(string? runtimeConfig)
    {
        Assert.Empty(FrameworkResolver.ReadFrameworks(WriteRuntimeConfig(runtimeConfig)));
    }

    [Fact]
    public void FindsTheInstalledFrameworkOfTheSample()
    {
        var (directories, warnings) = FrameworkResolver.Resolve(Sample.AssemblyPath);

        Assert.Empty(warnings);
        var framework = Assert.Single(directories);
        Assert.Equal("Microsoft.NETCore.App", framework.Name);
        Assert.StartsWith("10.", framework.Version, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(framework.Path, "System.Runtime.dll")));
    }

    /// <summary>Writes the runtimeconfig.json of a fake assembly, and returns the path of the assembly.</summary>
    private static string WriteRuntimeConfig(string? content)
    {
        var directory = Directory.CreateTempSubdirectory("xunit-shard-runtimeconfig-");
        var assembly = Path.Combine(directory.FullName, "Tests.dll");
        if (content is not null)
            File.WriteAllText(Path.Combine(directory.FullName, "Tests.runtimeconfig.json"), content);

        return assembly;
    }
}
