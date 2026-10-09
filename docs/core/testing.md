# Testing conventions

This page tells you how to write tests in Core and in the repositories that use Core. It extends the test rules T1 to T5 in [Core conventions](conventions.md#test-rules).
Each rule has a reason. Read the reason before you ask for an exception.

**Terms.** A *ratchet* is a check that counts known violations. It fails when the count goes up. It never asks you to fix old code at once. A *watchdog* is a long time limit that stops a hung test. A watchdog is not an assertion.

## Rules

1. **Test behavior. Do not test source text.**
   - Do not read a `.cs` or `.axaml` file and compare its text.
   - Exception: architecture rules. Put them in the Architecture test project. A ratchet limits how many there are.
   - Why: a refactor that renames a file or moves a member breaks such a test. The test does not find real behavior errors.

2. **Do not synchronize with real time.**
   - Do not use `Task.Delay` or `Thread.Sleep` to wait for a result.
   - Use `ManualTimeProvider`, `TaskCompletionSource`, or a signal.
   - Why: a loaded CI machine makes timed waits fail at random. Three flaky tests had this one cause.

3. **Do not assert elapsed time.**
   - Do not write "finishes in less than one second".
   - A wait must have a watchdog limit. The limit only prevents a hang.
   - Why: elapsed time depends on the machine, not on the code.

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

## Shared test support

Build these helpers once. Use them in all repositories.

| Helper | What it solves | Status | Where it lives |
|---|---|---|---|
| `ManualTimeProvider` | Fake clock, including periodic timers | Available | `Nvt.Core.TestSupport` |
| `TestWorkspace` | Temporary folder and cleanup | Available | `Nvt.Core.TestSupport` |
| `SignalWait` and watchdog | Replaces `Task.Delay` in tests | Planned | `Nvt.Core.TestSupport` |
| `ChildProcessFixture` | Child process, process tree, output limit | Planned | `Nvt.Core.TestSupport` |
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
