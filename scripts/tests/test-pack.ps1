# Copyright (c) 2026 Dennis Liu. All rights reserved.

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
[xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
$commit = 'a' * 40
$tag = "core-v$version"
$pwsh = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })

$cases = @(
    @{ Name = 'local: version tag exists'; ExistingTag = $true
        Error = "Version $version already has tag $tag. Select a new version." }
    @{ Name = 'local: dirty working tree'; Dirty = $true
        Error = 'Pack from a clean working tree so the source commit identifies the package content.' }
    @{ Name = 'local: non-empty output folder'; NonEmptyOutput = $true
        Error = 'artifacts/packages must be empty. Preserve its packages before packing again.' }
    @{ Name = 'local: missing version'; MissingVersion = $true
        Error = 'Directory.Build.props must define the Core version.' }
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
)

# Each child process defines these functions before it invokes the copied pack script.
$runner = @'
# Copyright (c) 2026 Dennis Liu. All rights reserved.
$ErrorActionPreference = 'Stop'
$case = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'case.json') -Raw | ConvertFrom-Json -AsHashtable
$tag = "core-v$($case.Version)"
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
    if ($case.WrongTag) { $env:GITHUB_REF = 'refs/tags/core-v999.0.0' }
    if ($case.WrongGitHubSha) { $env:GITHUB_SHA = 'b' * 40 }
}

function git {
    $global:LASTEXITCODE = 0
    $command = $args -join ' '
    if ($command -ceq 'rev-parse HEAD') { return $case.Commit }
    if ($command -ceq "tag --list $tag") {
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
        if ($packageId -cnotin @('Nvt.Core', 'Nvt.Core.Avalonia')) {
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
    & (Join-Path $PSScriptRoot 'scripts/pack.ps1')
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
            $propsPath = Join-Path $fixture 'Directory.Build.props'
            Copy-Item -LiteralPath (Join-Path $repository 'Directory.Build.props') -Destination $propsPath
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
            $case.Version = $version
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
                if ($checksums.Count -ne 2) { throw 'SHA256SUMS must list exactly two files.' }
                foreach ($packageId in @('Nvt.Core', 'Nvt.Core.Avalonia')) {
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

Write-Host "$($cases.Count - $failed)/$($cases.Count) cases passed; $failed failed."
if ($failed -gt 0) { exit 1 }
exit 0
