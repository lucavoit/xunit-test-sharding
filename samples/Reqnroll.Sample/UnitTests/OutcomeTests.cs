using Xunit;

namespace Reqnroll.Sample.UnitTests;

// Used by the end-to-end tests of xunit-shard to compare the results of sharded and unsharded runs
public sealed class OutcomeTests
{
    [Fact(Skip = "Skipped tests are reported like the others")]
    public void Skipped()
    {
    }

    [Fact]
    public void FailsOnRequest() => SampleFailures.FailIfRequested();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SecondRowFailsOnRequest(int row)
    {
        if (row == 2)
            SampleFailures.FailIfRequested();
    }
}
