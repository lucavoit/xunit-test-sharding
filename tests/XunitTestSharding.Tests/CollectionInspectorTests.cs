using Xunit;

namespace XunitTestSharding.Tests
{
    public sealed class CollectionInspectorTests
    {
        private static readonly CollectionInspector s_inspector = CollectionInspector.Inspect(
            typeof(CollectionInspectorTests).Assembly.Location,
            [
                typeof(Fixtures.NamedSerial).FullName,
                typeof(Fixtures.NamedParallel).FullName,
                typeof(Fixtures.UndefinedName).FullName,
                typeof(Fixtures.TypeSerial).FullName,
                typeof(Fixtures.GenericTypeSerial).FullName,
                typeof(Fixtures.DerivedFromSerial).FullName,
                typeof(Fixtures.Outer.NestedSerial).FullName,
                typeof(Fixtures.NoCollection).FullName,
                "Does.Not.Exist",
            ]);

        [Theory]
        [InlineData(typeof(Fixtures.NamedSerial), "Serial")]
        [InlineData(typeof(Fixtures.TypeSerial), "SerialDefinition")]
        [InlineData(typeof(Fixtures.GenericTypeSerial), "SerialDefinition")]
        [InlineData(typeof(Fixtures.DerivedFromSerial), "Serial")]
        [InlineData(typeof(Fixtures.Outer.NestedSerial), "Serial")]
        public void CollectionsThatDisableParallelization(Type testClass, string collectionName)
        {
            var collection = s_inspector.GetCollection(testClass.FullName);

            Assert.True(collection.DisableParallelization);
            Assert.Equal(collectionName, collection.Name);
        }

        [Theory]
        [InlineData(typeof(Fixtures.NamedParallel))]
        [InlineData(typeof(Fixtures.UndefinedName))]
        [InlineData(typeof(Fixtures.NoCollection))]
        public void CollectionsThatRunInParallel(Type testClass)
        {
            Assert.False(s_inspector.GetCollection(testClass.FullName).DisableParallelization);
        }

        [Fact]
        public void NamesAreCaseInsensitive()
        {
            Assert.True(s_inspector.GetCollection(typeof(Fixtures.NamedSerial).FullName).DisableParallelization);
        }

        [Fact]
        public void UnknownClassesRunInParallel()
        {
            Assert.False(s_inspector.GetCollection("Does.Not.Exist").DisableParallelization);
            Assert.False(s_inspector.GetCollection(null).DisableParallelization);
            Assert.Contains(s_inspector.Warnings, warning => warning.Contains("Does.Not.Exist", StringComparison.Ordinal));
        }

        [Fact]
        public void AssemblyParallelizationIsEnabled()
        {
            Assert.False(s_inspector.AssemblyParallelizationDisabled);
        }
    }
}

// Test classes inspected through their metadata only: they do not contain any test.
namespace XunitTestSharding.Tests.Fixtures
{
    [CollectionDefinition("Serial", DisableParallelization = true)]
    public sealed class SerialNamedDefinition;

    [CollectionDefinition("Parallel")]
    public sealed class ParallelNamedDefinition;

    [CollectionDefinition(DisableParallelization = true)]
    public sealed class SerialDefinition;

    [Collection("serial")]
    public class NamedSerial;

    [Collection("Parallel")]
    public sealed class NamedParallel;

    [Collection("Not defined anywhere")]
    public sealed class UndefinedName;

    [Collection(typeof(SerialDefinition))]
    public sealed class TypeSerial;

    [Collection<SerialDefinition>]
    public sealed class GenericTypeSerial;

    public sealed class DerivedFromSerial : NamedSerial;

    public static class Outer
    {
        [Collection("Serial")]
        public sealed class NestedSerial;
    }

    public sealed class NoCollection;
}
