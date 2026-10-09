# Copyright (c) 2026 Dennis Liu. All rights reserved.

param(
    [string]$FontsPackagePath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
[xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
$commit = 'a' * 40
[xml]$fontsProperties = Get-Content -LiteralPath (Join-Path $repository 'src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj') -Raw
$sets = @(
    @{ Package = 'Core'; Version = $version; TagPrefix = 'core-v'
        VersionFile = 'Directory.Build.props'; PackageIds = @('Nvt.Core', 'Nvt.Core.Avalonia') }
    @{ Package = 'Fonts'; Version = [string]$fontsProperties.Project.PropertyGroup.Version; TagPrefix = 'core-fonts-v'
        VersionFile = 'src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj'; PackageIds = @('Nvt.Core.Fonts') }
)
$pwsh = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })

$cases = @(
    foreach ($set in $sets) {
        $version = $set.Version
        $tag = "$($set.TagPrefix)$version"
        $setCases = @(
            @{ Name = 'local: version tag exists'; ExistingTag = $true
                Error = "Version $version already has tag $tag. Select a new version." }
            @{ Name = 'local: dirty working tree'; Dirty = $true
                Error = 'Pack from a clean working tree so the source commit identifies the package content.' }
            @{ Name = 'local: non-empty output folder'; NonEmptyOutput = $true
                Error = 'artifacts/packages must be empty. Preserve its packages before packing again.' }
            @{ Name = 'local: missing version'; MissingVersion = $true
                Error = "$($set.VersionFile) must define the $($set.Package) version." }
            foreach ($step in @('restore', 'build', 'test', 'pack')) {
                @{ Name = "local: dotnet $step fails"; FailStep = $step
                    Error = "dotnet $step failed with exit code 23." }
            }
            @{ Name = 'tag release: tag name differs from version'; TagRelease = $true; WrongTag = $true
                Error = "The release tag must be $tag." }
            @{ Name = 'tag release: tag commit differs from HEAD'; TagRelease = $true; WrongTagCommit = $true
                Error = 'The release tag must point to the source commit at HEAD.' }
            @{ Name = 'tag release: GitHub SHA differs from HEAD'; TagRelease = $true; WrongGitHubSha = $true
                Error = 'The release tag must point to the source commit at HEAD.' }
            @{ Name = 'tag release: Release already exists'; TagRelease = $true; ExistingRelease = $true
                Error = "Release $tag already exists. Use its original packages." }
            @{ Name = 'tag release: gh fails to list Releases'; TagRelease = $true; GhFailure = $true
                Error = 'Cannot check existing GitHub Releases.' }
            @{ Name = 'local: success' }
            @{ Name = 'tag release: success'; TagRelease = $true }
            @{ Name = 'tag release: expected tag is absent'; TagRelease = $true; AbsentTag = $true
                Error = "The release tag must be $tag." }
            @{ Name = 'tag release: other package ref'; TagRelease = $true; WrongFamily = $true; AbsentTag = $true
                Error = "The release tag must be $tag." }
            @{ Name = 'tag release: unrelated ref'; TagRelease = $true; UnrelatedRef = $true; AbsentTag = $true
                Error = "The release tag must be $tag." }
            if ($set.Package -eq 'Core') {
                @{ Name = 'local: explicit Core selection'; ExplicitPackage = $true }
            }
        )
        foreach ($case in $setCases) {
            $case.Name = "$($set.Package) / $($case.Name)"
            $case.Package = $set.Package
            $case.Version = $set.Version
            $case.TagPrefix = $set.TagPrefix
            $case.VersionFile = $set.VersionFile
            $case.PackageIds = $set.PackageIds
            $case
        }
    }
)

