using Xunit;

namespace Reqnroll.Sample;

/// <summary>
/// Makes some tests fail when XUNIT_SHARD_SAMPLE_FAILURES is 1, so that the end-to-end tests of xunit-shard can
/// compare failures too. The tests pass otherwise.
/// </summary>
internal static class SampleFailures
{
    public const string EnvironmentVariable = "XUNIT_SHARD_SAMPLE_FAILURES";

    public static void FailIfRequested() =>
        Assert.False(Environment.GetEnvironmentVariable(EnvironmentVariable) == "1", $"Failure requested by {EnvironmentVariable}=1");
}
