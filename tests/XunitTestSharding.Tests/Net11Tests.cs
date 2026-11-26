using System.Reflection;
using Xunit;

namespace XunitTestSharding.Tests;

/// <summary>
/// Test assemblies that target a newer .NET than xunit-shard, here .NET 11 with C# unions: their attributes reference
/// types that do not exist in .NET 10, the .NET xunit-shard runs on. The fixture declares Microsoft.NETCore.App 11.0.0
/// in its runtimeconfig.json, like a net11.0 test project.
/// </summary>
public sealed class Net11Tests : IClassFixture<Net11Tests.Net11Installation>
{
    private static readonly string[] s_classes = ["Net11Fixture.SerialTests", "Net11Fixture.UnionTests", "Net11Fixture.ParallelTests"];

    private readonly Net11Installation _installation;

    public Net11Tests(Net11Installation installation) =>
        _installation = installation;

    [Fact]
    public void ReadsTheAssemblyWithTheInstalledNet11Framework()
    {
        var inspector = CollectionInspector.Inspect(Net11Installation.FixturePath, s_classes, [_installation.Root]);

        AssertCollections(inspector);
        Assert.Empty(inspector.Warnings);
        Assert.Equal(0, inspector.SkippedAttributes);

        // The host would run the fixture on 11.0.2: the latest patch of the requested 11.0
        var framework = Assert.Single(inspector.Frameworks);
        Assert.Equal(("Microsoft.NETCore.App", "11.0.0", "11.0.2"), (framework.Name, framework.RequestedVersion, framework.Version));
        Assert.Equal(Path.Combine(_installation.Root, "shared", "Microsoft.NETCore.App", "11.0.2"), framework.Path);
    }

    [Fact]
    public void ReadsTheAssemblyWhenNet11IsNotInstalled()
    {
        var inspector = CollectionInspector.Inspect(Net11Installation.FixturePath, s_classes, dotnetRoots: []);

        // The framework assemblies of .NET 10 do not have UnionAttribute: it is skipped
        AssertCollections(inspector);
        Assert.Empty(inspector.Frameworks);
        Assert.True(inspector.SkippedAttributes > 0);
        Assert.Contains(inspector.Warnings, warning => warning.Contains("Microsoft.NETCore.App 11.0.0, which is not installed", StringComparison.Ordinal));
    }

    private static void AssertCollections(CollectionInspector inspector)
    {
        Assert.True(inspector.GetCollection("Net11Fixture.SerialTests").DisableParallelization);
        Assert.True(inspector.GetCollection("Net11Fixture.UnionTests").DisableParallelization);
        Assert.False(inspector.GetCollection("Net11Fixture.ParallelTests").DisableParallelization);
    }

    /// <summary>
    /// A .NET installation with Microsoft.NETCore.App 11.0.0 and 11.0.2, made of the .NET 11 reference assemblies
    /// (enough to read metadata), and an older 10.0.0 that must not be used.
    /// </summary>
    public sealed class Net11Installation : IDisposable
    {
        public static string FixturePath { get; } = GetMetadata("Net11FixtureAssemblyPath");

        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("xunit-shard-dotnet-");

        public Net11Installation()
        {
            var referenceAssemblies = GetMetadata("NetCoreAppRef11Directory");
            foreach (var version in new[] { "10.0.0", "11.0.0", "11.0.2" })
            {
                var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, "shared", "Microsoft.NETCore.App", version));
                foreach (var file in Directory.EnumerateFiles(referenceAssemblies, "*.dll"))
                    File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
            }
        }

        public string Root => _root.FullName;

        public void Dispose() =>
            _root.Delete(recursive: true);

        private static string GetMetadata(string key) =>
            typeof(Net11Tests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
    }
}
