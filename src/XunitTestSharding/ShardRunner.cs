namespace XunitTestSharding;

internal static class ShardRunner
{
    // '--xunit-list' (listing the tests with Microsoft.Testing.Platform) was added in xUnit.net v3 4.0
    private static readonly Version s_mtpListingVersion = new(4, 0);

    public static async Task<int> RunAsync(ShardOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var assembly = new TestAssembly(options.TestAssemblyPath);
        var discovery = await CreatePlanAsync(assembly, options, cancellationToken);
        var plan = discovery.Plan;

        foreach (var warning in discovery.Warnings)
            output.WriteLine($"xunit-shard: warning: {warning}");

        WriteSummary(output, assembly, options, plan);

        var selectedTests = plan.Shards[options.ShardIndex - 1];
        if (options.DryRun)
        {
            foreach (var test in selectedTests)
                output.WriteLine($"  {test.Id}  {test.DisplayName}");

            return 0;
        }

        if (selectedTests.Count == 0)
        {
            output.WriteLine($"xunit-shard: shard {options.ShardIndex}/{options.TotalShards} has no tests to run.");
            return 0;
        }

        var arguments = BuildRunArguments(options, selectedTests, discovery.FiltersApplied);

        await output.FlushAsync(cancellationToken);
        var result = await assembly.RunAsync(options.Runner, arguments, captureOutput: false, options.Verbose, cancellationToken);
        return result.ExitCode;
    }

    public static async Task<DiscoveryResult> CreatePlanAsync(TestAssembly assembly, ShardOptions options, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        IReadOnlyList<DiscoveredTest> tests;
        var filtersApplied = true;

        if (options.Runner is TestRunner.Xunit)
        {
            tests = await TestDiscovery.DiscoverWithXunitAsync(assembly, options.TestArguments, options.Verbose, cancellationToken);
        }
        else if (assembly.XunitVersion is null || assembly.XunitVersion >= s_mtpListingVersion)
        {
            tests = await TestDiscovery.DiscoverWithMtpAsync(assembly, options.TestArguments, options.Verbose, cancellationToken);
        }
        else
        {
            // Before 4.0, Microsoft.Testing.Platform cannot list the tests, but the xUnit.net runner of the same
            // executable can, with the same IDs. The Microsoft.Testing.Platform arguments only apply when running.
            tests = await TestDiscovery.DiscoverWithXunitAsync(assembly, [], options.Verbose, cancellationToken);
            filtersApplied = options.TestArguments.Count == 0;
            if (!filtersApplied)
            {
                warnings.Add($"xUnit.net v3 {assembly.XunitVersion} cannot list the tests with Microsoft.Testing.Platform, so the plan is made " +
                    "without the test arguments: filters are applied when running each shard, and the shards may be unbalanced. " +
                    "xUnit.net v3 4.0 and later do not have this limitation.");
            }
        }

        var collections = CollectionInspector.Inspect(assembly.ManagedAssemblyPath, tests.Select(test => test.ClassName));
        warnings.AddRange(collections.Warnings);
        if (collections.AssemblyParallelizationDisabled)
        {
            warnings.Add("the assembly disables test parallelization ([CollectionBehavior(DisableTestParallelization = true)]). " +
                "Each shard runs its tests sequentially, but the shards run concurrently on different agents.");
        }

        return new DiscoveryResult(ShardPlanner.Plan(tests, collections.GetCollection, options.TotalShards), warnings, filtersApplied);
    }

    /// <summary>
    /// The arguments that run the selected tests: the test arguments, theory pre-enumeration (so that the IDs match
    /// the listing) and the IDs of the tests.
    /// </summary>
    internal static List<string> BuildRunArguments(ShardOptions options, IReadOnlyList<DiscoveredTest> selectedTests, bool filtersApplied)
    {
        if (options.Runner is TestRunner.Xunit)
        {
            var arguments = TestDiscovery.WithPreEnumeratedTheories(options.TestArguments).ToList();
            foreach (var test in selectedTests)
            {
                arguments.Add("-id");
                arguments.Add(test.Id);
            }

            return arguments;
        }

        var mtpArguments = new List<string> { TestDiscovery.MtpPreEnumerateTheoriesOption, "on" };
        mtpArguments.AddRange(options.TestArguments);

        // When the plan was made without the filters, they can exclude every test of a shard, which is not an error
        if (!filtersApplied && !options.TestArguments.Contains("--ignore-exit-code", StringComparer.OrdinalIgnoreCase))
        {
            mtpArguments.Add("--ignore-exit-code");
            mtpArguments.Add(TestDiscovery.MtpZeroTestsExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        mtpArguments.Add("--filter-uid");
        mtpArguments.AddRange(selectedTests.Select(test => test.Id));
        return mtpArguments;
    }

    private static void WriteSummary(TextWriter output, TestAssembly assembly, ShardOptions options, ShardPlan plan)
    {
        var runner = options.Runner is TestRunner.Xunit ? "xUnit.net runner" : "Microsoft.Testing.Platform";
        output.WriteLine($"xunit-shard: {assembly.Name}: {plan.TotalTests} tests in {options.TotalShards} shards (plan {plan.Fingerprint}, {runner})");

        if (plan.SerialTests.Count > 0)
        {
            var collections = string.Join(", ", plan.SerialCollections.Select(collection => $"{collection.Key} ({collection.Value})"));
            output.WriteLine($"xunit-shard: {plan.SerialTests.Count} tests of collections that disable parallelization are kept on shard 1: {collections}");
        }

        output.WriteLine($"xunit-shard: tests per shard: {string.Join(", ", plan.Shards.Select((shard, index) => $"{index + 1}={shard.Count}"))}");
        output.WriteLine($"xunit-shard: {(options.DryRun ? "selected" : "running")} shard {options.ShardIndex}/{options.TotalShards}: {plan.Shards[options.ShardIndex - 1].Count} tests");
    }
}

internal sealed record DiscoveryResult(ShardPlan Plan, IReadOnlyList<string> Warnings, bool FiltersApplied);
