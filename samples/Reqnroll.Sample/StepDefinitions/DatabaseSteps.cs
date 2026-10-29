using Reqnroll;
using Xunit;

namespace Reqnroll.Sample.StepDefinitions;

[Binding]
public sealed class DatabaseSteps
{
    // Stands in for a shared resource such as a database: the scenarios using it must not run concurrently.
    private static int s_rows;

    [Given("the table is empty")]
    public void GivenTheTableIsEmpty() => s_rows = 0;

    [When("a row is inserted")]
    public void WhenARowIsInserted() => s_rows++;

    [Then("the table should contain {int} row(s)")]
    public void ThenTheTableShouldContainRows(int expected) => Assert.Equal(expected, s_rows);
}
