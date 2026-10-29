using XunitTestSharding;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var options = ShardOptions.Parse(args, Environment.GetEnvironmentVariable);
    return await ShardRunner.RunAsync(options, Console.Out, cancellation.Token);
}
catch (HelpRequestedException)
{
    Console.WriteLine(ShardOptions.Usage);
    return 0;
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ShardOptions.Usage);
    return 2;
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or ArgumentException or BadImageFormatException)
{
    Console.Error.WriteLine($"xunit-shard: {ex.Message}");
    return 1;
}
