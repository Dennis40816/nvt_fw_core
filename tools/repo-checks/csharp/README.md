# Shared C# health policy

Core owns this bundle for Core, NFC, NFH, and NFU. The props, EditorConfig, and
three banned-symbol lists implement the C# health policy approved on 2026-10-09.
All files use LF and UTF-8 without a BOM.

Copy the six paths in `bundle-manifest.json`'s `copyFiles` from an approved Core
commit, byte for byte, into `eng/core-health/`. Add `eng/core-health.lock.json`
recording that commit and the SHA-256 of each of the six files and of `repo-health.ps1` (an old application
lock may omit the checker entry). CI must compare with
the trusted Core revision as well as the local copies. The manifest's `version`
is its format version; its entries record `path`, `sha256`, and `byteLength`.
The manifest, this README, and the generator are Core tooling, outside `copyFiles`.
The manifest lists `repo-health.ps1` in `verifiedFiles`.
Its hash and byte length appear in `files`.
The generator never copies the checker.

Classify every C# project in repository-owned `eng/code-health/projects.props`,
using `domain`, `application`, `presentation`, `infrastructure`, `composition`,
`tests`, or `tooling`. Integrate in root props in this order:

```xml
<PropertyGroup>
  <RepoHealthRoot>$(MSBuildThisFileDirectory)</RepoHealthRoot>
</PropertyGroup>
<Import Project="eng/code-health/projects.props" />
<Import Project="eng/core-health/Directory.Build.props" />
```

The checker wraps the common EditorConfig in the root file's managed block;
keep markers out of the bundle. Preserve stronger rules and reject weaker
descendant overrides. Supply each layer's `BannedSymbols.txt`, the approved
per-project diagnostic baseline, and exact seam owners. Public API analysis is
enabled only for Core's published assemblies via `HealthPublicApi`.

`schema.json` defines the baseline, seam entry/wrapper and distribution lock.
The seam wrapper has schemaVersion=1 and entries; Core initially has no seam
owners. Each reviewed entry names an exact path, member and documentation symbol
with owner, reason, kind=permanent-seam and review. A reviewed file capability may
use member `*`; directories and symbol wildcards never match. Existing RS0030
findings belong in the ledger, not mechanically in the seam manifest.

The minimal lock has schemaVersion=1, coreCommit (40 lowercase hex), and files
mapping the six distributed filenames to raw-byte SHA-256 (64 lowercase hex).
Core's lock also records `repo-health.ps1`.
An old application lock may omit that entry until its pin synchronization.
The checker then skips its own hash and blob checks.
For an application the checker verifies Git blob identity at that revision in addition to
the hashes, and protects the base/HEAD pin. Core instead compares each copy, its canonical file
and the lock hash as the same bytes. Apps provide a local approved Core checkout with
-CoreRoot; no network or Core runtime dependency is introduced. A pin change is a
dedicated owner-reviewed synchronization and must preserve the previous baseline.
The root managed markers are BEGIN/END CORE HEALTH MANAGED BLOCK comments; the
interior is this .editorconfig byte for byte. The generator handles distribution.

Diagnostic syntaxHash v1 selects the nearest non-block Roslyn StatementSyntax at
the reported token, else MemberDeclarationSyntax, else compilation unit. Join
DescendantTokens().Text with one ASCII space, excluding trivia; SHA-256 of UTF-8
without BOM becomes lowercase hex. H02 syntax metrics retain their recognized-node
normalization. Locationless project diagnostics hash the trimmed invariant message.
Identity is rule/project/member/symbol/syntaxHash and count; paths survive renames.
Format uses FORMAT:<DiagnosticId> and the same syntax hash. No policy.json is needed;
the policy definition stays closed. See [checker documentation](../repo-health.md)
for the modes, tools, evaluated coverage and enrollment contract.

Enroll is the only way to create the first ledger, refuses an existing ledger and
runs every provider. Verify and LowerBaseline require a committed baseline and
Solution; LowerBaseline cannot enroll. Generated HealthBaselineWarningIds must
equal the build diagnostic ledger per project. VSTHRD100 remains error: apps must
remove async void before enrollment can build. Public API enrollment remains H07d.

To change a bundle file in Core, edit its canonical file in `tools/repo-checks/csharp/`.
To change the checker, edit `tools/repo-checks/repo-health.ps1`.
Save the checker as LF UTF-8 without a BOM.
Run the same commands from the repository root for either change.
Use the pin that the lock names now.
The pin stays in this change.

