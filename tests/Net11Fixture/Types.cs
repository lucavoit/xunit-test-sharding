using System.Runtime.CompilerServices;
using Xunit;

namespace Net11Fixture;

// What C# emits for a union declaration on .NET 11
[Union]
public readonly struct Shape;

[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialDefinition;

[Collection("Serial")]
public sealed class SerialTests;

// A union attribute on a test class itself
[Union]
[Collection("Serial")]
public sealed class UnionTests;

public sealed class ParallelTests;
