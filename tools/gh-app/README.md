# Shared GitHub App write module

This PowerShell module sends repository writes through the repository's GitHub App.
It adds a shared interface without removing existing scripts.
It requires PowerShell 7.4 or later, Git, GitHub CLI, and an owner-installed token helper.
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

The local base tree must equal the remote parent's tree.
Every commit, blob, and tree SHA must contain exactly 40 lowercase hexadecimal characters.
The created tree must match the local HEAD tree before any ref changes.
An existing ref advances with `force=false`.
Only an HTTP 404 permits creating a missing ref.

An open PR blocks a push unless `-AllowOpenPr` is present.
Any owner review blocks a push even with that switch.
Use a follow-up PR after the owner has reviewed the original PR.
`-ExtraParent` adds a second parent for a merge commit.

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

## Merge-GhAppApprovedPullRequest

This function processes PR numbers in the caller's order.
It skips closed or merged PRs.
It stops the batch at the first unsafe operation or failed request.

The owner's latest state-changing review must be `APPROVED` on the current head. A later COMMENTED review does not cancel it.
Submission time orders reviews, and review IDs break ties.
A pending owner review blocks merging.
A later owner comment, change request, or dismissal supersedes an earlier approval.
The function updates a behind branch through the App with `expected_head_sha`.
It checks approval again after the head changes.
It stops when approval does not carry, conflicts require manual resolution, or three updates leave the branch behind.

The function waits for required checks and a clean merge state.
It rechecks the head and owner approval before merging with the exact head SHA.
Failed or cancelled checks stop the batch.
The default timeout is 1,200 seconds, and the default poll interval is 15 seconds.
Use `-TimeoutSeconds` and `-PollSeconds` to change those limits.

After confirming the merge, it deletes the head branch when its ref still matches and `main` contains that head.
It keeps changed, absent, base, and uncontained branches.
The caller supplies an existing directory for `-LogPath`.
The function appends UTC timestamps, head SHAs, merge SHAs, updates, and branch outcomes.

```powershell
Merge-GhAppApprovedPullRequest -Numbers 7,8 -LogPath .\merge.log
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

- The module reads the config file and caller-supplied body files.
- The module never reads private keys or DPAPI contents.
- Only the owner-installed helper obtains installation tokens.
- Each write receives a fresh token in memory.
- Only one `gh` child receives that token through `GH_TOKEN`.
- The parent environment never receives the App token.
- Request bodies travel through stdin, without temporary payload files.
- Captured output replaces an accidentally echoed token with `[redacted]`.
- Captured error text never reaches callers or logs.
- Process and API failures report only `HTTP <status> <API path>`.
- An unavailable HTTP status appears as `unknown`.
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
- A merge needs the owner's latest state-changing review to be APPROVED on the current head. A later COMMENTED review does not cancel the approval, as on GitHub.
- The Core merge script accepted any approval on the head. The module also stops when a later CHANGES_REQUESTED or DISMISSED review follows it.
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
6. Replace approved merges with `Merge-GhAppApprovedPullRequest`.
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
The runner removes its temporary directory after execution.

Exit code `0` means all executed tests passed.
Exit code `1` means a test or test block failed.
Exit code `2` means the runner could not execute a valid supported suite.

Endpoint behavior follows the [GitHub pull request API](https://docs.github.com/en/rest/pulls/pulls).
Required check polling uses the [GitHub CLI check command](https://cli.github.com/manual/gh_pr_checks).
