using System.Globalization;

namespace XunitTestSharding;

internal sealed record ShardOptions(
    string TestAssemblyPath,
    int ShardIndex,
    int TotalShards,
    IReadOnlyList<string> TestArguments,
    bool DryRun,
    bool Verbose)
{
    // Azure DevOps sets these for jobs using 'strategy: parallel: N'. JobPositionInPhase is 1-based.
    public const string AzureDevOpsShardIndexVariable = "SYSTEM_JOBPOSITIONINPHASE";
    public const string AzureDevOpsTotalShardsVariable = "SYSTEM_TOTALJOBSINPHASE";

    public const string Usage = """
        Usage: xunit-shard [options] <test-assembly> [-- <xunit arguments>]

        Runs one shard of the tests of an xUnit.net v3 test assembly (.exe or .dll). The tests are
        selected by their unique ID, so every test case, including each data row of a theory or
        Reqnroll scenario outline, can be assigned to a shard. Tests of collections that disable
        parallelization are always assigned to the same shard.

        Options:
          --shard-index <n>    1-based index of the shard to run
                               (default: %SYSTEM_JOBPOSITIONINPHASE%)
          --total-shards <n>   total number of shards
                               (default: %SYSTEM_TOTALJOBSINPHASE%)
          --dry-run            print the shard plan and the selected tests without running them
          --verbose            print the command lines that are executed
          --version            show the version of xunit-shard
          -h, --help           show this help

        Every argument after '--' is passed to the test assembly, both when listing and when running
        the tests. For instance: -- -result-junit results.xml -trait- "Category=Slow"
        """;

    public static ShardOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnvironmentVariable)
    {
        string? testAssemblyPath = null;
        int? shardIndex = null;
        int? totalShards = null;
        var dryRun = false;
        var verbose = false;
        var testArguments = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--":
                    testArguments.AddRange(args.Skip(i + 1));
                    i = args.Count;
                    break;

                case "--shard-index":
                    shardIndex = ParsePositiveInteger(arg, GetValue(args, ref i));
                    break;

                case "--total-shards":
                    totalShards = ParsePositiveInteger(arg, GetValue(args, ref i));
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "-h" or "--help" or "-?":
                    throw new HelpRequestedException();

                case "--version":
                    throw new VersionRequestedException();

                default:
                    if (arg.StartsWith('-'))
                        throw new UsageException($"Unknown option '{arg}'. Arguments for the test assembly must follow '--'.");

                    if (testAssemblyPath is not null)
                        throw new UsageException($"Unexpected argument '{arg}'. Only one test assembly can be specified.");

                    testAssemblyPath = arg;
                    break;
            }
        }

        if (testAssemblyPath is null)
            throw new UsageException("The test assembly is missing.");

        shardIndex ??= ParseEnvironmentVariable("--shard-index", AzureDevOpsShardIndexVariable, getEnvironmentVariable);
        totalShards ??= ParseEnvironmentVariable("--total-shards", AzureDevOpsTotalShardsVariable, getEnvironmentVariable);

        if (shardIndex > totalShards)
            throw new UsageException($"The shard index ({shardIndex}) must not be greater than the total number of shards ({totalShards}).");

        return new ShardOptions(testAssemblyPath, shardIndex.Value, totalShards.Value, testArguments, dryRun, verbose);
    }

    private static string GetValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count)
            throw new UsageException($"Missing value for '{args[index]}'.");

        return args[++index];
    }

    private static int ParsePositiveInteger(string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result
            : throw new UsageException($"The value of '{name}' must be a positive integer, but was '{value}'.");

    private static int ParseEnvironmentVariable(string optionName, string variableName, Func<string, string?> getEnvironmentVariable)
    {
        var value = getEnvironmentVariable(variableName);
        if (string.IsNullOrEmpty(value))
            throw new UsageException($"'{optionName}' is missing and the environment variable '{variableName}' is not set.");

        return ParsePositiveInteger(variableName, value);
    }
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class HelpRequestedException() : Exception("Help requested.");

internal sealed class VersionRequestedException() : Exception("Version requested.");