# Each child process defines these functions before it invokes the copied pack script.
$runner = @'
# Copyright (c) 2026 Dennis Liu. All rights reserved.
$ErrorActionPreference = 'Stop'
$case = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'case.json') -Raw | ConvertFrom-Json -AsHashtable
$tag = "$($case.TagPrefix)$($case.Version)"
$env:GITHUB_ACTIONS = $null
$env:GITHUB_EVENT_NAME = $null
$env:GITHUB_REF = $null
$env:GITHUB_SHA = $null
$env:GITHUB_REPOSITORY = 'example/core'
if ($case.TagRelease) {
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_EVENT_NAME = 'push'
    $env:GITHUB_REF = "refs/tags/$tag"
    $env:GITHUB_SHA = $case.Commit
    if ($case.WrongTag) { $env:GITHUB_REF = "refs/tags/$($case.TagPrefix)999.0.0" }
    if ($case.WrongFamily) {
        $otherPrefix = if ($case.Package -eq 'Core') { 'core-fonts-v' } else { 'core-v' }
        $env:GITHUB_REF = "refs/tags/$otherPrefix$($case.Version)"
    }
    if ($case.UnrelatedRef) { $env:GITHUB_REF = 'refs/tags/unrelated-v999.0.0' }
    if ($case.WrongGitHubSha) { $env:GITHUB_SHA = 'b' * 40 }
}

function git {
    $global:LASTEXITCODE = 0
    $command = $args -join ' '
    if ($command -ceq 'rev-parse HEAD') { return $case.Commit }
    if ($command -ceq "tag --list $tag") {
        if ($case.AbsentTag) { return }
        if ($case.TagRelease -or $case.ExistingTag) { return $tag }
        return
    }
    if ($command -ceq "rev-parse refs/tags/$tag^{commit}") {
        if ($case.WrongTagCommit) { return ('b' * 40) }
        return $case.Commit
    }
    if ($command -ceq 'status --porcelain') {
        if ($case.Dirty) { return ' M scripts/pack.ps1' }
        return
    }
    throw "Unexpected git command: $command"
}

function gh {
    if (($args -join ' ') -cne 'api repos/example/core/releases --paginate --jq .[].tag_name') {
        throw "Unexpected gh command: $($args -join ' ')"
    }
    $global:LASTEXITCODE = 0
    if ($case.GhFailure) { $global:LASTEXITCODE = 17; return }
    if ($case.ExistingRelease) { return $tag }
    return 'unrelated-release'
}

function dotnet {
    $step = $args[0]
    if ($step -cnotin @('restore', 'build', 'test', 'pack')) {
        throw "Unexpected dotnet step: $step"
    }
    $global:LASTEXITCODE = 0
    if ($step -ceq $case.FailStep) { $global:LASTEXITCODE = 23; return }
    if ($step -ceq 'pack') {
        $packageId = [IO.Path]::GetFileNameWithoutExtension($args[1])
        if ($packageId -cnotin $case.PackageIds) {
            throw "Unexpected package: $packageId"
        }
        $outputIndex = [Array]::IndexOf($args, '--output')
        if ($outputIndex -lt 0) { throw 'dotnet pack requires --output.' }
        $output = $args[$outputIndex + 1]
        $name = "$packageId.$($case.Version).nupkg"
        $bytes = [Text.Encoding]::UTF8.GetBytes("Synthetic package: $packageId $($case.Version)`n")
        [IO.File]::WriteAllBytes((Join-Path $output $name), $bytes)
    }
}

