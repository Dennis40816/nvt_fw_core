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

`schema.json` exposes definitions via JSON Pointers under `$defs`: `baseline`
and `seam-owner-entry` validate the approved fields. The field lists for
`policy` and `core-health.lock`, the collection wrapper for `seam-owners`, the
entity-kind values and the `syntaxHash` algorithm are not defined yet. Until
they are, those definitions reject all input.

To update the pin, review the canonical changes and run from the repo root:

```powershell
./tools/repo-checks/csharp/New-HealthBundle.ps1
./tools/repo-checks/csharp/New-HealthBundle.ps1 -Check
python -B -m unittest discover -s tests/repo-checks -p test_csharp_health_bundle.py -v
```

Make a dedicated, owner-reviewed synchronization change that recopies all six
files and updates the consumer lock to the approved Core commit and hashes.
It must still pass comparison with the previous diagnostic/structural baseline.
The host first downloads the approved analyzer packages, adds the central pins,
regenerates and reviews package locks, smoke builds, and verifies locked restore.
Core build integration and gate activation belong to H03 after baseline enrollment.
