using Xunit;

namespace XunitTestSharding.Tests;

public sealed class TestDiscoveryTests
{
    [Fact]
    public void ParsesTheJsonListAndIgnoresOtherOutput()
    {
        const string Output = """
            Some output written by the test project
            [{"Assembly":"/tests/Tests.dll","DisplayName":"Add(a: 1)","ID":"abc","Class":"Ns.Calculator","Method":"Add","Explicit":false,"Traits":{"Category":["Fast"]}},{"Assembly":"/tests/Tests.dll","DisplayName":"Other","ID":"def","Explicit":true,"Skip":"Not now"}]
            """ + "\u001b[0m";

        var tests = TestDiscovery.Parse(Output);

        Assert.NotNull(tests);
        Assert.Equal(
            [new DiscoveredTest("abc", "Add(a: 1)", "Ns.Calculator", "Add"), new DiscoveredTest("def", "Other", null, null)],
            tests);
    }

    [Fact]
    public void ParsesAnEmptyList()
    {
        Assert.Empty(TestDiscovery.Parse("[]\n")!);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoList()
    {
        Assert.Null(TestDiscovery.Parse("error: something went wrong\n[not json"));
    }

    [Fact]
    public void AddsPreEnumerateTheoriesOnce()
    {
        Assert.Equal(["-preEnumerateTheories", "-trait", "a=b"], TestDiscovery.WithPreEnumeratedTheories(["-trait", "a=b"]));
        Assert.Equal(["-PreEnumerateTheories"], TestDiscovery.WithPreEnumeratedTheories(["-PreEnumerateTheories"]));
    }
}