try {
    if ($case.Package -eq 'Fonts' -or $case.ExplicitPackage) {
        & (Join-Path $PSScriptRoot 'scripts/pack.ps1') -Package $case.Package
    }
    else {
        & (Join-Path $PSScriptRoot 'scripts/pack.ps1')
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
exit 0
'@

$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("test-pack-" + [guid]::NewGuid().ToString('N'))))
$failed = 0
$index = 0
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    foreach ($case in $cases) {
        $index++
        try {
            $fixture = Join-Path $temporaryRoot "$index"
            New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') | Out-Null
            Copy-Item -LiteralPath (Join-Path $repository 'scripts/pack.ps1') -Destination (Join-Path $fixture 'scripts/pack.ps1')
            $fontsDirectory = Join-Path $fixture 'src/Nvt.Core.Fonts'
            New-Item -ItemType Directory -Path $fontsDirectory | Out-Null
            foreach ($versionFile in @('Directory.Build.props', 'src/Nvt.Core.Fonts/Nvt.Core.Fonts.csproj')) {
                Copy-Item -LiteralPath (Join-Path $repository $versionFile) -Destination (Join-Path $fixture $versionFile)
            }
            # The fixtures retain different versions to detect the wrong version source.
            $propsPath = Join-Path $fixture $case.VersionFile
            if ($case.MissingVersion) {
                [xml]$fixtureProperties = Get-Content -LiteralPath $propsPath -Raw
                foreach ($node in @($fixtureProperties.SelectNodes('/Project/PropertyGroup/Version'))) {
                    $node.ParentNode.RemoveChild($node) | Out-Null
                }
                $fixtureProperties.Save($propsPath)
            }
            $output = Join-Path $fixture 'artifacts/packages'
            if ($case.NonEmptyOutput) {
                New-Item -ItemType Directory -Path $output | Out-Null
                [IO.File]::WriteAllText((Join-Path $output 'existing.nupkg'), 'Existing package')
            }
            $version = $case.Version
            $case.Commit = $commit
            [IO.File]::WriteAllText((Join-Path $fixture 'case.json'), ($case | ConvertTo-Json))
            $runnerPath = Join-Path $fixture 'run.ps1'
            [IO.File]::WriteAllText($runnerPath, $runner)
            $messages = @(& $pwsh -NoLogo -NoProfile -NonInteractive -File $runnerPath 2>&1)
            $exitCode = $LASTEXITCODE
            $expectedExitCode = if ($case.Error) { 1 } else { 0 }
            if ($exitCode -ne $expectedExitCode) {
                throw "Expected exit $expectedExitCode, got ${exitCode}: $($messages -join ' ')"
            }
            if ($case.Error) {
                if (($messages -join "`n").Trim() -cne $case.Error) {
                    throw "Expected error '$($case.Error)', got '$($messages -join ' ')'."
                }
            }
            else {
                if (@($messages | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] }).Count -gt 0) {
                    throw "Unexpected error on success: $($messages -join ' ')"
                }
                $checksums = @(Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS'))
                if ($checksums.Count -ne $case.PackageIds.Count) {
                    throw "SHA256SUMS must list exactly $($case.PackageIds.Count) files."
                }
                $expectedFiles = @('SHA256SUMS', 'SOURCE.md') + @($case.PackageIds | ForEach-Object { "$_.$version.nupkg" })
                $actualFiles = @(Get-ChildItem -LiteralPath $output -File | ForEach-Object Name)
                if (Compare-Object $expectedFiles $actualFiles -CaseSensitive) {
                    throw 'The output must contain only the selected packages, SHA256SUMS, and SOURCE.md.'
                }
                foreach ($line in $checksums) {
                    if ($line -cnotmatch '^[0-9a-f]{64}  [A-Za-z0-9.-]+\.nupkg$') {
                        throw 'SHA256SUMS must use lowercase SHA-256, two spaces, and one package filename per line.'
                    }
                }
                foreach ($packageId in $case.PackageIds) {
                    $name = "$packageId.$version.nupkg"
                    $bytes = [IO.File]::ReadAllBytes((Join-Path $output $name))
                    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                    if ("$hash  $name" -cnotin $checksums) {
                        throw "SHA256SUMS does not match $name."
                    }
                }
                $source = @(Get-Content -LiteralPath (Join-Path $output 'SOURCE.md'))
                if ("Version: $version" -cnotin $source -or "Commit: $commit" -cnotin $source) {
                    throw 'SOURCE.md must record the version and commit.'
                }
                if ('Repository: https://github.com/Dennis40816/nvt_fw_core' -cnotin $source) {
                    throw 'SOURCE.md must record the repository.'
                }
                if ($case.Package -eq 'Fonts') {
                    if ($source.Count -ne 4 -or 'Package: Nvt.Core.Fonts' -cnotin $source) {
                        throw 'SOURCE.md must name the single Fonts package.'
                    }
                }
                elseif ($source.Count -ne 3) {
                    throw 'The Core SOURCE.md format must remain unchanged.'
                }
            }
            Write-Host "PASS $($case.Name)"
        }
        catch {
            $failed++
            Write-Host "FAIL $($case.Name): $($_.Exception.Message -replace '\r?\n', ' ')"
        }
    }
}
finally {
    if ((Resolve-Path -LiteralPath $temporaryRoot).Path -cne $temporaryRoot) {
        throw 'The temporary cleanup path changed.'
    }
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}


