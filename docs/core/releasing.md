# Releasing Core

Core has two independent release lines.

| Release line | Version source | Tag | Pack command | Packages |
| --- | --- | --- | --- | --- |
| Core | `Directory.Build.props` | `core-v<version>` | `./scripts/pack.ps1 -Package Core` | `Nvt.Core`, `Nvt.Core.Avalonia` |
| Fonts | `src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj` | `core-fonts-v<version>` | `./scripts/pack.ps1 -Package Fonts` | `Nvt.Core.Fonts` |

The Core packages share their Core SemVer. Fonts starts at `0.1.0` and advances independently.
Omitting `-Package` selects Core.
Never replace a version with different package content.

Run the pack script's offline tests with PowerShell 7:

```powershell
pwsh -File ./scripts/tests/test-pack.ps1
```

1. Set a new Core version in `Directory.Build.props`. In [CHANGELOG.md](../../CHANGELOG.md), move the "Unreleased" entries under the new version. Merge the reviewed source into `main`.
2. Check out that commit with a clean working tree.
3. Fetch the version tags and run the pack script with PowerShell 7:

   ```powershell
   git fetch origin --tags
   pwsh -File ./scripts/pack.ps1 -Package Core
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

## Release Fonts

1. Set the Fonts version in `src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj` and record its changes in `CHANGELOG.md`.
2. Merge the reviewed source into `main`, check out that commit, and fetch tags with `git fetch origin --tags`.
3. Preserve earlier output elsewhere and run `pwsh -File ./scripts/pack.ps1 -Package Fonts` from a clean tree.
4. Create `core-fonts-v<version>` at that reviewed commit, then push that tag.
5. Use the workflow's immutable Release assets for adoption. Preserve any existing draft and its original assets if publication fails.

For the initial Fonts release, the tag is `core-fonts-v0.1.0`.

```powershell
git tag -a core-fonts-v0.1.0 -m "Core Fonts 0.1.0"
git push origin core-fonts-v0.1.0
```

The `Core / release fonts` job verifies the project version and creates `Core Fonts <tag>` with `--verify-tag`.
It attaches exactly these assets:

- `Nvt.Core.Fonts.0.1.0.nupkg`
- `SHA256SUMS`, with one checksum line
- `SOURCE.md`, with version, source commit, repository URL, and `Package: Nvt.Core.Fonts`

The package contains its assembly with five embedded font files, XML documentation, `LICENSE`, `README.md`, and the font `licenses/` folder.
The license folder includes the Material Symbols modification `NOTICE`.
Its only direct dependencies are `Avalonia` and `Avalonia.Fonts.Inter`.
Inter remains in its dependency package.

Fonts packing runs the same locked restore, Release build, and solution tests as Core.
It requires a clean tree and empty `artifacts/packages/`.
Local packing rejects an existing Fonts version tag.
A tag push must match the selected release line, project version, HEAD, and `GITHUB_SHA`.
Only the first tag-triggered release may pack an existing tag. Existing Releases, including drafts, block repacking.
The Fonts tag runs neither the Core release job nor the verify job.
Core tags run neither the Fonts release job nor the verify job.

Check a locally built candidate's contents with the optional pack test argument:

```powershell
pwsh -File ./scripts/tests/test-pack.ps1 -FontsPackagePath ./artifacts/packages/Nvt.Core.Fonts.0.1.0.nupkg
```

The check verifies the embedded font bytes, license files, readme, repository metadata, and direct dependency list.

## Pin packages in a tool

Every tool downloads Core packages into `artifacts/core-packages/` at the repository root before restore.
The tool ignores that folder in git and commits only `core-packages.json` as its package record.
The manifest pins each package's Release tag, asset name, and SHA-256 from that Release's `SHA256SUMS`.
Core Releases are immutable.
The public Core repository needs no download token.

A tool takes only the Core packages it references.
Add `Nvt.Core.Avalonia` when the tool adopts shared UI.
Both Core packages always use the same version.
Add `Nvt.Core.Fonts` when the tool adopts font roles, the Chinese fallback, or Material Symbols.
Pin Core packages to `core-v<Core version>` and Fonts to `core-fonts-v<Fonts version>` in the same manifest.
For example, Core `0.5.0` and Fonts `0.1.0` use different Release tags and exact package versions.
Each entry has its own asset name and SHA-256 from that Release's `SHA256SUMS`.
The [example manifest](../../tools/core-packages/core-packages.example.json) includes all three packages.
Replace its placeholder Fonts checksum with the published checksum before adoption.

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
It covers Core package code, including Fonts.
Tools using Fonts also ship the font `licenses/` folder and preserve Inter dependency licenses and notices.
See [Fonts license duties](modules/Fonts.md#license-duties-and-updates).
Each tool's own license does not relicense Core.
