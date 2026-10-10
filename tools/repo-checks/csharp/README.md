# Shared C# health policy

Core owns this bundle for Core, NFC, NFH, and NFU. The props, EditorConfig, and
three banned-symbol lists implement the C# health policy approved on 2026-10-09.
All files use LF and UTF-8 without a BOM.

Copy the six paths in `bundle-manifest.json`'s `copyFiles` from an approved Core
commit, byte for byte, into `eng/core-health/`. Add `eng/core-health.lock.json`
recording that commit and each distributed file's SHA-256. CI must compare with
the trusted Core revision as well as the local copies. The manifest's `version`
is its format version; its entries record `path`, `sha256`, and `byteLength`.
The manifest, this README, and the generator are Core tooling, outside `copyFiles`.

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
The checker verifies Git blob identity at that revision in addition to the hashes,
and protects the base/HEAD pin. Apps provide a local approved Core checkout with
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

To update the pin, review the canonical changes and run from the repo root:

```powershell
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Distribute -CoreCommit <approved-commit>
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Check
python -B -m unittest discover -s tests/repo-checks -p test_csharp_health_bundle.py -v
```

Make a dedicated, owner-reviewed synchronization change that recopies all six
files and updates the consumer lock to the approved Core commit and hashes.
It must still pass comparison with the previous diagnostic/structural baseline.
The host first downloads the approved analyzer packages, adds the central pins,
regenerates and reviews package locks, smoke builds, and verifies locked restore.
First integrate the contract and enrollment before verifying later PRs against their base; absent base artifacts fail closed.
