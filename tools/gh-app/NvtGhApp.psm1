# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
Set-StrictMode -Version Latest
$script:Configuration = $null

function Get-GhAppConfigPath {
    param([string]$Owner, [string]$Repo)
    Join-Path $HOME ".nvt/gh-app/$Owner-$Repo.json"
}

function Test-GhAppRepositoryPath {
    param([string]$Path)
    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path)))
    while ($null -ne $directory) {
        $marker = Join-Path $directory.FullName '.git'
        if ([IO.File]::Exists($marker) -or [IO.Directory]::Exists($marker)) { return $true }
        $directory = $directory.Parent
    }
    return $false
}

function Import-GhAppConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Owner, [Parameter(Mandatory)][string]$Repo)
    $script:Configuration = $null
    if ($Owner -cnotmatch '^[A-Za-z0-9-]+\z' -or $Repo -cnotmatch '^[A-Za-z0-9_.-]+\z' -or $Repo -in @('.', '..')) {
        throw 'Owner and Repo must name one GitHub repository.'
    }
    $path = Get-GhAppConfigPath $Owner $Repo
    if (Test-GhAppRepositoryPath $path) { throw 'The config file must be outside every repository.' }
    if (-not [IO.File]::Exists($path)) { throw 'The repository config file is missing.' }
    try { $config = [IO.File]::ReadAllText($path) | ConvertFrom-Json -AsHashtable -ErrorAction Stop }
    catch { throw 'The repository config file must contain a JSON object.' }
    if ($config -isnot [System.Collections.IDictionary]) { throw 'The repository config file must contain a JSON object.' }
    $fields = @('owner', 'repo', 'clientId', 'installationId', 'tokenHelperPath', 'dpapiPath', 'botLogin')
    foreach ($field in $fields) {
        if (-not $config.Contains($field) -or [string]::IsNullOrWhiteSpace([string]$config[$field])) {
            throw "Config field '$field' is required."
        }
    }
    if ($config.owner -cne $Owner -or $config.repo -cne $Repo) { throw "Repository mismatch. Expected $Owner/$Repo." }
    if ([string]$config.installationId -cnotmatch '^[1-9][0-9]{0,17}\z') { throw "Config field 'installationId' must be a positive integer." }
    foreach ($field in @('owner', 'repo', 'clientId', 'tokenHelperPath', 'dpapiPath', 'botLogin')) {
        if ($config[$field] -isnot [string] -or $config[$field] -match '[\x00-\x1f\x7f]') {
            throw "Config field '$field' must be a string without control characters."
        }
    }
    foreach ($field in @('tokenHelperPath', 'dpapiPath')) {
        if (-not [IO.Path]::IsPathFullyQualified($config[$field])) { throw "Config field '$field' must be an absolute path." }
    }
    if ([IO.Path]::GetExtension($config.tokenHelperPath) -ine '.ps1' -or -not [IO.File]::Exists($config.tokenHelperPath)) {
        throw "Config field 'tokenHelperPath' must name an installed PowerShell script."
    }
    # Copy only the defined fields. The returned object cannot change the active configuration.
    $script:Configuration = @{}
    foreach ($field in $fields) { $script:Configuration[$field] = $config[$field] }
    [pscustomobject]$script:Configuration.Clone()
}

function Get-GhAppContext {
    param([string]$Repository)
    if ($null -eq $script:Configuration) { throw 'Import-GhAppConfig must succeed before using this module.' }
    $expected = "$($script:Configuration.owner)/$($script:Configuration.repo)"
    if (($Repository -and $Repository -cne $expected) -or ($env:GH_REPO -and $env:GH_REPO -cne $expected)) {
        throw "Repository mismatch. Expected $expected."
    }
    $script:Configuration
}

