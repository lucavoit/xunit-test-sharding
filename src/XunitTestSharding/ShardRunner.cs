namespace XunitTestSharding;

internal static class ShardRunner
{
    public static async Task<int> RunAsync(ShardOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        var assembly = new TestAssembly(options.TestAssemblyPath);
        var (plan, warnings) = await CreatePlanAsync(assembly, options, cancellationToken);

        foreach (var warning in warnings)
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

        var arguments = TestDiscovery.WithPreEnumeratedTheories(options.TestArguments).ToList();
        foreach (var test in selectedTests)
        {
            arguments.Add("-id");
            arguments.Add(test.Id);
        }

        await output.FlushAsync(cancellationToken);
        var result = await assembly.RunAsync(arguments, captureOutput: false, options.Verbose, cancellationToken);
        return result.ExitCode;
    }

    public static async Task<(ShardPlan Plan, IReadOnlyList<string> Warnings)> CreatePlanAsync(TestAssembly assembly, ShardOptions options, CancellationToken cancellationToken)
    {
        var tests = await TestDiscovery.DiscoverAsync(assembly, options.TestArguments, options.Verbose, cancellationToken);
        var collections = CollectionInspector.Inspect(assembly.ManagedAssemblyPath, tests.Select(test => test.ClassName));

        var warnings = collections.Warnings.ToList();
        if (collections.AssemblyParallelizationDisabled)
        {
            warnings.Add("the assembly disables test parallelization ([CollectionBehavior(DisableTestParallelization = true)]). " +
                "Each shard runs its tests sequentially, but the shards run concurrently on different agents.");
        }

        return (ShardPlanner.Plan(tests, collections.GetCollection, options.TotalShards), warnings);
    }

    private static void WriteSummary(TextWriter output, TestAssembly assembly, ShardOptions options, ShardPlan plan)
    {
        output.WriteLine($"xunit-shard: {assembly.Name}: {plan.TotalTests} tests in {options.TotalShards} shards (plan {plan.Fingerprint})");

        if (plan.SerialTests.Count > 0)
        {
            var collections = string.Join(", ", plan.SerialCollections.Select(collection => $"{collection.Key} ({collection.Value})"));
            output.WriteLine($"xunit-shard: {plan.SerialTests.Count} tests of collections that disable parallelization are kept on shard 1: {collections}");
        }

        output.WriteLine($"xunit-shard: tests per shard: {string.Join(", ", plan.Shards.Select((shard, index) => $"{index + 1}={shard.Count}"))}");
        output.WriteLine($"xunit-shard: {(options.DryRun ? "selected" : "running")} shard {options.ShardIndex}/{options.TotalShards}: {plan.Shards[options.ShardIndex - 1].Count} tests");
    }
}
