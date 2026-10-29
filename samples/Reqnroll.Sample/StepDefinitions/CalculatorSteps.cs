using Reqnroll;
using Xunit;

namespace Reqnroll.Sample.StepDefinitions;

[Binding]
public sealed class CalculatorSteps
{
    private int _first;
    private int _second;
    private int _result;

    [Given("the first number is {int}")]
    public void GivenTheFirstNumberIs(int number) => _first = number;

    [Given("the second number is {int}")]
    public void GivenTheSecondNumberIs(int number) => _second = number;

    [When("the two numbers are added")]
    public void WhenTheTwoNumbersAreAdded() => _result = _first + _second;

    [When("the second number is subtracted from the first")]
    public void WhenTheSecondNumberIsSubtractedFromTheFirst() => _result = _first - _second;

    [Then("the result should be {int}")]
    public void ThenTheResultShouldBe(int expected) => Assert.Equal(expected, _result);
}
