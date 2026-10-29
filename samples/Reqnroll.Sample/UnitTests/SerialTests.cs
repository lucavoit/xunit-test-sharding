using Xunit;

namespace Reqnroll.Sample.UnitTests;

// Collection defined by name with parallelization disabled: all of its tests stay on one shard.
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection;

[Collection("Serial")]
public sealed class SerialTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public void UsesSharedResource(string value) => Assert.NotEmpty(value);
}

// Collection defined by type with parallelization disabled.
[CollectionDefinition(DisableParallelization = true)]
public sealed class ExclusiveCollection;

[Collection(typeof(ExclusiveCollection))]
public sealed class ExclusiveTests
{
    [Fact]
    public void UsesExclusiveResource() => Assert.True(true);
}

// Named collection that still runs in parallel with other collections: its tests can be split.
[Collection("Shared fixture")]
public sealed class SharedFixtureTests
{
    [Fact]
    public void First() => Assert.True(true);

    [Fact]
    public void Second() => Assert.True(true);
}
