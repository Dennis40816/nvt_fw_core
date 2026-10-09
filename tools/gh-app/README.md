# Shared GitHub App write module

This PowerShell module sends repository writes through the repository's GitHub App.
It adds a shared interface without removing existing scripts.
It requires PowerShell 7.4 or later, Git 2.38 or later for merge proof, GitHub CLI, and an owner-installed token helper.
The offline tests require Windows and Pester 3.4.0.

## Configure one repository

The owner creates `~/.nvt/gh-app/<owner>-<repo>.json` outside every repository.
Use the following format with actual values in that private config file.
The values below are placeholders.

```json
{
  "owner": "<OWNER>",
  "repo": "<REPO>",
  "clientId": "<CLIENT_ID>",
  "installationId": "<INSTALLATION_ID>",
  "tokenHelperPath": "<ABSOLUTE_HELPER_PATH>",
  "dpapiPath": "<ABSOLUTE_DPAPI_PATH>",
  "botLogin": "<APP_SLUG>[bot]"
}
```

Both path fields contain absolute paths.
Neither path field contains file contents.
The installation ID must be a positive integer or its decimal string.
The helper must be an installed `.ps1` file.
Missing fields stop the import.
A failed import clears the active configuration.

Importing a config binds every function to that repository until another import succeeds.
Each function also accepts `-Repository OWNER/REPO`, with `-Repo` as an alias.
A different repository argument or `GH_REPO` value stops execution before `gh` runs.
The returned config object cannot change the active configuration.

```powershell
Import-Module .\tools\gh-app\NvtGhApp.psd1
Import-GhAppConfig -Owner OWNER -Repo REPO
```

The helper receives these arguments in a separate PowerShell process:

```text
-NoProfile -NonInteractive -File <HELPER_PATH> -Mode token
-Owner <OWNER> -Repo <REPO> -ClientId <CLIENT_ID>
-InstallationId <INSTALLATION_ID> -DpapiPath <DPAPI_PATH>
```

The helper must return one printable ASCII token on stdout, without whitespace, and exit with zero.
The helper restricts that token to the configured repository.
The owner reviews and installs the helper outside repositories.
This module does not install helpers or change credentials.

## Push-GhAppBranch

This function recreates the local `LocalBase..HEAD` tree difference as one remote commit.
It uses the Git data API for blobs, trees, commits, and refs.
It reads committed Git objects, including binary files, executable modes, symlinks, gitlinks, and deletions.
It leaves uncommitted changes out of the commit.

HEAD must contain `LocalBase`. A branch cut from an older base would otherwise delete every file the base gained since. The function checks this before any `gh` call, so rebase onto the remote base first.
The local base tree must equal the remote parent's tree.
Every commit, blob, and tree SHA must contain exactly 40 lowercase hexadecimal characters.
The created tree must match the local HEAD tree before any ref changes.
An existing ref advances with `force=false`.
Only an HTTP 404 permits creating a missing ref.

An open PR blocks a push unless `-AllowOpenPr` is present.
Any owner review blocks a push even with that switch.
Use a follow-up PR after the owner has reviewed the original PR.
`-ExtraParent` adds a second parent for a merge commit.

### Pushing a workflow file

GitHub needs the `workflows` permission to create a tree that changes a file under `.github/workflows/`.
The module does not request it by default. Every other push is unchanged.
Add `-IncludeWorkflowsWrite` and `-WorkflowsLogPath <FILE>` to ask for it for one push.

- The switch is off by default and is chosen for each push.
- The function refuses it before any `gh` call unless the pushed tree changes a file under `.github/workflows/`.
- Each use appends one line to the log file: UTC time, branch, file count and workflow file count. The line holds no token.
- The helper receives one more argument, `-IncludeWorkflowsWrite`, for the token requests of that push only. The stored configuration does not change and nothing is cached.
- If GitHub did not grant `workflows` to the installation, the helper says so and the function shows that fixed sentence: `Installation token lacks the requested workflows permission.` The owner accepts the new permission on the installation page.

```powershell
Push-GhAppBranch -Worktree . -LocalBase '<LOCAL_BASE_SHA>' `
    -RemoteParent '<REMOTE_PARENT_SHA>' -Branch 'build/example' `
    -MessageFile .\message.txt -IncludeWorkflowsWrite -WorkflowsLogPath .\workflows-write.log
```