```powershell
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Distribute -CoreCommit <coreCommit of eng/core-health.lock.json>
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Check
```

The change includes the canonical file, any distributed copy, the manifest and the lock hash.
`-Distribute` updates the manifest and lock hash for `repo-health.ps1` in the same run.
It copies only the six bundle files.
Core's `Verify` compares these files with the working tree.
After merge, a pin synchronization moves `coreCommit` to the merge commit.
Run `New-HealthBundle.ps1 -Distribute -CoreCommit <merge commit>` for that step.
An application then updates its pin as below.

An application moves its pin in one pull request that changes `eng/core-health.lock.json`
(`coreCommit` and the hashes) and the copies in `eng/core-health/` (copy all six files of the new Core
commit, byte for byte). CI must run `Verify` with `-BaseRef origin/main` and with `-CoreRoot` set to a
Core checkout. The checker that runs must be the one at the new pin, so run it from a checkout of that pin
and not from a later commit that changed the checker. The checker then accepts the pin move when the new
pin is a commit of that checkout, descends from the old pin and its six files equal the copies.
The running `repo-health.ps1` must match the checker blob at the new pin.
A lock that had the `repo-health.ps1` entry in the base lock cannot drop it.
Its lock entry records the raw-byte SHA-256 of that checker.
Without `-CoreRoot` (a lock-only run) a moved pin is an error.

Core's pin step runs in the Core checkout. The command exists only there, because an application has no
`tools/repo-checks/csharp`:

```powershell
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Distribute -CoreCommit <approved-commit>
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Check
python -B -m unittest discover -s tests/repo-checks -p test_csharp_health_bundle.py -v
```

An application does not run these commands.
It copies the six bundle files of the approved commit by hand.
It runs the checker from that commit, in one of the two ways described in "Application CI wiring" below.
Only an application that keeps a local copy of the checker needs the `.gitattributes` line
`tools/repo-checks/repo-health.ps1 text eol=lf`. Core has this line.
Without it, a Windows checkout with `autocrlf` writes CRLF and `Verify` stops with a tool error.
It edits its own `eng/core-health.lock.json`:

- `coreCommit` is the approved Core commit (40 lowercase hex digits).
- `repo-health.ps1` records the hash of `tools/repo-checks/repo-health.ps1` at that commit.
- Each file entry is the SHA-256 of the raw LF bytes of the copied file, as lowercase hex. In PowerShell:
  `(Get-FileHash -Algorithm SHA256 <file>).Hash.ToLowerInvariant()`. Hash the file only when it has LF line
  endings. A CRLF checkout gives a different hash, and the checker rejects it.

For an application, make a dedicated, owner-reviewed synchronization change that recopies all six
files and updates the consumer lock to the approved Core commit and hashes.
It must still pass comparison with the previous diagnostic/structural baseline.
The host first downloads the approved analyzer packages, adds the central pins,
regenerates and reviews package locks, smoke builds, and verifies locked restore.
First integrate the contract and enrollment before verifying later PRs against their base; absent base artifacts fail closed.

## Application CI wiring

An application runs `Verify` in CI with the checker that its lock names.
The recommended way is to check out Core at `coreCommit` into `.core` and run the checker from there.
This section gives the workflow steps and the rules that three applications found by running them.

### Two ways to run the checker

| | A. Run the checker from `.core` (recommended) | B. Run a local copy |
|---|---|---|
| Local `tools/repo-checks/repo-health.ps1` | Not needed. Delete it. | Needed. Same blob as the checker at the pin. |
| Local `tools/repo-checks/schema.json` | Not needed. The checker reads `.core/tools/repo-checks/csharp/schema.json`. | Needed. Same bytes as `tools/repo-checks/csharp/schema.json` at the pin. |
| `.gitattributes` line for the checker | Not needed. | Needed (see above). |
| Pin synchronization | Lock, copies in `eng/core-health/` and ledgers only. | The same, plus the checker copy and the schema file. `Adopt` refuses the schema path, so a synchronization that needs `Adopt` cannot use way B. |

The checker reads its schema from `csharp/schema.json` next to the script.
When that file is missing, it reads `schema.json` next to the script.
An application copy has no `csharp` directory, so it uses the second file.
The lock schema at a new pin has a `repo-health.ps1` key.
An old schema file rejects a lock that has this key, with `Invalid core-health.lock document (schema)`.

