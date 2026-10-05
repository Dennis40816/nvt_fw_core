# Releasing Core

Core ships as two versioned NuGet packages: `Nvt.Core` and `Nvt.Core.Avalonia`.
Both packages use the independent Core SemVer in `Directory.Build.props`, starting at `0.1.0`.
Never replace a version with different package content.

Run the pack script's offline tests with PowerShell 7:

```powershell
pwsh -File ./scripts/tests/test-pack.ps1
```

1. Set a new version in `Directory.Build.props` and merge the reviewed source into `main`.
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
Use these Release assets as the canonical package files for tool adoption.
Keep any local pack output as a candidate until the Release exists.
Do not substitute a local rebuild for a Release asset.
Packing twice gives different `.nupkg` bytes because package archives record timestamps, so the Release asset and its `SHA256SUMS` entry are a version's only valid copy.
If release creation fails after a draft exists, preserve its assets and resolve the failure without rebuilding that version.

To add Core to a designated tool's local feed:

1. Download both packages, `SHA256SUMS`, and `SOURCE.md` from the same Core Release into `<download-directory>`.
2. Compare each package's SHA-256 checksum with `SHA256SUMS`.
3. Copy the unchanged packages and source record into `<tool-repository>/<local-feed>`.
4. Add `<local-feed>` to the tool's NuGet sources and map both Core package IDs to that source.
5. Pin both package references to the exact version, update the tool's lock files, and commit the feed files.
6. Run the tool's locked restore, existing behavior tests, and release smoke tests before shipping.

Keep earlier packages for rollback and select the earlier exact version when needed.
Deliver Core only under its proprietary `LICENSE`, including inside designated tools' releases.
Each tool's own license does not relicense Core.
