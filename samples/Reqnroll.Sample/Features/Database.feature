@nonparallel
Feature: Database
    The @nonparallel tag makes Reqnroll put this feature into the
    "ReqnrollNonParallelizableFeatures" collection (DisableParallelization = true),
    so the sharder keeps all of its scenarios on a single shard.

Scenario: Insert a row
    Given the table is empty
    When a row is inserted
    Then the table should contain 1 row

Scenario: Insert two rows
    Given the table is empty
    When a row is inserted
    And a row is inserted
    Then the table should contain 2 rows