if ($FontsPackagePath) {
    $index++
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $FontsPackagePath).Path)
        try {
            function Read-PackageBytes {
                param([string]$Name)
                $entry = $archive.GetEntry($Name)
                if ($null -eq $entry) { throw "Missing package file: $Name" }
                $stream = $entry.Open()
                $buffer = [IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($buffer)
                    return ,$buffer.ToArray()
                }
                finally {
                    $stream.Dispose()
                    $buffer.Dispose()
                }
            }

            foreach ($file in @('LICENSE', 'README.md')) {
                $actual = [Convert]::ToHexString((Read-PackageBytes $file))
                $expected = [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $repository $file)))
                if ($actual -cne $expected) { throw "Package $file differs from its source." }
            }
            $fontsRoot = Join-Path $repository 'src/Nvt.Core.Fonts'
            foreach ($file in Get-ChildItem -LiteralPath (Join-Path $fontsRoot 'licenses') -Recurse -File) {
                $name = [IO.Path]::GetRelativePath($fontsRoot, $file.FullName).Replace('\', '/')
                $actual = [Convert]::ToHexString((Read-PackageBytes $name))
                $expected = [Convert]::ToHexString([IO.File]::ReadAllBytes($file.FullName))
                if ($actual -cne $expected) { throw "Package $name differs from its source." }
            }
            $assemblyHex = [Convert]::ToHexString((Read-PackageBytes 'lib/net10.0/Nvt.Core.Fonts.dll'))
            $assets = @(Get-ChildItem -LiteralPath (Join-Path $fontsRoot 'Assets') -Recurse -File |
                Where-Object Extension -In @('.ttf', '.otf'))
            if ($assets.Count -ne 5) { throw 'Expected five embedded font assets.' }
            foreach ($asset in $assets) {
                $assetHex = [Convert]::ToHexString([IO.File]::ReadAllBytes($asset.FullName))
                if (-not $assemblyHex.Contains($assetHex, [StringComparison]::Ordinal)) {
                    throw "The package assembly does not embed the original bytes of $($asset.Name)."
                }
            }
            Read-PackageBytes 'lib/net10.0/Nvt.Core.Fonts.xml' | Out-Null
            $nuspecStream = [IO.MemoryStream]::new((Read-PackageBytes 'Nvt.Core.Fonts.nuspec'))
            try {
                $nuspec = [xml]::new()
                $nuspec.Load($nuspecStream)
            }
            finally {
                $nuspecStream.Dispose()
            }
            $metadata = $nuspec.package.metadata
            if ($metadata.id -cne 'Nvt.Core.Fonts' -or $metadata.version -cne $sets[1].Version) {
                throw 'The Fonts package ID or independent version is incorrect.'
            }
            if ($metadata.readme -cne 'README.md' -or $metadata.license.type -cne 'file' -or
                $metadata.license.InnerText -cne 'LICENSE') {
                throw 'The package must declare its readme and license file.'
            }
            if ($metadata.repository.type -cne 'git' -or
                $metadata.repository.url -cne 'https://github.com/Dennis40816/nvt_fw_core' -or
                $metadata.repository.commit -cnotmatch '^[0-9a-f]{40}$') {
                throw 'The package must record the repository URL and source commit.'
            }
            $dependencies = @($nuspec.SelectNodes("//*[local-name()='dependencies']//*[local-name()='dependency']"))
            if ($dependencies.Count -ne 2 -or
                (Compare-Object @('Avalonia', 'Avalonia.Fonts.Inter') @($dependencies | ForEach-Object id) -CaseSensitive)) {
                throw 'Fonts must depend only on Avalonia and Avalonia.Fonts.Inter.'
            }
        }
        finally {
            $archive.Dispose()
        }
        Write-Host 'PASS Fonts / real package contents, embedded fonts, metadata, and dependencies'
    }
    catch {
        $failed++
        Write-Host "FAIL Fonts / real package contents: $($_.Exception.Message -replace '\r?\n', ' ')"
    }
}

Write-Host "$($index - $failed)/$index cases passed; $failed failed."
if ($failed -gt 0) { exit 1 }
exit 0
