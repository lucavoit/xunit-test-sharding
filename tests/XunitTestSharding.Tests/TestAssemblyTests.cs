using Xunit;

namespace XunitTestSharding.Tests;

public sealed class TestAssemblyTests
{
    [Fact]
    public void XunitResponseFilesHaveOneArgumentPerLine()
    {
        Assert.Equal(
            ["-trait-", "Category=a b", "-id", "abc"],
            TestAssembly.GetResponseFileLines(TestRunner.Xunit, ["-trait-", "Category=a b", "-id", "abc"]));
    }

    [Fact]
    public void MicrosoftTestingPlatformResponseFilesQuoteArgumentsWithWhitespace()
    {
        Assert.Equal(
            ["--report-xunit-junit-filename", "\"shard 1.xml\"", "--filter-uid", "abc", "def"],
            TestAssembly.GetResponseFileLines(TestRunner.MicrosoftTestingPlatform, ["--report-xunit-junit-filename", "shard 1.xml", "--filter-uid", "abc", "def"]));
    }

    [Theory]
    [InlineData("xunit", "")]
    [InlineData("xunit", " leading")]
    [InlineData("xunit", "line\nbreak")]
    [InlineData("mtp", "")]
    [InlineData("mtp", "has \"quotes\"")]
    [InlineData("mtp", "line\r\nbreak")]
    public void RejectsArgumentsThatCannotBeWritten(string runner, string argument)
    {
        Assert.Throws<ArgumentException>(() =>
            TestAssembly.GetResponseFileLines(runner == "xunit" ? TestRunner.Xunit : TestRunner.MicrosoftTestingPlatform, [argument]));
    }
}
