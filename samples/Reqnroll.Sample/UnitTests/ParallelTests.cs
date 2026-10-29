using Xunit;

namespace Reqnroll.Sample.UnitTests;

// Default collection (one per class): runs in parallel, every data row can go to a different shard.
public sealed class ParallelTests
{
    [Fact]
    public void Fact() => Assert.True(true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Theory(int value) => Assert.True(value > 0);
}
