# Core package downloads

Tools commit a small manifest and download Core packages before every restore.
They do not commit package files.
The script uses Python 3.10 or later and only the standard library.
Core's public GitHub repository needs no download token.
Core Releases are immutable.

## Adopt the script

1. Copy `fetch_core_packages.py` unchanged to `scripts/fetch_core_packages.py` in the tool.
   Record the full Core commit that supplied the script in the tool's pull request.
2. Copy [core-packages.example.json](core-packages.example.json) to `core-packages.json` at the tool's repository root.
   Keep only the packages that the tool references.
3. Commit the script and manifest.
   Add `artifacts/core-packages/` to the tool's `.gitignore`.
4. Add the local folder to `NuGet.config` with the source mapping below.
5. Pin each package reference to its exact version and update the tool's lock files.
   The lock files pin each package's version and content hash.
6. Call the script before every restore in the tool's build scripts, verify scripts, and CI.
   When a workflow calls `dotnet restore` directly, add one download step before that command.
   The owner pushes workflow changes.
7. Run the tool's locked restore, existing behavior tests, and release smoke tests.

Use this complete `NuGet.config` at the tool's repository root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="core-packages" value="artifacts/core-packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="core-packages">
      <package pattern="Nvt.Core" />
      <package pattern="Nvt.Core.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The exact Core name and prefix take precedence over `*`.
NuGet resolves `Nvt.Core` and `Nvt.Core.*` only from the local folder.
Other packages use `nuget.org`.
See [NuGet's source mapping rules](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping#package-pattern-precedence).
The fetch script never falls back to another package source.

Run these commands from the tool's repository root:

```powershell
python -B scripts/fetch_core_packages.py
dotnet restore --locked-mode
```

CI must stop when the script returns a nonzero exit code.
Build and verify scripts must also stop before restore when the download or verification fails.

## Manifest and command

Each package entry contains its Release tag, asset name, and SHA-256 from that Release's `SHA256SUMS`.
The manifest uses schema `1` and repository `Dennis40816/nvt_fw_core`.
Unknown keys and duplicate asset names are errors.
Release tags and asset names allow letters, digits, `.`, `_`, and `-`, but reject `..`.
Assets must end with `.nupkg`.
SHA-256 values must contain 64 lowercase hexadecimal characters.

Each package pins its own Release. Packages may use different versions.
`core-v<version>` contains `Nvt.Core` and `Nvt.Core.Avalonia`.
`Nvt.Core.Fonts` starts at independent version `0.1.0` with tag `core-fonts-v0.1.0`.
The [example manifest](core-packages.example.json) includes `Nvt.Core.Fonts.0.1.0.nupkg`.
Its all-zero Fonts SHA-256 is a placeholder. Replace it with the published Release's `SHA256SUMS` value.

```xml
<ItemGroup>
  <PackageReference Include="Nvt.Core" Version="0.5.0" />
  <PackageReference Include="Nvt.Core.Avalonia" Version="0.5.0" />
  <PackageReference Include="Nvt.Core.Fonts" Version="0.1.0" />
</ItemGroup>
```

```text
python fetch_core_packages.py [--manifest PATH] [--dest PATH] [--offline] [--base-url URL]
```

`--manifest` defaults to `core-packages.json` in the current directory.
`--dest` defaults to `artifacts/core-packages` beside the manifest.
Every relative destination resolves from the manifest's folder.
`--offline` verifies existing files and downloads nothing.
`--base-url` replaces GitHub's base URL for local HTTP tests.
Normal downloads require HTTPS.

The script verifies cached packages before it sends a request.
It warns about an incorrect cached hash, then downloads a replacement.
It checks each download before it replaces the destination file atomically.
It tries network failures up to three times with a short growing delay.
It never retries a SHA-256 mismatch.
Each request has a 60-second timeout and a 256 MiB response limit.

Each package has one standard-output line beginning with `core-packages: `.
The final success line contains `core-packages: ` followed by the absolute destination folder.
Warnings and errors go to standard error.
Exit codes are `0` for success, `1` for download or verification failure, and `2` for usage or manifest errors.

## Upgrade, rollback, and licenses

To upgrade, change the manifest to the new Release's asset names and SHA-256 values from its `SHA256SUMS`.
Update the exact package references and lock files, then run the tool's adoption checks again.
To roll back, put the earlier Release's values back in the manifest and restore its package references and lock files.
Fonts uses the same manifest with its independent `core-fonts-v<version>` tag in `release`.

A tool release takes `LICENSE` from a verified downloaded Core package and ships it as `licenses/Nvt.Core/LICENSE`.
That license covers Core package code, including Fonts.
A tool using Fonts also ships the package's `licenses/` folder and preserves Inter dependency licenses and notices.
See [Fonts license duties](../../docs/core/modules/Fonts.md#license-duties-and-updates).
Deliver Core only under its proprietary `LICENSE`.
The tool's own license does not relicense Core.

## A sandbox without network

Run the script on the host first.
Make the downloaded folder available inside the sandbox at the same manifest-relative destination.
Then verify the packages inside the sandbox before restore:

```powershell
python -B scripts/fetch_core_packages.py --offline
dotnet restore --locked-mode
```
