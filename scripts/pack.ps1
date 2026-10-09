# Copyright (c) 2026 Dennis Liu. All rights reserved.

param(
    [ValidateSet('Core', 'Fonts')]
    [string]$Package = 'Core'
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $versionFile = if ($Package -eq 'Fonts') { 'src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj' } else { 'Directory.Build.props' }
    $tagPrefix = if ($Package -eq 'Fonts') { 'core-fonts-v' } else { 'core-v' }
    $packageIds = if ($Package -eq 'Fonts') { @('Nvt.Core.Fonts') } else { @('Nvt.Core', 'Nvt.Core.Avalonia') }
    [xml]$properties = Get-Content -LiteralPath $versionFile -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "$versionFile must define the $Package version."
    }
    $tag = "$tagPrefix$version"
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot read the source commit.'
    }
    $existingTag = git tag --list $tag
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot read version tags.'
    }

    # Only the first release of the checked-out GitHub tag may use an existing tag.
    $isTagPush = $env:GITHUB_ACTIONS -eq 'true' -and
        $env:GITHUB_EVENT_NAME -eq 'push' -and
        $env:GITHUB_REF -like 'refs/tags/*'
    $isTagRelease = $isTagPush -and $env:GITHUB_REF -like "refs/tags/$tagPrefix*"
    if ($isTagPush -and -not $isTagRelease) {
        throw "The release tag must be $tag."
    }
    if ($isTagRelease) {
        if ($env:GITHUB_REF -cne "refs/tags/$tag" -or $existingTag -cne $tag) {
            throw "The release tag must be $tag."
        }
        $tagCommit = git rev-parse "refs/tags/$tag^{commit}"
        if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $commit -or $env:GITHUB_SHA -cne $commit) {
            throw 'The release tag must point to the source commit at HEAD.'
        }
        $releaseTags = @(gh api "repos/$env:GITHUB_REPOSITORY/releases" --paginate --jq '.[].tag_name')
        if ($LASTEXITCODE -ne 0) {
            throw 'Cannot check existing GitHub Releases.'
        }
        if ($releaseTags -ccontains $tag) {
            throw "Release $tag already exists. Use its original packages."
        }
    }
    elseif ($existingTag) {
        throw "Version $version already has tag $tag. Select a new version."
    }

    $changes = git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot check the source working tree.'
    }
    if ($changes) {
        throw 'Pack from a clean working tree so the source commit identifies the package content.'
    }

    $output = Join-Path (Get-Location) 'artifacts/packages'
    if ((Test-Path -LiteralPath $output) -and
        @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
        throw 'artifacts/packages must be empty. Preserve its packages before packing again.'
    }

    $env:AVALONIA_TELEMETRY_OPTOUT = '1'
    $sourceProperties = @("-p:RepositoryCommit=$commit", "-p:SourceRevisionId=$commit")
    Invoke-DotNet -Arguments @('restore', 'Nvt.Core.sln', '--locked-mode')
    Invoke-DotNet -Arguments (@('build', 'Nvt.Core.sln', '--configuration', 'Release', '--no-restore') + $sourceProperties)
    Invoke-DotNet -Arguments @('test', 'Nvt.Core.sln', '--configuration', 'Release', '--no-build', '--no-restore')

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    foreach ($packageId in $packageIds) {
        Invoke-DotNet -Arguments (@('pack', "src/$packageId/$packageId.csproj",
            '--configuration', 'Release', '--no-build', '--no-restore', '--output', $output) + $sourceProperties)
    }

    $checksums = foreach ($packageId in $packageIds) {
        $name = "$packageId.$version.nupkg"
        $hash = (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), ($checksums -join "`n") + "`n")
    $source = "Version: $version`nCommit: $commit`nRepository: https://github.com/Dennis40816/nvt_fw_core`n"
    if ($Package -eq 'Fonts') {
        $source += "Package: Nvt.Core.Fonts`n"
    }
    [IO.File]::WriteAllText((Join-Path $output 'SOURCE.md'), $source)
    $releaseName = if ($Package -eq 'Fonts') { 'Core Fonts' } else { 'Core' }
    Write-Host "Packed $releaseName $version from $commit into artifacts/packages/."
    Write-Host "Output: $(@($packageIds).Count) .nupkg files, SHA256SUMS, and SOURCE.md."
}
finally {
    Pop-Location
}
