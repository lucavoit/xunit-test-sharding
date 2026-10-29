using Reqnroll;
using Xunit;

namespace Reqnroll.Sample.StepDefinitions;

[Binding]
public sealed class InventorySteps
{
    private readonly Dictionary<string, int> _stock = new(StringComparer.Ordinal);

    [Given("the stock of {string} is {int}")]
    public void GivenTheStockIs(string item, int stock) => _stock[item] = stock;

    [When("{int} {string} are sold")]
    public void WhenItemsAreSold(int sold, string item) => _stock[item] -= sold;

    [Then("the stock of {string} should be {int}")]
    public void ThenTheStockShouldBe(string item, int expected) => Assert.Equal(expected, _stock[item]);
}
