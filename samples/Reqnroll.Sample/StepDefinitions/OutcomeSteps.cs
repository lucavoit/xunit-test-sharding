using Reqnroll;

namespace Reqnroll.Sample.StepDefinitions;

[Binding]
public sealed class OutcomeSteps
{
    [Then("the scenario fails if failures are requested")]
    public void ThenTheScenarioFailsIfFailuresAreRequested() => SampleFailures.FailIfRequested();
}