```powershell
Push-GhAppBranch -Worktree . -LocalBase '<LOCAL_BASE_SHA>' `
    -RemoteParent '<REMOTE_PARENT_SHA>' -Branch 'feature/example' `
    -MessageFile .\message.txt -ExtraParent '<SECOND_PARENT_SHA>'
```

## New-GhAppPullRequest

This function opens a PR with a description from a UTF-8 body file.
It then reads the author and compares it with `botLogin`.
An incorrect author stops execution with the full PR URL.
The function leaves that PR open for inspection.
`-Base` defaults to `main`, and `-Draft` creates a draft.
Head and base names must identify branches in the configured repository.

```powershell
New-GhAppPullRequest -Head 'feature/example' -Title 'Add shared behavior' `
    -BodyFile .\pr-body.txt -Base main -Draft
```

## Set-GhAppPullRequestBody

This function replaces a PR description with the contents of a body file.

```powershell
Set-GhAppPullRequestBody -Number 7 -BodyFile .\pr-body.txt
```

## Add-GhAppComment

This function adds a file's contents as a PR or issue comment.

```powershell
Add-GhAppComment -Number 7 -BodyFile .\comment.txt
```

## Add-GhAppReviewRecord

This function posts the caller's review record through `POST /repos/{owner}/{repo}/pulls/{number}/reviews`.
It reads the PR head before posting.
A changed head stops execution.
The review uses that exact head as `commit_id`.
The event always equals `COMMENT`.
`APPROVE`, `REQUEST_CHANGES`, and other events stop before any network call.
Only the owner approves.

```powershell
Add-GhAppReviewRecord -Number 7 -ExpectedHeadSha '<EXPECTED_HEAD_SHA>' `
    -BodyFile .\review-record.txt
```

## Request-GhAppOwnerReview

Call this function when a session sends a PR to the owner for review.
It reads the current head and appends an external review request record.
The PR must be open, not a draft, in the configured repository, and authored by `botLogin`.
It makes no GitHub write and returns the recorded entry.
A new head sent for review needs a new call.

The default ledger is `~/.nvt/gh-app/review-ledger.jsonl`, beside the repository config.
Both this ledger and any optional `-LedgerPath` must be outside every repository.
Writes append one JSON object per line in UTF-8 without a BOM.
Each object contains `owner`, `repo`, `number`, `head` (a 40-character SHA), `requestedAt` (UTC ISO 8601), and `base` (the branch name).
Keep this file private and retain earlier entries.
Use the same optional `-LedgerPath` when recording requests and merging.

```powershell
Request-GhAppOwnerReview -Number 7
```

## Merge-GhAppApprovedPullRequest

This function processes PR numbers in the caller's order.
It skips closed or merged PRs.
It stops the batch at the first unsafe operation or failed request.

The 10-05 rule preserves approval after a clean merge of the base branch, once required checks pass.
Manual conflict resolution requires owner approval and a new review request.
GitHub can move a review's `commit_id` to a newer head, so that field cannot prove which head the owner reviewed.
The module trusts the head recorded when the session sent the PR to the owner.

All three approval conditions must hold:

1. The latest appended ledger entry for this owner, repository, and PR supplies requested head S and request time T.
2. The owner's latest state-changing review is `APPROVED` and was submitted strictly after T. Submission time orders reviews; review IDs break ties. `COMMENTED` reviews do not change approval. `CHANGES_REQUESTED`, `DISMISSED`, and `PENDING` block merging. The approval's `commit_id` must also be S or a proven merge after S. T comes from this machine's clock, so this binding stops an approval of an older head from counting when the clock runs behind GitHub.
3. Walking the current head H along first parents reaches S within 20 commits. Every later commit has exactly two parents, and its second parent is contained in the current PR base. The base branch name must still match the request.

Every merge after the review request is recomputed with `git merge-tree --write-tree P1 P2`.
It must report no conflicts and produce exactly the merge commit's tree.
This proof requires Git 2.38 or later; older Git stops with a clear message.
A blob comparison is not enough: a conflict resolved wholly to the base's file can match its blob and mode, while a conflict resolved wholly to the first parent can leave an empty diff.
Both resolutions still require owner approval.

