using System.Reflection;

namespace XunitTestSharding.Tests;

/// <summary>The Reqnroll sample, built by the test project.</summary>
internal static class Sample
{
    public const int TestCount = 30;

    /// <summary>Makes some tests of the sample fail when set to 1.</summary>
    public const string FailuresVariable = "XUNIT_SHARD_SAMPLE_FAILURES";

    public static string AssemblyPath { get; } = typeof(Sample).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "SampleAssemblyPath")
        .Value!;

    public static TestRunner ToRunner(string runner) =>
        runner == "xunit" ? TestRunner.Xunit : TestRunner.MicrosoftTestingPlatform;
}
