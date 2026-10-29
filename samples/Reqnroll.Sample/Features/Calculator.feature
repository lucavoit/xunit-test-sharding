Feature: Calculator
    Runs in parallel with other features. Every scenario and every example row
    of the outline is its own xUnit test case, so they can be spread across shards.

Scenario: Add two numbers
    Given the first number is 50
    And the second number is 70
    When the two numbers are added
    Then the result should be 120

Scenario: Subtract two numbers
    Given the first number is 70
    And the second number is 50
    When the second number is subtracted from the first
    Then the result should be 20

Scenario Outline: Add many numbers
    Given the first number is <first>
    And the second number is <second>
    When the two numbers are added
    Then the result should be <result>

Examples:
    | first | second | result |
    | 1     | 2      | 3      |
    | 2     | 3      | 5      |
    | 10    | 20     | 30     |
    | -1    | 1      | 0      |
    | 0     | 0      | 0      |
    | 100   | 1      | 101    |
