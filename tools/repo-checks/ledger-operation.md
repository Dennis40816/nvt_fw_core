# Operating the debt ledger

The debt ledger goes down. Only Adopt adds to an existing ledger, and only in a synchronization-only
change (see [When a pin synchronization reveals debt](#when-a-pin-synchronization-reveals-debt)).
This page tells you how to change code that carries debt without raising a failure that you did not expect.
The rules of the checker are in [repo-health.md](repo-health.md). This page is the practical side of them.

The two ledgers are `eng/code-health/baseline.json` and `eng/code-health/test-debt.json`.
`removeBy` is optional on a finding. Core's ledgers carry none.

Terms on this page:

- **Fingerprint**: the identity that the ledger stores for one kind of finding (see the next section).
- **Pin**: the Core commit that a repository takes its checker bundle from. It is in `eng/core-health.lock.json`.
- **Pin synchronization**: a pull request that moves the pin and copies the bundle files of that commit.
- **Adopt**: `-Mode Adopt`, the one mode that adds debt to an existing ledger.
- **`asyncVoid`**: an async void method or local function, or an async lambda converted to a void delegate.

## What the ledger remembers

The ledger does not remember a file or a line. It remembers a fingerprint of each finding:

- the rule,
- the project,
- the member (the qualified type and the signature of the member, or the nested or local member),
- the symbol,
- a hash of tokens (`syntaxHash`), and
- a count.

The hash covers the nearest statement or member for analyzer and format findings. For the syntax metrics
such as `asyncVoid`, it covers the tokens of the recognized node, for example the lambda or the local
function.

Path and line are for display only. A file rename keeps its allowance. A change to the rule, the
project, the member, the symbol or the hash makes the checker see one finding gone and one finding new.
A new finding is new debt, and a count that grew is new debt too. Both fail.

The syntax hash excludes trivia, so a change of white space or of a comment does not change it.
A white-space edit can still add or remove a format finding (see "Checkout line endings" in
[repo-health.md](repo-health.md)), so run Verify after it.

The size rules work differently. For a file, a method, a type with partial files or a view, the ledger
stores a ceiling per entity, which is the measured excess above the limit. A new line in an oversized
file or method, even a comment or a blank line, grows the excess. It fails with
`New/growing excess or raised ceiling is forbidden` and no fingerprint changes. Split a large file or
method before you add to it.

## Refactors that change a fingerprint

A refactor that has no effect on behavior can still change a fingerprint. No mode accepts the result.
Verify fails on the new fingerprint, LowerBaseline never adds, and Adopt takes only a synchronization-only
change. In code that carries debt, do not do these unless the change also removes the finding:

- **Rename.** A type, a method, a field or a local function. The member identity changes, so the old
  fingerprint disappears and a new one appears.
- **Change a signature.** The parameter list is part of the member identity of a method.
- **Move.** A member to another type, or a type to another project. Moving a whole file to another path
  keeps its fingerprints, because the path is for display only.
- **Turn an async lambda into a local function.** `asyncVoid` counts both forms. A local function has its
  own name in the member identity, so an `async void` local function gives a new fingerprint. A local
  function that returns `Task` is not `asyncVoid`, so the finding goes away and the ledger goes down.
- **Edit the code that carries the debt.** The hash covers the tokens of the statement, member or
  recognized node (see above). Any token change in it, such as a renamed variable or a new argument,
  is a new fingerprint.

Path and line do not enter a fingerprint, so reordering members is safe. The one exception is two members
that share one identity, for example duplicates in partial files. The checker matches them by location
and an identity that stays ambiguous fails closed. Keep such members in the same file and in the same
relative order. If Verify reports an ambiguous identity, restore the original layout.

Lowering debt is allowed. If the change removes the finding, the ledger goes down. If the change only
reshapes the finding, the old fingerprint is removed and a new one is added, and the new one is new
debt. Revert the reshape, or fix the finding in the same change.

## Order of work

1. Fetch the base: `git fetch origin main`. Verify with `-BaseRef origin/main` needs that ref.
2. Make the code change. Keep each pull request to one kind of change.
3. Run Verify on your machine before you push. On Core it can take 25 minutes or more. CI runs
   the same check, and a failure there costs a wait for another CI run.
4. If Verify reports `run LowerBaseline`, the measured value is below the ledger value. Run
   LowerBaseline **last**, after the final code edit. Any later edit can change a fingerprint again.
5. Run Verify once more. Commit the code and the ledger together.

LowerBaseline lowers `eng/code-health/baseline.json` and the generated block of
`eng/code-health/projects.props`. It also lowers `eng/code-health/test-debt.json` when the findings of
that ledger shrank. Commit every file that it changed. The policy-change rule of [repo-health.md](repo-health.md#known-limitations) covers `baseline.json`
and `projects.props`, so say in the pull request that the ledger went down.

```powershell
./tools/repo-checks/repo-health.ps1 -Mode Verify -Repo core -Root . -Solution Nvt.Core.sln -BaseRef origin/main
# Run the next command only when Verify reports "run LowerBaseline".
./tools/repo-checks/repo-health.ps1 -Mode LowerBaseline -Repo core -Root . -Solution Nvt.Core.sln -BaseRef origin/main
```

Notes for a local run:

- Use a full clone with the locked restore done. The checker never restores.
- Use the checkout style that enrolled the ledger. The format ledger depends on line endings
  (see "Checkout line endings" in [repo-health.md](repo-health.md)).

## When a pin synchronization reveals debt

A newer checker can measure more than the old one. The excess has no ledger entry, and nothing in
Verify or LowerBaseline can add one. Adopt adds it, under these conditions:

- The change is synchronization-only. Only these paths may differ from the merge base: the lock, the
  two ledgers, the generated warning-ID block of `eng/code-health/projects.props`, the bundle files by
  exact name (`Directory.Build.props`, `.editorconfig`, `schema.json`, `bundle-manifest.json`,
  `doc-sync.core.json`) under `eng/core-health` and `tools/repo-checks`, any `.ps1`, `.py` or `.txt` file
  there, `tests/repo-checks/*.py`, and `*.md` files. Any other changed path refuses Adopt.
- Adopt runs in a pull request that holds no code change. Run it before the code changes.
- Adopt needs `-BaseRef`, and `-Root` must be the repository root.
- If the pull request also moves the pin, pass `-CoreRoot <approved Core checkout>`.
- Adopt only adds. It never raises a value that the ledger already has.
- Adopt cannot register an analyzer warning ID that has no ledger entry, a format finding or a finding of
  the evaluated project (member `MSBuild`). It runs after the build and format stages and writes nothing
  while another failure remains.
- After Adopt, run Verify.
- The owner reviews the added entries at the Files changed tab of `eng/code-health/baseline.json`.

A change of the bundle `.editorconfig` also changes the managed block of the root `.editorconfig`, and
that file is not a synchronization path. That pin synchronization cannot use Adopt. A root
`Directory.Build.props` is not one either. The full list of conditions and limits is in the Adopt
section of [repo-health.md](repo-health.md#adopt-debt-that-a-rule-change-reveals).

## Common failures

| What you see | Meaning | What to do |
|---|---|---|
| `New fingerprint; deleting another finding cannot offset it`, `New/growing excess or raised ceiling is forbidden`, `New test duplication`, `New format ...` | A fingerprint appeared or its count grew. | Undo the change that altered the fingerprint (a rename, a move or an edit of the statement). If the new debt is real, fix it instead of enrolling it. |
| `Fixes must lower/delete baseline debt in this change; run LowerBaseline.` (for the test ledger: `... test-debt ...`) | The measured value is below the ledger value. The code lowered debt and the ledger did not follow. | Run LowerBaseline last. Never edit a ceiling by hand. A ceiling that you raise by hand fails with the message of the first row or with this one, depending on whether it equals the measurement. |
| `The test-debt ledger may only shrink after its first enrollment.` | `test-debt.json` has an entry that is higher than in the base ref. | Restore the ledger of the base ref. Lower it only with LowerBaseline. |
| `The removeBy date in <ledger> has passed. Fix the finding, or move the date in a dedicated pull request with an owner decision.` (the output line starts with `HC_STATE`) | A finding that has a `removeBy` date is past due. | Fix the finding first. A date change is an owner decision in a pull request of its own. |
| `Adopt refused: this change touches N path(s) that are not synchronization files; ...` | The change has a path that is not a synchronization path. | Move that path to another pull request. Run Adopt alone. |
| Exit code 2 | Tool or input error, such as a parse error, an unavailable stage or a bad ledger. | Read the first error. It is not debt. |

## What not to do

- Do not edit a ceiling or a count upward. Verify rejects it.
- Do not add or move a `removeBy` date inside a code change.
- Do not hide a bundle file change in a code change. It is a policy change (see the policy-change rule
  in [repo-health.md](repo-health.md)), so the pull request says so.
- Do not skip the local Verify run because CI will run it.
