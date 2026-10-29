Feature: Inventory
    Another parallel feature.

Scenario Outline: Stock is reduced when an item is sold
    Given the stock of "<item>" is <stock>
    When <sold> "<item>" are sold
    Then the stock of "<item>" should be <remaining>

Examples:
    | item   | stock | sold | remaining |
    | apple  | 10    | 3    | 7         |
    | banana | 5     | 5    | 0         |
    | cherry | 100   | 1    | 99        |
    | date   | 7     | 2    | 5         |
