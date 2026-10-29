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
    public void ParsesTheMicrosoftTestingPlatformList()
    {
        const string Output = """
            xUnit.net v3 Microsoft.Testing.Platform Runner v4.0.2 (64-bit .NET 10.0.13)

            Assembly: Tests
              - Display name: "Add(a: \"1\", b: \"C:\\temp\", c: \"x\ny\")"
                Test method:  Ns.Outer+Calculator.Add
                ID:           abc
                Traits:
                  "Category": ["Fast"]
              - Display name: "Class level"
                Test class:   Ns.Custom
                ID:           def
                Explicit:     true
                Skip reason:  "Not now"

              total: 0
              failed: 0
            """;

        var tests = TestDiscovery.ParseMtpList(Output.ReplaceLineEndings("\r\n"));

        Assert.NotNull(tests);
        Assert.Equal(
            [
                new DiscoveredTest("abc", "Add(a: \"1\", b: \"C:\\temp\", c: \"x\ny\")", "Ns.Outer+Calculator", "Add"),
                new DiscoveredTest("def", "Class level", "Ns.Custom", null),
            ],
            tests);
    }

    [Fact]
    public void ParsesAnEmptyMicrosoftTestingPlatformList()
    {
        Assert.Empty(TestDiscovery.ParseMtpList("Assembly: Tests\n\n  total: 0\n")!);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoMicrosoftTestingPlatformList()
    {
        Assert.Null(TestDiscovery.ParseMtpList("Unknown option '--xunit-list'\n"));
    }

    [Fact]
    public void AddsPreEnumerateTheoriesOnce()
    {
        Assert.Equal(["-preEnumerateTheories", "-trait", "a=b"], TestDiscovery.WithPreEnumeratedTheories(["-trait", "a=b"]));
        Assert.Equal(["-PreEnumerateTheories"], TestDiscovery.WithPreEnumeratedTheories(["-PreEnumerateTheories"]));
    }
}