function New-GhAppProcessInfo {
    param([string]$Executable, [string[]]$Arguments)
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $info.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($name in @($info.Environment.Keys)) {
        if ($name -match '^(GH_|GITHUB_|GCM_|BW_|GIT_CONFIG_)' -or $name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')) {
            [void]$info.Environment.Remove($name)
        }
    }
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    $info
}

function Get-GhAppToken {
    param([hashtable]$Context, [string]$ApiPath)
    $process = $null
    $token = $null
    try {
        $executable = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $arguments = @('-NoProfile', '-NonInteractive', '-File', $Context.tokenHelperPath,
            '-Mode', 'token', '-Owner', $Context.owner, '-Repo', $Context.repo,
            '-ClientId', $Context.clientId, '-InstallationId', [string]$Context.installationId,
            '-DpapiPath', $Context.dpapiPath)
        $process = [Diagnostics.Process]::Start((New-GhAppProcessInfo $executable $arguments))
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $token = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $token -cnotmatch '^[\x21-\x7e]+\z') { throw 'Invalid helper result.' }
        return $token
    } catch { throw "HTTP unknown $ApiPath" }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $token = $null
    }
}

function Invoke-GhAppGh {
    param([hashtable]$Context, [string[]]$Arguments, [string]$ApiPath,
        [AllowNull()][string]$InputText, [switch]$AsApp, [int[]]$AllowedExitCodes = @(0), [switch]$AllowNotFound)
    $process = $null
    $token = $null
    $info = $null
    $failure = "HTTP unknown $ApiPath"
    try {
        $executable = (Get-Command gh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $info = New-GhAppProcessInfo $executable $Arguments
        $info.Environment['GH_HOST'] = 'github.com'
        $info.Environment['GH_REPO'] = "$($Context.owner)/$($Context.repo)"
        $info.Environment['GH_PROMPT_DISABLED'] = '1'
        if ($AsApp) {
            $token = Get-GhAppToken $Context $ApiPath
            $info.Environment['GH_TOKEN'] = $token
        }
        $process = [Diagnostics.Process]::Start($info)
        # Remove the parent's reference to the child's environment immediately after launch.
        [void]$info.Environment.Remove('GH_TOKEN')
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if ($null -ne $InputText) { $process.StandardInput.Write($InputText) }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $output = $outputTask.GetAwaiter().GetResult()
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($token) {
            $output = $output.Replace($token, '[redacted]')
            $errorText = $errorText.Replace($token, '[redacted]')
        }
        $status = 'unknown'
        if ($errorText -match '\bHTTP\s+(\d{3})\b') { $status = $Matches[1] }
        if ($process.ExitCode -notin $AllowedExitCodes) {
            if ($AllowNotFound -and $status -eq '404') { return $null }
            $failure = "HTTP $status $ApiPath"
            throw 'Child failed.'
        }
        [pscustomobject]@{ Output = $output; ExitCode = $process.ExitCode }
    } catch { throw $failure }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        if ($null -ne $info) { [void]$info.Environment.Remove('GH_TOKEN') }
        $token = $null
    }
}

