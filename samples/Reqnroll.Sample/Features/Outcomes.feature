Feature: Outcomes
    Used by the end-to-end tests of xunit-shard to compare the results of sharded and unsharded runs:
    one scenario is ignored, the other one fails when the XUNIT_SHARD_SAMPLE_FAILURES environment
    variable is 1.

@ignore
Scenario: Ignored scenario
    Given the first number is 1
    And the second number is 1
    When the two numbers are added
    Then the result should be 2

Scenario: Fails when failures are requested
    Given the first number is 1
    And the second number is 1
    When the two numbers are added
    Then the scenario fails if failures are requested
