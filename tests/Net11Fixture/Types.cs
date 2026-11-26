using System.Runtime.CompilerServices;
using Xunit;

namespace Net11Fixture;

public sealed class Circle;

public sealed class Square;

// What C# emits for a union of Circle and Square on .NET 11. Compilers that know unions check its shape: a creation
// member (a constructor with one parameter) per case, and a public 'object? Value' property.
[Union]
public readonly struct Shape
{
    public Shape(Circle value) => Value = value;

    public Shape(Square value) => Value = value;

    public object? Value { get; }
}

[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialDefinition;

[Collection("Serial")]
public sealed class SerialTests;

// A union attribute on a test class itself
[Union]
[Collection("Serial")]
public sealed class UnionTests
{
    public UnionTests(Circle value) => Value = value;

    public object? Value { get; }
}

public sealed class ParallelTests;
