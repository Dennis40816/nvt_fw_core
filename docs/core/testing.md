# Testing conventions

This page tells you how to write tests in Core and in the repositories that use Core. It extends the test rules T1 to T5 in [Core conventions](conventions.md#test-rules).
Each rule has a reason. Read the reason before you ask for an exception.

**Terms.** A *ratchet* is a check that counts known violations. It fails when the count goes up. It never asks you to fix old code at once. A *watchdog* is a long time limit that stops a hung test. A watchdog is not an assertion. A *calibration unit* is the median time of `RelativePerf.CalibrationUnit` on one machine. A *reference second* is a second on the reference machine. A *slow test* is a test that takes more than 5 reference seconds.

## Rules

1. **Test behavior. Do not test source text.**
   - Do not read a `.cs` or `.axaml` file and compare its text.
   - Exception: architecture rules. Put them in the Architecture test project. A ratchet limits how many there are.
   - Why: a refactor that renames a file or moves a member breaks such a test. The test does not find real behavior errors.

2. **Do not synchronize with real time.**
   - Do not use `Task.Delay` or `Thread.Sleep` to wait for a result.
   - Use `ManualTimeProvider`, `TaskCompletionSource`, or a signal.
   - Why: a loaded CI machine makes timed waits fail at random. Three flaky tests had this one cause.

3. **Do not assert wall-clock time. Measure performance relative to the machine.**
   - Do not write "finishes in less than one second". An absolute limit fails on a slow runner and hides a regression on a fast one.
   - A hot path needs a performance test. Examples: console scrolling and a large load.
   - Set its threshold with one of these:
     - A calibration unit. Run a fixed reference workload in the same process and take the median as one unit. Limit the work to a multiple of it.
     - A scale ratio. The time for 20,000 rows divided by the time for 10,000 rows must stay below 2.5. This catches O(n²) on any machine.
     - A machine-independent count: allocated bytes, containers created, or number of measurements. Use a count when one fits.
   - Warm up, take at least seven samples and use the median. Do not retry. Report an unstable test as an issue (rule 13).
   - Write the threshold next to the test: the median measured on the reference machine and the margin applied.
   - Use `RelativePerf` for all of this. It is the only test code that reads a clock to measure speed. A test does not start its own timer.
   - Mark the test `[Trait("Category", "Performance")]`. CI runs these tests in a separate step, and a failure there blocks merging.
   - A wait must still have a watchdog limit. The limit only prevents a hang.
   - Why: wall-clock time depends on the machine, not on the code. A ratio or a count does not.

4. **Use `TestWorkspace` for files.**
   - Do not call `Path.GetTempPath` in a test.
   - Why: `TestWorkspace` cleans up in one way. It rejects paths that escape the workspace. If cleanup fails, it names the paths that remain.

5. **Use `ManualTimeProvider` for time.**
   - Do not add a new clock class.
   - Why: one fake clock behaves the same in every test. Your own clock class copies the same code again.

6. **Share one headless Avalonia session.**
   - Each test assembly has one shared setup.
   - Write UI tests with `[AvaloniaFact]`. Do not start the headless environment in a test file.
   - Why: many private setups are slow. They also fail in different ways.

7. **Use `ChildProcessFixture` for child processes.**
   - The fixture ends the whole process tree when the test ends.
   - It limits the size of the captured output.
   - It has a watchdog.
   - Why: a process that stays alive holds the workspace folder. That caused flaky failures.

8. **Do not copy fakes.**
   - If the same fake appears in two or more files, move it to the shared test project of that repository.
   - If the fake is generic, move it to Core `TestSupport`.
   - Why: copies drift apart. A fix in one copy does not reach the others.

9. **One test checks one behavior.**
   - Name it in PascalCase with no underscores, for example `WaitAsyncWatchdogExpiresThrowsTimeout`. Core's CA1707 rule fails the build on an underscore.
   - Use a fixed order: arrange, act, assert.
   - Do not write `if` or loops in a test.
   - Why: a failing test then tells you which behavior broke.

10. **Tests do not depend on each other.**
    - Do not share static state. Do not depend on the run order.
    - When a test changes an environment variable or the current directory, use a scope object that restores the old value.
    - Why: tests run in parallel and in any order. A leaked change breaks other tests.

11. **A bug fix includes a regression test.**
    - The test must fail on the old code.
    - Why: a test that never failed does not prove the fix.

12. **A policy test or an architecture test has a negative case.**
    - The negative case shows that a violation makes the test fail.
    - A passing run alone is not evidence.
    - Why: a rule that cannot fail does not protect anything.

13. **Report a flaky test as an issue.**
    - Open an issue. Add the test name and the error message.
    - Do not hide the failure with a rerun until the cause is fixed.
    - Why: a rerun hides a real defect. An issue keeps the evidence.

14. **Confidential data stays out of public repositories.**
    - Test data holds only hash values.
    - The original files stay in a private repository.
    - Why: a public repository cannot take data back after a push.

## Test duration

These rules take effect when `tests/slow-tests-baseline.json` is merged. Until then, `tools/repo-checks/duration_report.py` only reports.

15. **Measure test duration on CI in the Release build. Convert it to reference seconds.**
    - Reference seconds = measured seconds × (reference unit ÷ this machine's unit).
    - The reference unit is stored in `tests/slow-tests-baseline.json`. It is measured once on the CI runner image.
    - A calibration test prints this machine's unit. `tools/repo-checks/duration_report.py` does the conversion.
    - A local Debug run does not count. A test that is slow only in Debug is not slow.
    - Why: a fixed limit in seconds fails on a slow machine and hides slow tests on a fast one.

16. **A slow test has `[Trait("Category", "Slow")]` and an entry in `tests/slow-tests-baseline.json`.**
    - The entry has a reason: `real-process`, `large-input`, `real-io`, or `ui-render`.
    - Not a reason: waiting for real time. Use `ManualTimeProvider` or `SignalWait`.
    - Not a reason: a host or workspace built again in each test. Use a class fixture.
    - A slow test still runs in the merge gate. It runs in its own shard so it does not slow the fast shards.

17. **The number of slow tests is a ratchet.**
    - The check fails when: a test is over the limit and is not in the baseline; a reason is not in the list; a test is over 30 reference seconds and is not approved.
    - The count can only go down. Remove a test from the baseline when it becomes fast.
    - Why: a new slow test must not enter without a decision.

18. **CI lists the 20 slowest tests in the job summary.**
    - Columns: rank, test, project, seconds, reference seconds, slow mark.
    - The data comes from a TRX file. The summary also shows the calibration unit of this run, the number of tests over 1 reference second, and the total time of the slow tests.

19. **Each shard has a watchdog. A watchdog is not an assertion.**
    - Per run: a hang timeout of 5 minutes with no dump, so a hang becomes a named failure.
    - Per job: `timeout-minutes` = 3 × the median time of the last 10 green runs of that shard, rounded up, with a minimum of 10.
    - A watchdog stop is reported as a hang. It is never counted as a slow-test failure.

20. **A test over 30 reference seconds is split, or the owner approves it in the pull request.**
    - The approval is `"approved": true` in the baseline entry.
    - Why: one long test hides its cause and defines the run time of its whole shard.

## Shared test support

Build these helpers once. Use them in all repositories.

| Helper | What it solves | Status | Where it lives |
|---|---|---|---|
| `ManualTimeProvider` | Fake clock, including periodic timers | Available | `Nvt.Core.TestSupport` |
| `TestWorkspace` | Temporary folder and cleanup | Available | `Nvt.Core.TestSupport` |
| `SignalWait` and watchdog | Replaces `Task.Delay` in tests | Available | `Nvt.Core.TestSupport` |
| `ChildProcessFixture` | Child process, process tree, output limit | Available | `Nvt.Core.TestSupport` |
| `RelativePerf` | Performance thresholds that follow the machine: calibration unit, scale ratio, allocation count | Available | `Nvt.Core.TestSupport` |
| `TaskBlock` | The one way to block on a task in a test hook that cannot await; keeps `Task.Wait`, `Result` and `GetResult` out of tests | Available | `Nvt.Core.TestSupport` |
| `TestFiles` | Plain fixture files through streams, because `File.ReadAll*` and `File.WriteAll*` are banned | Available | `Nvt.Core.TestSupport` |
| `HeadlessSessionFixture` | One Avalonia session for each test assembly | Many private copies | One thin wrapper in each app |
| `SourceTextReader` and repository root lookup | One place that reads source text | Many private copies | Architecture test project only |
| Shared fakes | Catalog, runtime probe, output writer, replay fakes | Copied in many files | Shared test project of each repository |

## Order of work

1. `Nvt.Core.TestSupport` with `ManualTimeProvider` and `TestWorkspace` is merged.
2. Before the 1.0.0 release (light work):
   - Publish this page and link it from the Core conventions document.
   - Add duplicate-test counts to the repository health ratchet. Count real-time waits, temporary paths, private clocks, source-text reads, and private headless setups.
   - Block every new violation at once. Record the existing violations as the baseline.
3. After the 1.0.0 release (batch migration):
   - Move old tests to the shared helpers in small pull requests.
   - Split the largest repository by test family.
   - Each pull request lowers the baseline.
4. Add `SignalWait` and `ChildProcessFixture` in a later Core pull request.

Why no batch migration before 1.0.0: the largest repository has more than a thousand test files. One big migration carries a high risk of new failures and uses a lot of automated-coding budget. Small pull requests after the release are safer.
