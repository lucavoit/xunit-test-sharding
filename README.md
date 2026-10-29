# xunit-test-sharding

`xunit-shard` splits the tests of an **xUnit.net v3** test assembly, typically a **Reqnroll** project, into shards
and runs one shard, so a test suite can be spread over several Azure DevOps agents.

It is inspired by [Meziantou.ShardedTest](https://github.com/meziantou/meziantou.shardedtest), with these
differences:

- It runs the compiled test executable directly instead of `dotnet test`, with any xUnit.net option, such as `-junit`.
- Tests are selected by their **unique test case ID** (`-id`), not by display name. Display names of Reqnroll scenario
  outlines are long and full of characters that filters must escape. IDs select exactly one test case.
- Each test case is a separate unit, including every **theory data row** and every **example of a Reqnroll scenario
  outline**.
- Tests of **collections that disable parallelization** are never split. This includes Reqnroll features tagged with a
  tag listed in `addNonParallelizableMarkerForTags`.

## Usage

```
xunit-shard [options] <test-assembly> [-- <xunit arguments>]

  --shard-index <n>    1-based index of the shard to run (default: %SYSTEM_JOBPOSITIONINPHASE%)
  --total-shards <n>   total number of shards (default: %SYSTEM_TOTALJOBSINPHASE%)
  --dry-run            print the shard plan and the selected tests without running them
  --verbose            print the command lines that are executed
```

```
xunit-shard --shard-index 2 --total-shards 4 MyProduct.AcceptanceTests.exe -- -junit results.xml
```

Every argument after `--` goes to the test executable, both when listing and when running the tests. So filters such as
`-trait- "Category=manual"` apply to every shard. Reqnroll tags are xUnit traits named `Category`. The exit code is the
one of the test executable.

Each agent prints a summary. The plan fingerprint must be the same on every shard of a run:

```
xunit-shard: Reqnroll.Sample: 24 tests in 3 shards (plan 50e42e728534)
xunit-shard: 5 tests of collections that disable parallelization are kept on shard 1: ExclusiveCollection (1), ReqnrollNonParallelizableFeatures (2), Serial (2)
xunit-shard: tests per shard: 1=8, 2=8, 3=8
xunit-shard: running shard 2/3: 8 tests
```

## How it works

1. **Discovery.** It runs `<test-assembly> -list full/json -preEnumerateTheories`. Theories are not pre-enumerated by
   default in xUnit.net v3. Without this switch, a theory or a scenario outline would be a single test case.
2. **Collections.** `-list` does not report test collections, so `xunit-shard` reads them from the assembly metadata.
   It uses `MetadataLoadContext`, so no test code runs. It applies the xUnit.net rules:
   - `[Collection("name")]`, `[Collection(typeof(T))]` or `[Collection<T>]` on the class or a base class;
   - the matching `[CollectionDefinition(..., DisableParallelization = true)]`.

   For Reqnroll, `@nonparallel` (or any tag configured in `reqnroll.json`) generates
   `[Collection("ReqnrollNonParallelizableFeatures")]`. Reqnroll defines that collection with
   `DisableParallelization = true`.
3. **Plan.** Every agent computes the same plan on its own:
   - All tests of collections that disable parallelization go to **shard 1**. There xUnit.net runs them one at a time,
     as it would without sharding.
   - The other tests are sorted by class, method and display name, then cut into contiguous slices of (almost) equal
     size, so the scenarios of a feature mostly stay on the same shard. This limits how often class fixtures and
     `[BeforeFeature]` hooks run. Shard 1 gets fewer of these tests to make up for the serial ones.
   - The plan never depends on test IDs. xUnit.net computes them from the **path** of the test assembly, which differs
     between agents. Tests that can only be told apart by their ID are kept together.
4. **Run.** It runs the test executable with `-preEnumerateTheories` and one `-id` per selected test. The arguments go
   through a response file (`@@ <file>`), so there is no command-line length limit and every shard is a single run
   with a single report.

## Requirements

- The test project uses **xUnit.net v3 3.1 or later** (`-id` was added in 3.1). Use `-junit` up to 3.x and
  `-result-junit` from 4.0.
- Test discovery must be deterministic: same tests and same display names on every agent. Theory data that can't be
  serialized is not pre-enumerated. Such a theory stays a single test case, which is fine. Data that changes between
  runs (random values, timestamps) is not fine.
- `xunit-shard` targets .NET 10. Publish it self-contained (as the pipeline does) so agents don't need the runtime.

## Limitations

- Shards run concurrently on different agents. Tests of non-parallel collections never run at the same time as each
  other, but they do run at the same time as tests of other shards. If they share an external resource with other
  tests (such as a database used by every agent), give each agent its own resource.
- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` is reported as a warning. The tests are still
  sharded, and each shard runs them sequentially.
- Shards are balanced by number of tests, not by duration.

## Azure DevOps

[`pipelines/templates/xunit-shard-job.yml`](pipelines/templates/xunit-shard-job.yml) is a job template. It:

- runs N jobs with `strategy: parallel: N`;
- passes `System.JobPositionInPhase` and `System.TotalJobsInPhase` as the shard index and count;
- runs `xunit-shard` from cmd (Windows PowerShell drops the `--` separator);
- publishes every shard's JUnit file with `PublishTestResults@2`.

The steps that put the tool and the tests on the agent are passed as `prepareSteps`.

- [`pipelines/azure-pipelines.yml`](pipelines/azure-pipelines.yml) is this repository's pipeline. It builds and tests
  the tool, publishes it self-contained for `win-x64` as the `xunit-shard` artifact, then runs the Reqnroll sample in
  3 shards.
- [`pipelines/examples/tests-from-another-pipeline.yml`](pipelines/examples/tests-from-another-pipeline.yml) shows the
  usual setup. The test executable comes from another pipeline's artifact, `xunit-shard` from this repository's
  pipeline, and the template from this repository.

```yaml
jobs:
- template: pipelines/templates/xunit-shard-job.yml@sharding
  parameters:
    pool:
      name: MyWindowsPool
    totalShards: 8
    prepareSteps:
    - checkout: none
    - download: tests
      artifact: drop
    - download: xunitShard
      artifact: xunit-shard
    toolPath: $(Pipeline.Workspace)\xunitShard\xunit-shard\xunit-shard.exe
    testAssembly: $(Pipeline.Workspace)\tests\drop\MyProduct.AcceptanceTests.exe
    junitSwitch: -junit
```

## Repository layout

| Path | Content |
| --- | --- |
| `src/XunitTestSharding` | the `xunit-shard` tool |
| `tests/XunitTestSharding.Tests` | unit tests, plus end-to-end tests that shard the sample |
| `samples/Reqnroll.Sample` | Reqnroll + xUnit.net v3 project with parallel features, a `@nonparallel` feature, scenario outlines and xUnit collections |
| `pipelines` | Azure DevOps pipeline, job template and example |

## Development

```
dotnet build XunitTestSharding.slnx
dotnet test --solution XunitTestSharding.slnx
dotnet run --project src/XunitTestSharding -- --shard-index 1 --total-shards 3 --dry-run samples/Reqnroll.Sample/bin/Debug/net10.0/Reqnroll.Sample.dll
```

`dotnet test` uses Microsoft.Testing.Platform (configured in `global.json`). The sample's executable keeps using the
xUnit.net native command line when it is run directly.