### Two pull requests

1. The wiring pull request changes `.github/workflows/*.yml` and the `.gitignore`. The pin does not move.
2. The synchronization pull request changes the lock, the copies and the ledgers. The pin moves.

Keep them separate.
`Adopt` accepts only synchronization paths, and a workflow file is not one of them.
The wiring pull request runs its own workflow, so it proves the wiring before the pin moves.

### Standard steps

```yaml
      - name: Check out the application with history
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          fetch-depth: 0
          persist-credentials: false

      - name: Read the Core pin
        id: pin
        shell: pwsh
        run: |
          $lock = Get-Content -Raw -LiteralPath eng/core-health.lock.json | ConvertFrom-Json
          if ($lock.coreCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'eng/core-health.lock.json: coreCommit is not a 40-character lowercase SHA.' }
          "commit=$($lock.coreCommit)" >> $env:GITHUB_OUTPUT

      - name: Check out Core at the pin
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          repository: Dennis40816/nvt_fw_core
          ref: ${{ steps.pin.outputs.commit }}
          path: .core
          fetch-depth: 0
          persist-credentials: false

      - name: Verify repo health (pull request)
        if: github.event_name == 'pull_request'
        shell: pwsh
        env:
          BASE_REF: ${{ github.base_ref }}
        run: ./.core/tools/repo-checks/repo-health.ps1 -Mode Verify -Repo <repo> -Root . -Solution <Name>.sln -BaseRef "origin/$env:BASE_REF" -CoreRoot ./.core

      - name: Verify repo health (push)
        if: github.event_name == 'push'
        shell: pwsh
        env:
          PUSH_BEFORE: ${{ github.event.before }}
        run: |
          $p = @{ Mode = 'Verify'; Repo = '<repo>'; Root = '.'; Solution = '<Name>.sln'; CoreRoot = './.core' }
          if ($env:PUSH_BEFORE -and $env:PUSH_BEFORE -notmatch '^0+$') { $p.BaseRef = $env:PUSH_BEFORE }
          ./.core/tools/repo-checks/repo-health.ps1 @p
```

Replace `<repo>` and `<Name>.sln` with the application values.
Put the steps after the application's own restore step, in the order the application already uses.
A way B application runs its own `tools/repo-checks/repo-health.ps1` instead of the `.core` script and keeps `-CoreRoot ./.core`.

### Rules

- Both checkouts use `fetch-depth: 0`.
  The checker asks whether the new pin is a commit of the Core checkout and whether the old pin is its ancestor.
  A shallow clone reports `not a commit of the Core checkout`.
  `-BaseRef` needs the merge base, so the application checkout needs the full history too.
- Check the pin before you pass it on.
  The lock is controlled by the pull request.
  The step above accepts only 40 lowercase hex digits and passes the value as a step output, not inside `run:`.
- Pass `-CoreRoot` on pushes too.
  After a pin move, the lock on the pushed commit is the new pin and `-BaseRef` is the old head.
  When `github.event.before` is all zeros (a new branch), pass no `-BaseRef`.
  The checker then compares with the lock at `HEAD`, which is allowed.
  After a forced push, the `before` commit can be missing from the clone and the push run fails.
  Do not force-push to a branch that runs this job.
- Build the push arguments as a hashtable and splat it with `@p`.
  An array splat passes `-Mode` as a positional value.
- Add `/.core/` to the application `.gitignore`.
  An untracked `.core/**/*.csproj` is counted as an unregistered project.
- The checker uses the process working directory as the workspace, not the PowerShell location.
  CI starts in the workspace.
  Outside CI, start the process in the application root, or pass an absolute `-Root`.
- Register and verify the ledger in a Windows (CRLF) checkout, as CI does.
  The count of `FORMAT:*` findings and the member that holds each finding depend on the line endings of the files.
  NFU got 12 new `FORMAT:WHITESPACE` fingerprints in a worktree with `core.autocrlf=false` and none in a CRLF checkout.
  The checker and the base commit were the same.
  A checker older than the pin also gave different counts in the two trees.
  A mismatch that only an LF checkout shows is not a checker change and needs no `Adopt`.
- Core is a public repository, so the default token is enough for the `.core` checkout.
  A private Core needs a token with read access.
