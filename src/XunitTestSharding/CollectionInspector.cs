using System.Reflection;

namespace XunitTestSharding;

/// <summary>The test collection of a test class, as far as sharding is concerned.</summary>
internal sealed record TestCollectionInfo(string Name, bool DisableParallelization)
{
    public static TestCollectionInfo Default { get; } = new("(default)", DisableParallelization: false);
}

/// <summary>
/// Reads the test collections of a test assembly from its metadata, without running any of its code.
/// '-list full/json' does not report the test collection of the tests, so this mirrors the rules of
/// xUnit.net v3:
/// <list type="bullet">
/// <item>[Collection("name")], [Collection(typeof(T))] or [Collection&lt;T&gt;] on the test class or one of its base classes</item>
/// <item>[CollectionDefinition("name", DisableParallelization = true)] on an exported type; the definition of a
/// type-based collection is a [CollectionDefinition] without a name on that type</item>
/// <item>classes without [Collection] get their own collection, which runs in parallel</item>
/// </list>
/// Reqnroll uses the same mechanism: features with a tag configured in 'addNonParallelizableMarkerForTags'
/// get [Collection("ReqnrollNonParallelizableFeatures")], defined with DisableParallelization = true.
/// </summary>
internal sealed class CollectionInspector
{
    private const string CollectionAttributeName = "Xunit.CollectionAttribute";
    private const string GenericCollectionAttributeName = "Xunit.CollectionAttribute`1";
    private const string CollectionDefinitionAttributeName = "Xunit.CollectionDefinitionAttribute";
    private const string CollectionBehaviorAttributeName = "Xunit.CollectionBehaviorAttribute";

    private readonly Dictionary<string, TestCollectionInfo> _collectionsByClass;

    private CollectionInspector(Dictionary<string, TestCollectionInfo> collectionsByClass, bool assemblyParallelizationDisabled, IReadOnlyList<string> warnings)
    {
        _collectionsByClass = collectionsByClass;
        AssemblyParallelizationDisabled = assemblyParallelizationDisabled;
        Warnings = warnings;
    }

    /// <summary>[assembly: CollectionBehavior(DisableTestParallelization = true)] is set.</summary>
    public bool AssemblyParallelizationDisabled { get; }

    public IReadOnlyList<string> Warnings { get; }

    public TestCollectionInfo GetCollection(string? className) =>
        className is not null && _collectionsByClass.TryGetValue(className, out var collection) ? collection : TestCollectionInfo.Default;

    public static CollectionInspector Inspect(string assemblyPath, IEnumerable<string?> classNames)
    {
        var warnings = new List<string>();
        using var context = new MetadataLoadContext(new PathAssemblyResolver(GetResolverPaths(assemblyPath)));
        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        var types = GetTypes(assembly, warnings);
        var typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (type.FullName is { } fullName)
                typesByName.TryAdd(fullName, type);
        }

        var definitionsByName = new Dictionary<string, TestCollectionInfo>(StringComparer.OrdinalIgnoreCase);
        var definitionsByType = new Dictionary<string, TestCollectionInfo>(StringComparer.Ordinal);
        foreach (var type in types.Where(type => type.IsVisible))
        {
            var definition = FindAttribute(type, CollectionDefinitionAttributeName, inherit: false, warnings);
            if (definition is null)
                continue;

            var name = definition.ConstructorArguments.Select(argument => argument.Value).OfType<string>().FirstOrDefault();
            var collection = new TestCollectionInfo(name ?? type.Name, GetNamedArgument(definition, "DisableParallelization"));

            // Like xUnit.net, the first definition wins when several types define the same collection
            if (name is null)
                definitionsByType.TryAdd(type.FullName!, collection);
            else
                definitionsByName.TryAdd(name, collection);
        }

        var collectionsByClass = new Dictionary<string, TestCollectionInfo>(StringComparer.Ordinal);
        foreach (var className in classNames.Distinct(StringComparer.Ordinal))
        {
            if (className is null)
                continue;

            if (!typesByName.TryGetValue(className, out var type))
            {
                warnings.Add($"The test class '{className}' was not found in '{assemblyPath}': its tests are assumed to run in parallel.");
                continue;
            }

            if (GetCollection(type, definitionsByName, definitionsByType, warnings) is { } collection)
                collectionsByClass[className] = collection;
        }

        var collectionBehavior = assembly.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType.FullName == CollectionBehaviorAttributeName);
        var assemblyParallelizationDisabled = collectionBehavior is not null && GetNamedArgument(collectionBehavior, "DisableTestParallelization");

        return new CollectionInspector(collectionsByClass, assemblyParallelizationDisabled, warnings);
    }

    private static TestCollectionInfo? GetCollection(
        Type testClass,
        Dictionary<string, TestCollectionInfo> definitionsByName,
        Dictionary<string, TestCollectionInfo> definitionsByType,
        List<string> warnings)
    {
        var attribute = FindAttribute(testClass, CollectionAttributeName, inherit: true, warnings)
            ?? FindAttribute(testClass, GenericCollectionAttributeName, inherit: true, warnings);

        if (attribute is null)
            return null;

        if (attribute.AttributeType.IsGenericType)
            return GetTypeCollection(attribute.AttributeType.GetGenericArguments()[0]);

        return attribute.ConstructorArguments.FirstOrDefault().Value switch
        {
            string name => definitionsByName.TryGetValue(name, out var definition) ? definition : new TestCollectionInfo(name, DisableParallelization: false),
            Type type => GetTypeCollection(type),
            _ => null,
        };

        TestCollectionInfo GetTypeCollection(Type type) =>
            type.FullName is not null && definitionsByType.TryGetValue(type.FullName, out var definition) ? definition : new TestCollectionInfo(type.Name, DisableParallelization: false);
    }

    private static CustomAttributeData? FindAttribute(Type type, string attributeName, bool inherit, List<string> warnings)
    {
        for (var current = type; current is not null; current = inherit ? SafeGetBaseType(current) : null)
        {
            IList<CustomAttributeData> attributes;
            try
            {
                attributes = current.GetCustomAttributesData();
            }
            catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException)
            {
                warnings.Add($"Cannot read the attributes of '{current.FullName}': {ex.Message}");
                return null;
            }

            var attribute = attributes.FirstOrDefault(attribute =>
                (attribute.AttributeType.IsGenericType ? attribute.AttributeType.GetGenericTypeDefinition() : attribute.AttributeType).FullName == attributeName);

            if (attribute is not null)
                return attribute;
        }

        return null;

        static Type? SafeGetBaseType(Type type)
        {
            try
            {
                return type.BaseType;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }

    private static bool GetNamedArgument(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.Any(argument => argument.MemberName == name && argument.TypedValue.Value is true);

    private static Type[] GetTypes(Assembly assembly, List<string> warnings)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            warnings.Add($"Some types of '{assembly.GetName().Name}' cannot be loaded: {ex.LoaderExceptions.FirstOrDefault()?.Message}");
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    /// <summary>
    /// The assemblies next to the test assembly, completed by the assemblies of the current runtime for the
    /// framework assemblies of a framework-dependent test project.
    /// </summary>
    private static List<string> GetResolverPaths(string assemblyPath)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path) => paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);

        Add(assemblyPath);
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(assemblyPath)!, "*.dll"))
            Add(path);

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(runtimeDirectory))
            runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

        foreach (var path in Directory.EnumerateFiles(runtimeDirectory, "*.dll"))
            Add(path);

        return paths.Values.ToList();
    }
}