`-Worktree` is mandatory and names a local clone with complete history.
The module fetches the needed commit objects from the configured repository using the owner's normal Git credentials.
It leaves local refs, `FETCH_HEAD`, the index, and working files untouched.
Recomputation writes tree objects into the clone's object database.
A non-merge commit after S, an unrelated second parent, a changed merge tree, or a longer walk stops merging.

For `BEHIND`, the App calls `PUT pulls/{n}/update-branch` with `expected_head_sha` equal to H.
It makes at most three updates per PR.
Update failure or `DIRTY` stops with “manual resolution requires owner approval”.
After an update, the module proves the new head and waits for its required checks.

The function waits for required checks and a clean merge state.
It rechecks the ledger, approval, and history after checks before merging with `sha` equal to H.
Failed or cancelled checks stop the batch.
The default timeout is 1,200 seconds, and the default poll interval is 15 seconds.
Use `-TimeoutSeconds` and `-PollSeconds` to change those limits.

Branch deletion after a merge is opt-in through `-DeleteBranchPrefix`.
Without it, every head branch is kept, including release branches.
Pass only a branch category that the owner has authorized for deletion.
Before deleting any other branch, send its name and head to the owner for confirmation.
With it, the function deletes the head branch only when all of these hold:

- the branch name starts with the prefix;
- the branch is not `main` or the PR base;
- its ref still matches the merged head;
- the PR base contains that head.

The caller supplies an existing directory for `-LogPath`.
The function appends UTC timestamps, head SHAs, merge SHAs, and branch outcomes.
Its approval record reads `#N review requested at <S>; approved <time>; merged head <H>; differences only from <base>`.
It also logs the last GraphQL timeline `PullRequestCommit` before the approval as an observation only.
That observation never allows a merge: commit times can predate pushes. An unavailable timeline is logged as unavailable.

```powershell
Merge-GhAppApprovedPullRequest -Numbers 7,8 -Worktree . -LogPath .\merge.log -DeleteBranchPrefix 'feature/'
```

## Close-GhAppPullRequest

This function adds the comment before closing the PR.
A comment failure prevents closing.
Branch deletion requires `-DeleteBranch` and a nonempty `-BranchPrefix`.
The branch must start with that prefix, stay at the checked head, and differ from the base branch and `main`.
A prefix mismatch stops before the comment or close request.
Use this option for the module's own test branch.

```powershell
Close-GhAppPullRequest -Number 7 -CommentFile .\close-comment.txt `
    -DeleteBranch -BranchPrefix 'test/gh-app/'
