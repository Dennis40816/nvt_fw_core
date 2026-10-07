# Releasing Core

Core ships as two versioned NuGet packages: `Nvt.Core` and `Nvt.Core.Avalonia`.
Both packages use the independent Core SemVer in `Directory.Build.props`, starting at `0.1.0`.
Never replace a version with different package content.

Run the pack script's offline tests with PowerShell 7:

```powershell
pwsh -File ./scripts/tests/test-pack.ps1
```

1. Set a new version in `Directory.Build.props`. In [CHANGELOG.md](../../CHANGELOG.md), move the "Unreleased" entries under the new version. Merge the reviewed source into `main`.
2. Check out that commit with a clean working tree.
3. Fetch the version tags and run the pack script with PowerShell 7:

   ```powershell
   git fetch origin --tags
   pwsh -File ./scripts/pack.ps1
   ```

The script runs locked restore, Release build, tests, and pack without another build or restore.
It writes these files into `artifacts/packages/`:

- `Nvt.Core.<version>.nupkg`
- `Nvt.Core.Avalonia.<version>.nupkg`
- `SHA256SUMS`, with each package's SHA-256 checksum
- `SOURCE.md`, with the version, full source commit, and repository URL

Each package contains `LICENSE`, `README.md`, and repository commit metadata.
The script rejects an existing `core-v<version>` tag during local packing.
Fetch tags before packing, so this check includes remote version tags.
The script also requires an empty output directory to prevent package replacement.
It rejects uncommitted changes so the source commit identifies the package content.
Preserve previous packages outside that directory.

Create and push `core-v<version>` at the reviewed source commit:

```powershell
git tag -a core-v0.1.0 -m "Core 0.1.0"
git push origin core-v0.1.0
```

Replace `0.1.0` with the version in `Directory.Build.props`.
The Core workflow checks that the tag matches that version and points to the checked-out commit.
Only this tag-triggered run may pack an existing tag, before its first GitHub Release.
The workflow rejects any existing Release for that tag, including a draft.
It runs the pack script and attaches both packages, `SHA256SUMS`, and `SOURCE.md` to the new Release.
After the workflow creates the Release, copy that version's CHANGELOG notes into the Release description through the GitHub App. Do not change the assets or the tag.
Use these Release assets as the canonical package files for tool adoption.
Keep any local pack output as a candidate until the Release exists.
Do not substitute a local rebuild for a Release asset.
Packing twice gives different `.nupkg` bytes because package archives record timestamps, so the Release asset and its `SHA256SUMS` entry are a version's only valid copy.
If release creation fails after a draft exists, preserve its assets and resolve the failure without rebuilding that version.

Every tool downloads Core packages into `artifacts/core-packages/` at the repository root before restore.
The tool ignores that folder in git and commits only `core-packages.json` as its package record.
The manifest pins each package's Release tag, asset name, and SHA-256 from that Release's `SHA256SUMS`.
Core Releases are immutable.
The public Core repository needs no download token.

A tool takes only the Core packages it references.
Add `Nvt.Core.Avalonia` when the tool adopts shared UI.
Both Core packages always use the same version.
A font package with its own version and tag uses the same manifest with its own `release` value.

Follow the [Core package download guide](../../tools/core-packages/README.md) to add or upgrade Core in a designated tool:

1. Copy the fetch script unchanged into the tool and record its Core commit in the tool's pull request.
2. Commit the manifest with the Release's asset names and SHA-256 values, then ignore the download folder.
3. Configure NuGet source mapping so `Nvt.Core` and `Nvt.Core.*` use only that local folder.
4. Run the script before every restore in build scripts, verify scripts, and CI.
   Add a download step when a workflow calls `dotnet restore` directly.
   The owner pushes workflow changes.
5. Pin each package reference to the exact version and update the tool's lock files.
   Lock files still pin each package's version and content hash.
6. Run the tool's locked restore, existing behavior tests, and release smoke tests before shipping.

The script verifies cached files and checks each download's SHA-256 before it replaces a package file.
It fails on a checksum mismatch and never falls back to another source.
For a sandbox without network, download on the host first, then run the script with `--offline` inside the sandbox.

To upgrade, use the new Release's asset names and `SHA256SUMS` values in the manifest, then update the lock files.
To roll back, put the earlier Release's values back in the manifest and restore its package references and lock files.

Deliver Core only under its proprietary `LICENSE`, including inside designated tools' releases.
Each tool release takes `LICENSE` from a verified downloaded package and ships it as `licenses/Nvt.Core/LICENSE`.
It covers both Core packages.
Each tool's own license does not relicense Core.