function Invoke-GhAppApi {
    param([hashtable]$Context, [string]$Method, [string]$Suffix, [hashtable]$Body,
        [switch]$AsApp, [switch]$AllowNotFound)
    $decoded = [Uri]::UnescapeDataString($Suffix.Split('?')[0])
    if ($decoded -match '(^|/)\.\.?(/|$)|\\|//|[\x00-\x20\x7f]' -or $decoded.Contains('%') -or $decoded.StartsWith('/')) {
        throw "Repository mismatch. Expected $($Context.owner)/$($Context.repo)."
    }
    $path = "/repos/$($Context.owner)/$($Context.repo)/$Suffix"
    $arguments = @('api', $path, '--hostname', 'github.com', '--method', $Method)
    $inputText = $null
    if ($null -ne $Body) {
        $arguments += @('--input', '-')
        $inputText = ConvertTo-Json -InputObject $Body -Depth 30 -Compress
    }
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -InputText $inputText `
        -AsApp:$AsApp -AllowNotFound:$AllowNotFound
    if ($null -eq $result -or [string]::IsNullOrWhiteSpace($result.Output)) { return $null }
    try { ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
    catch { throw "HTTP unknown $path" }
}

function Get-GhAppItems {
    param([hashtable]$Context, [string]$Suffix)
    $separator = $(if ($Suffix.Contains('?')) { '&' } else { '?' })
    for ($page = 1; $page -le 1000; $page++) {
        $items = @(Invoke-GhAppApi $Context GET "${Suffix}${separator}per_page=100&page=$page")
        $items
        if ($items.Count -lt 100) { return }
    }
    throw 'Repository pagination exceeded 1000 pages.'
}

function Assert-GhAppSha {
    param([string]$Name, $Value)
    if (@($Value).Count -ne 1 -or [string]$Value -cnotmatch '^[0-9a-f]{40}\z') { throw "$Name is not a 40-character SHA." }
    [string]$Value
}

function Assert-GhAppBranch {
    param([string]$Branch)
    if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -match '[\x00-\x20\x7f~^:?*\[\\]' -or
        $Branch -match '\.\.|@\{|//|^[-/.]|[/.]$|(^|/)\.|\.lock($|/)' -or $Branch -eq '@') {
        throw 'Branch must be a valid local repository branch name.'
    }
}

function Read-GhAppBody {
    param([string]$Path)
    $Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    if ($null -ne $script:Configuration -and [string]::Equals([IO.Path]::GetFullPath($Path),
        [IO.Path]::GetFullPath($script:Configuration.dpapiPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The body or message file must not name the configured DPAPI file.'
    }
    if (-not [IO.File]::Exists($Path)) { throw 'The body or message file is missing.' }
    [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
}

function Get-GhAppPullRequest {
    param([hashtable]$Context, [int]$Number)
    $path = "/repos/$($Context.owner)/$($Context.repo)/pulls/$Number"
    $arguments = @('pr', 'view', [string]$Number, '--repo', "$($Context.owner)/$($Context.repo)",
        '--json', 'state,headRefOid,headRefName,baseRefName,mergeStateStatus,isCrossRepository,mergeCommit')
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path
    try { $pr = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
    catch { throw "HTTP unknown $path" }
    if ($pr.isCrossRepository) { throw "Repository mismatch. Expected $($Context.owner)/$($Context.repo)." }
    Assert-GhAppBranch $pr.headRefName
    $null = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    $pr
}

function Test-GhAppOwnerApproval {
    param([hashtable]$Context, [int]$Number, [string]$Head)
    # A COMMENTED review does not change approval on GitHub, so only state-changing reviews count.
    $reviews = @(Get-GhAppItems $Context "pulls/$Number/reviews" | Where-Object {
        $_.user.login -ieq $Context.owner -and $_.state -cin @('APPROVED', 'CHANGES_REQUESTED', 'DISMISSED', 'PENDING') })
    if ($reviews.Count -eq 0) { return $false }
    $ranked = foreach ($review in $reviews) {
        try {
            $id = [long]$review.id
            if ($review.state -ceq 'PENDING') { $time = [DateTimeOffset]::MaxValue }
            elseif ($review.submitted_at) { $time = [DateTimeOffset]$review.submitted_at }
            else { throw 'A submitted review needs a timestamp.' }
        } catch { throw "HTTP unknown /repos/$($Context.owner)/$($Context.repo)/pulls/$Number/reviews" }
        [pscustomobject]@{ Review = $review; Time = $time; Id = $id }
    }
    # A draft can receive its ID before a later review, then be submitted last.
    $latest = ($ranked | Sort-Object -Property Time, Id | Select-Object -Last 1).Review
    return ($latest.state -ceq 'APPROVED' -and $latest.commit_id -ceq $Head)
}

function Invoke-GhAppGit {
    param([string]$Worktree, [string[]]$Arguments, [switch]$Bytes)
    $process = $null
    $buffer = [IO.MemoryStream]::new()
    try {
        $Worktree = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Worktree)
        $executable = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $process = [Diagnostics.Process]::Start((New-GhAppProcessInfo $executable (@('-C', $Worktree) + $Arguments)))
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($buffer)
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Local git command failed.' }
        if ($Bytes) { return ,$buffer.ToArray() }
        return [Text.Encoding]::UTF8.GetString($buffer.ToArray())
    } catch { throw 'Local git command failed.' }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $buffer.Dispose()
    }
}

function Get-GhAppTreeChanges {
    param([string]$Worktree, [string]$LocalBase)
    $raw = Invoke-GhAppGit $Worktree @('diff', '--raw', '-z', '--no-renames', '--no-abbrev', $LocalBase, 'HEAD', '--')
    if (-not $raw) { return }
    $parts = $raw.Split([char]0)
    for ($index = 0; $index -lt $parts.Length - 1; $index += 2) {
        $metadata = $parts[$index].Split(' ')
        $path = $parts[$index + 1]
        $deleted = $metadata[4] -ceq 'D'
        $mode = $(if ($deleted) { $metadata[0].Substring(1) } else { $metadata[1] })
        $type = $(if ($mode -eq '160000') { 'commit' } else { 'blob' })
        $sha = $(if ($deleted) { $null } else { Assert-GhAppSha 'local blob or gitlink' $metadata[3] })
        $content = $null
        if (-not $deleted -and $type -eq 'blob') {
            $bytes = Invoke-GhAppGit -Worktree $Worktree -Arguments @('cat-file', 'blob', $sha) -Bytes
            $content = [Convert]::ToBase64String($bytes)
        }
        [pscustomobject]@{ Path = $path; Mode = $mode; Type = $type; Sha = $sha; Content = $content }
    }
}

function Write-GhAppCommit {
    param([hashtable]$Context, [string]$Worktree, [string]$LocalBase, [string[]]$Parents, [string]$Message)
    $expectedTree = Assert-GhAppSha 'local tree' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', 'HEAD^{tree}')).Trim()
    $localBaseTree = Assert-GhAppSha 'local base tree' (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$LocalBase^{tree}")).Trim()
    $changes = @(Get-GhAppTreeChanges $Worktree $LocalBase)
    $parentCommit = Invoke-GhAppApi $Context GET "git/commits/$($Parents[0])"
    $baseTree = Assert-GhAppSha 'remote base tree' $parentCommit.tree.sha
    if ($baseTree -cne $localBaseTree) { throw "base tree mismatch: remote $baseTree local $localBaseTree" }
    $entries = @(foreach ($change in $changes) {
        if ($null -ne $change.Content) {
            $blob = Invoke-GhAppApi $Context POST 'git/blobs' @{ content = $change.Content; encoding = 'base64' } -AsApp
            $blobSha = Assert-GhAppSha 'new blob' $blob.sha
            if ($blobSha -cne $change.Sha) { throw "blob mismatch $($change.Path)" }
        }
        @{ path = $change.Path; mode = $change.Mode; type = $change.Type; sha = $change.Sha }
    })
    $tree = Invoke-GhAppApi $Context POST 'git/trees' @{ base_tree = $baseTree; tree = @($entries) } -AsApp
    $treeSha = Assert-GhAppSha 'new tree' $tree.sha
    if ($treeSha -cne $expectedTree) { throw "tree mismatch: remote $treeSha local $expectedTree" }
    $commit = Invoke-GhAppApi $Context POST 'git/commits' @{ tree = $treeSha; message = $Message; parents = $Parents } -AsApp
    $commitSha = Assert-GhAppSha 'new commit' $commit.sha
    [pscustomobject]@{ Files = $changes.Count; Tree = $treeSha; Commit = $commitSha }
}

function Push-GhAppBranch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Worktree, [Parameter(Mandatory)][string]$LocalBase,
        [Parameter(Mandatory)][string]$RemoteParent, [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$MessageFile, [string]$ExtraParent, [switch]$AllowOpenPr,
        [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $parents = @(Assert-GhAppSha 'RemoteParent' $RemoteParent)
    $LocalBase = Assert-GhAppSha 'LocalBase' $LocalBase
    if ($ExtraParent) { $parents += Assert-GhAppSha 'ExtraParent' $ExtraParent }
    Assert-GhAppBranch $Branch
    $message = Read-GhAppBody $MessageFile
    $headFilter = [Uri]::EscapeDataString("$($context.owner):$Branch")
    $open = @(Get-GhAppItems $context "pulls?state=open&head=$headFilter")
    foreach ($pr in $open) {
        $reviews = @(Get-GhAppItems $context "pulls/$($pr.number)/reviews" | Where-Object { $_.user.login -ieq $context.owner })
        if ($reviews.Count -gt 0) { throw "#$($pr.number) has an owner review; push refused. Open a follow-up PR instead." }
        if (-not $AllowOpenPr) { throw "#$($pr.number) is open; push refused. Pass -AllowOpenPr only if it has not been sent to the owner." }
    }
    $result = Write-GhAppCommit $context $Worktree $LocalBase $parents $message
    $encodedBranch = [Uri]::EscapeDataString($Branch)
    $ref = Invoke-GhAppApi $context GET "git/ref/heads/$encodedBranch" -AllowNotFound
    if ($null -ne $ref) {
        $null = Assert-GhAppSha 'remote branch head' $ref.object.sha
        $null = Invoke-GhAppApi $context PATCH "git/refs/heads/$encodedBranch" @{ sha = $result.Commit; force = $false } -AsApp
    } else {
        $null = Invoke-GhAppApi $context POST 'git/refs' @{ ref = "refs/heads/$Branch"; sha = $result.Commit } -AsApp
    }
    [pscustomobject]@{ Files = $result.Files; Tree = $result.Tree; Commit = $result.Commit; Branch = $Branch }
}

function New-GhAppPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Head, [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$BodyFile, [string]$Base = 'main', [switch]$Draft,
        [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    Assert-GhAppBranch $Head
    Assert-GhAppBranch $Base
    $created = Invoke-GhAppApi $context POST 'pulls' @{ head = $Head; base = $Base; title = $Title;
        body = (Read-GhAppBody $BodyFile); draft = [bool]$Draft } -AsApp
    $pr = Invoke-GhAppApi $context GET "pulls/$($created.number)"
    $url = "https://github.com/$($context.owner)/$($context.repo)/pull/$($created.number)"
    if ($pr.user.login -cne $context.botLogin) { throw "PR author does not match botLogin. Inspect $url" }
    [pscustomobject]@{ Number = $created.number; Url = $url }
}

function Set-GhAppPullRequestBody {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$BodyFile, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    Invoke-GhAppApi $context PATCH "pulls/$Number" @{ body = (Read-GhAppBody $BodyFile) } -AsApp
}

function Add-GhAppComment {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][Alias('CommentFile')][string]$BodyFile, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    Invoke-GhAppApi $context POST "issues/$Number/comments" @{ body = (Read-GhAppBody $BodyFile) } -AsApp
}

function Add-GhAppReviewRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][string]$ExpectedHeadSha, [Parameter(Mandatory)][string]$BodyFile,
        [string]$Event = 'COMMENT', [Alias('Repo')][string]$Repository)
    if ($Event -cne 'COMMENT') { throw 'Review event must be COMMENT. Only the owner approves.' }
    $context = Get-GhAppContext $Repository
    $expected = Assert-GhAppSha 'ExpectedHeadSha' $ExpectedHeadSha
    $body = Read-GhAppBody $BodyFile
    $pr = Get-GhAppPullRequest $context $Number
    $head = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    if ($head -cne $expected) { throw "#$Number head changed. Expected $expected; found $head." }
    Invoke-GhAppApi $context POST "pulls/$Number/reviews" @{ event = 'COMMENT'; commit_id = $head; body = $body } -AsApp
}

function Write-GhAppMergeLog {
    param([string]$LogPath, [string]$Message)
    $line = "$([DateTimeOffset]::UtcNow.ToString('o')) $Message"
    try { [IO.File]::AppendAllText($LogPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
    catch { throw 'The merge log could not be written.' }
    Write-Verbose $line
}

# The one clock read by the merge wait. Tests replace it to control time.
function Get-GhAppUtcNow { [DateTimeOffset]::UtcNow }

function Wait-GhAppApprovedHead {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$LogPath, [int]$TimeoutSeconds, [int]$PollSeconds)
    $head = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
    $deadline = (Get-GhAppUtcNow).AddSeconds($TimeoutSeconds)
    $updates = 0
    while ($true) {
        if ($Pr.state -cne 'OPEN') { throw "#$Number stop: state $($Pr.state)." }
        $current = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
        if (-not (Test-GhAppOwnerApproval $Context $Number $current)) {
            if ($current -cne $head) { throw "#$Number stop: approval did not carry to $current." }
            throw "#$Number stop: no owner approval on $current."
        }
        if ($current -cne $head) { Write-GhAppMergeLog $LogPath "#$Number updated $head -> $current; approval kept"; $head = $current }
        if ($Pr.mergeStateStatus -ceq 'DIRTY') { throw "#$Number stop: merge conflict with main; manual resolution requires owner approval." }
        if ((Get-GhAppUtcNow) -ge $deadline) { throw "#$Number stop: timed out in state $($Pr.mergeStateStatus)." }
        if ($Pr.mergeStateStatus -ceq 'BEHIND') {
            if ($updates -ge 3) { throw "#$Number stop: still behind after 3 updates." }
            Write-GhAppMergeLog $LogPath "#$Number update-branch from $head"
            $null = Invoke-GhAppApi $Context PUT "pulls/$Number/update-branch" @{ expected_head_sha = $head } -AsApp
            $updates++
        } else {
            $path = "/repos/$($Context.owner)/$($Context.repo)/pulls/$Number/checks"
            $arguments = @('pr', 'checks', [string]$Number, '--repo', "$($Context.owner)/$($Context.repo)", '--required', '--json', 'name,bucket,state')
            $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -AllowedExitCodes @(0, 1, 8)
            try { $checks = @(ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop) }
            catch { throw "HTTP unknown $path" }
            if (@($checks | Where-Object { $_.bucket -in @('fail', 'cancel') }).Count -gt 0) { throw "#$Number stop: required checks failed." }
            $pending = @($checks | Where-Object { $_.bucket -notin @('pass', 'skipping') }).Count -gt 0
            if (-not $pending -and $result.ExitCode -eq 0 -and $Pr.mergeStateStatus -ceq 'CLEAN') {
                # Recheck both the head and the latest owner review after checking CI.
                $fresh = Get-GhAppPullRequest $Context $Number
                if ($fresh.headRefOid -ceq $head -and $fresh.state -ceq 'OPEN' -and $fresh.mergeStateStatus -ceq 'CLEAN' -and
                    (Test-GhAppOwnerApproval $Context $Number $head)) { return $fresh }
                $Pr = $fresh
                continue
            }
        }
        Start-Sleep -Seconds $PollSeconds
        $Pr = Get-GhAppPullRequest $Context $Number
    }
}

function Remove-GhAppMergedBranch {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$Head, [string]$LogPath)
    $branch = $Pr.headRefName
    $encoded = [Uri]::EscapeDataString($branch)
    if ($branch -ceq 'main' -or $branch -ceq $Pr.baseRefName) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; base branch"
        return
    }
    $ref = Invoke-GhAppApi $Context GET "git/ref/heads/$encoded" -AllowNotFound
    if ($null -eq $ref -or $ref.object.sha -cne $Head) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; ref changed or absent"
        return
    }
    $comparison = Invoke-GhAppApi $Context GET "compare/main...$Head"
    if ($comparison.status -notin @('behind', 'identical') -or $comparison.ahead_by -ne 0) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; main does not contain $Head"
        return
    }
    $null = Invoke-GhAppApi $Context DELETE "git/refs/heads/$encoded" -AsApp
    Write-GhAppMergeLog $LogPath "#$Number deleted branch $branch at $Head"
}

function Merge-GhAppApprovedPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int[]]$Numbers,
        [Parameter(Mandatory)][Alias('Log')][string]$LogPath, [ValidateRange(1, 3600)][int]$TimeoutSeconds = 1200,
        [ValidateRange(1, 60)][int]$PollSeconds = 15, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    foreach ($number in $Numbers) {
        try {
            Write-GhAppMergeLog $LogPath "#$number start"
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'OPEN') { Write-GhAppMergeLog $LogPath "#$number skip: state $($pr.state)"; continue }
            $pr = Wait-GhAppApprovedHead $context $number $pr $LogPath $TimeoutSeconds $PollSeconds
            $head = Assert-GhAppSha "head of #$number" $pr.headRefOid
            $merged = Invoke-GhAppApi $context PUT "pulls/$number/merge" @{ merge_method = 'merge'; sha = $head } -AsApp
            if (-not $merged.merged) { throw "#$number stop: merge failed." }
            $mergeSha = Assert-GhAppSha "merge commit of #$number" $merged.sha
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'MERGED') { throw "#$number stop: merge was not confirmed." }
            Write-GhAppMergeLog $LogPath "#$number merged $head -> $mergeSha"
            Remove-GhAppMergedBranch $context $number $pr $head $LogPath
        } catch {
            Write-GhAppMergeLog $LogPath "#$number error: $($_.Exception.Message)"
            throw
        }
    }
    Write-GhAppMergeLog $LogPath "done: $($Numbers -join ',')"
}

function Close-GhAppPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [Parameter(Mandatory)][Alias('BodyFile')][string]$CommentFile, [switch]$DeleteBranch,
        [Alias('DeleteBranchPrefix')][string]$BranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    if ($DeleteBranch -and [string]::IsNullOrWhiteSpace($BranchPrefix)) { throw 'DeleteBranch requires a nonempty BranchPrefix.' }
    $body = Read-GhAppBody $CommentFile
    $pr = Get-GhAppPullRequest $context $Number
    if ($DeleteBranch -and (-not $pr.headRefName.StartsWith($BranchPrefix, [StringComparison]::Ordinal) -or
        $pr.headRefName -ceq 'main' -or $pr.headRefName -ceq $pr.baseRefName)) { throw 'Head branch does not satisfy BranchPrefix; deletion refused.' }
    $null = Invoke-GhAppApi $context POST "issues/$Number/comments" @{ body = $body } -AsApp
    $closed = Invoke-GhAppApi $context PATCH "pulls/$Number" @{ state = 'closed' } -AsApp
    if ($closed.state -cne 'closed') { throw "#$Number close was not confirmed." }
    if ($DeleteBranch) {
        $encoded = [Uri]::EscapeDataString($pr.headRefName)
        $ref = Invoke-GhAppApi $context GET "git/ref/heads/$encoded"
        if ($ref.object.sha -cne $pr.headRefOid) { throw 'Head branch changed; deletion refused.' }
        $null = Invoke-GhAppApi $context DELETE "git/refs/heads/$encoded" -AsApp
    }
}

function Assert-GhAppReadArguments {
    param([hashtable]$Context, [string[]]$Arguments)
    $expected = "$($Context.owner)/$($Context.repo)"
    $refusal = 'Invoke-GhAppRead accepts only repository-scoped read commands.'
    if ($Arguments.Count -lt 2) { throw $refusal }
    $command = $Arguments[0]
    $subcommand = $Arguments[1]
    $api = $command -ceq 'api'
    if ($api) {
        $path = $subcommand.TrimStart('/')
        $route = $path.Split('?')[0]
        $decoded = [Uri]::UnescapeDataString($route)
        if (($route -cne "repos/$expected" -and -not $route.StartsWith("repos/$expected/", [StringComparison]::Ordinal)) -or
            $decoded -match '(^|/)\.\.?(/|$)|\\|//|[\x00-\x20\x7f]' -or $decoded.Contains('%')) {
            throw "Repository mismatch. Expected $expected."
        }
        $valueFlags = @('--method', '-X', '--jq', '-q')
        $switchFlags = @('--paginate', '--slurp')
    } else {
        if (($command -cnotin @('pr', 'issue') -or $subcommand -cnotin @('view', 'list', 'checks')) -and
            -not ($command -ceq 'repo' -and $subcommand -ceq 'view')) { throw $refusal }
        if ($command -ceq 'issue' -and $subcommand -ceq 'checks') { throw $refusal }
        $valueFlags = @('--repo', '-R', '--json', '--jq', '-q', '--limit', '-L', '--state', '--head', '--base',
            '--author', '--assignee', '--label')
        $switchFlags = @('--required', '--draft')
    }
    $positionals = 0
    for ($index = 2; $index -lt $Arguments.Count; $index++) {
        $argument = $Arguments[$index]
        $flag = $argument
        $value = $null
        if ($argument.Contains('=')) { $flag, $value = $argument.Split('=', 2) }
        if ($argument -cmatch '^-R(.+)\z') { $flag = '-R'; $value = $Matches[1].TrimStart('=') }
        if ($flag -cin $valueFlags) {
            if ($null -eq $value) {
                if (++$index -ge $Arguments.Count) { throw $refusal }
                $value = $Arguments[$index]
            }
            if ($flag -cin @('--repo', '-R') -and $value -cne $expected) { throw "Repository mismatch. Expected $expected." }
            if ($flag -cin @('--method', '-X') -and $value -cne 'GET') { throw $refusal }
        } elseif ($argument -cin $switchFlags) { continue }
        elseif (-not $api -and -not $argument.StartsWith('-')) {
            $positionals++
            if ($positionals -gt 1 -or $subcommand -ceq 'list') { throw $refusal }
            if ($command -ceq 'repo') {
                if ($argument -cne $expected) { throw "Repository mismatch. Expected $expected." }
            } elseif ($argument -cnotmatch '^[1-9][0-9]*\z') { throw $refusal }
        } else { throw $refusal }
    }
    if ($api) { return ,$Arguments }
    if ($command -ceq 'repo') {
        if ($positionals -eq 0) { return ,($Arguments + @($expected)) }
        return ,$Arguments
    }
    return ,($Arguments + @('--repo', $expected))
}

function Invoke-GhAppRead {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Arguments, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $safeArguments = Assert-GhAppReadArguments $context $Arguments
    $path = "/repos/$($context.owner)/$($context.repo)"
    if ($safeArguments[0] -ceq 'api') { $path = '/' + $safeArguments[1].TrimStart('/') }
    $result = Invoke-GhAppGh -Context $context -Arguments $safeArguments -ApiPath $path
    $result.Output.TrimEnd("`r", "`n")
}

Export-ModuleMember -Function Import-GhAppConfig, Push-GhAppBranch, New-GhAppPullRequest,
    Set-GhAppPullRequestBody, Add-GhAppComment, Add-GhAppReviewRecord,
    Merge-GhAppApprovedPullRequest, Close-GhAppPullRequest, Invoke-GhAppRead