```

## Invoke-GhAppRead

This function uses the owner's saved GitHub CLI login without invoking the App helper.
Sign in as the owner through normal GitHub CLI setup first.
The function removes inherited token overrides from its child environment.
It permits PR views, lists, checks, issue views and lists, repository views, and repository-scoped REST GET requests.
It rejects write commands, GraphQL, extensions, aliases, browser launches, other hosts, and other repositories.
It rejects API input files and fields because those flags can select POST.
It excludes advanced search flags.
Pass command arguments as a string array.
PR and issue identifiers must be numbers.

```powershell
Invoke-GhAppRead -Arguments @('pr', 'view', '7', '--json', 'state,headRefOid')
```

## Token rules

- The module reads the config file, review ledger, and caller-supplied body files.
- The module never reads private keys or DPAPI contents.
- Only the owner-installed helper obtains installation tokens.
- Each write receives a fresh token in memory.
- Only one `gh` child receives that token through `GH_TOKEN`.
- The parent environment never receives the App token.
- Request bodies travel through stdin, without temporary payload files.
- Captured output replaces an accidentally echoed token with `[redacted]`.
- Captured error text never reaches callers or logs.
- Process and API failures report only `HTTP <status> <API path>`. A helper failure adds only the helper's exit code.
- The config file, review ledger, helper, DPAPI file and `gh` must all be outside every repository.
- An unavailable HTTP status appears as `unknown`.
- A read retries up to three attempts, after 2 and 4 seconds, when its status is unknown, 429 or 5xx. After the third failure the message ends with `(after 3 attempts)`.
- A write never retries, because GitHub may have applied it. Examples are comments, ref updates, branch updates and merges.
- A write still retries its App token request up to three attempts. That step fails before gh starts, so nothing reaches GitHub.
- Every write targets the configured repository on GitHub.com.

## Sources and intentional differences

The module replaces per-repository copies of the same behavior:

| Source | Replaced by |
| --- | --- |
| Core maintainer scripts for branch push, PR creation and approved merge (not in this repository) | `Push-GhAppBranch`, `New-GhAppPullRequest`, `Merge-GhAppApprovedPullRequest` |
| NFC `nvt_fw_combiner` at `36754c99fc81949f09015df8bf9ce0bd622d6af0`, `docs/handoff/1.1.13/g0-scripts/Invoke-NfcGh.ps1` | the private `gh` runner and `Invoke-GhAppRead` |
| NFC `tools/post_review.py` | `Add-GhAppReviewRecord` |

These differences are intentional:

- `Invoke-GhAppRead` returns UTF-8 text without trailing line breaks. The NFC runner returned raw bytes. The supported reads return JSON or text only.
- A merge uses the latest external review request and an owner approval submitted after it. Review `commit_id` and the GraphQL timeline cannot authorize a merge. A later COMMENTED review does not cancel approval.
- The Core merge script accepted any approval on the head. The module stops when the latest state-changing owner review is CHANGES_REQUESTED, DISMISSED, or PENDING.
- The 10-05 rule allows only proven clean base merges after the recorded head. The module proves first-parent history and recomputes every merge with Git 2.38 or later using `merge-tree`.
- The module updates a behind branch at most three times, rechecks required checks and approval, and deletes a merged branch only with a prefix. The Core script always deleted the merged branch.
- Sessions record review requests with `Request-GhAppOwnerReview` before relying on an owner approval.
- Review records are always COMMENT reviews pinned to the expected head.

An adopting repository compares its old script output with these functions before it retires the old copy.
It compares the API calls, request bodies, log lines and stop messages for the same synthetic pull requests.

## Switch an existing repository

Adopt the shared interface in a later repository change.
This addition leaves the existing scripts intact.

1. Have the owner install the helper and create the external repository config.
2. Import this module and the repository config in the repository's entry script.
3. Replace branch pushes with `Push-GhAppBranch`.
4. Replace PR creation and description edits with the matching PR functions.
5. Replace comments and review records with their matching functions.
6. Record heads sent to the owner with `Request-GhAppOwnerReview`; replace approved merges with `Merge-GhAppApprovedPullRequest` and supply `-Worktree`.
7. Use `Invoke-GhAppRead` for supported owner-authenticated reads.
8. Run offline tests and review the adoption before retiring old copies.

Keep repository policy, body generation, and caller-selected runtime file locations in the adopting repository.
Keep helper paths, App identifiers, and credentials out of public source files.

## Run offline tests

Run this exact command from the repository root:

```powershell
pwsh -NoProfile -NonInteractive -File .\tools\gh-app\tests\Invoke-GhAppTests.ps1
```

The runner starts a separate process after removing GitHub, Git credential, and vault environment overrides.
It requires installed Pester 3.4.0 and the Windows .NET Framework C# compiler.
It compiles a fake `gh.exe` and puts that executable first on PATH.
The fake records arguments, request bodies, and redacted environment values.
An equality flag proves that the child received the fake App token without recording the token.
The fake helper never reads credentials or calls GitHub.
Local Git object fixtures cover binary data, Unicode filenames, modes, deletions, and merge parents.
Review fixtures cover external ledger records, approval timing, repeated branch updates, clean merge recomputation, altered merge trees, conflicts resolved wholly to either parent, and the 20-commit limit.
The runner removes its temporary directory after execution.

Exit code `0` means all executed tests passed.
Exit code `1` means a test or test block failed.
Exit code `2` means the runner could not execute a valid supported suite.

Endpoint behavior follows the [GitHub pull request API](https://docs.github.com/en/rest/pulls/pulls).
Required check polling uses the [GitHub CLI check command](https://cli.github.com/manual/gh_pr_checks).
Clean merge recomputation follows [Git merge-tree](https://git-scm.com/docs/git-merge-tree).
Timeline observations use the [GitHub GraphQL pull request schema](https://docs.github.com/en/graphql/reference/pulls).
