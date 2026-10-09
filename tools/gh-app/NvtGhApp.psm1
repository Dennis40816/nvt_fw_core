# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
Set-StrictMode -Version Latest
$script:Configuration = $null

function Get-GhAppConfigPath {
    param([string]$Owner, [string]$Repo)
    Join-Path $HOME ".nvt/gh-app/$Owner-$Repo.json"
}

function Test-GhAppRepositoryPath {
    param([string]$Path, [Collections.Generic.HashSet[string]]$Visited)
    if ($null -eq $Visited) { $Visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
    $Path = [IO.Path]::GetFullPath($Path)
    if (-not $Visited.Add($Path)) { return $false }
    $file = [IO.FileInfo]::new($Path)
    if ($file.LinkTarget) {
        $target = $file.ResolveLinkTarget($true)
        if ($null -eq $target -or (Test-GhAppRepositoryPath $target.FullName $Visited)) { return $true }
    }
    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path)))
    while ($null -ne $directory) {
        $marker = Join-Path $directory.FullName '.git'
        if ([IO.File]::Exists($marker) -or [IO.Directory]::Exists($marker)) { return $true }
        if ([IO.File]::Exists((Join-Path $directory.FullName 'HEAD')) -and
            [IO.File]::Exists((Join-Path $directory.FullName 'config')) -and
            [IO.Directory]::Exists((Join-Path $directory.FullName 'objects'))) { return $true }
        if ($directory.LinkTarget) {
            $target = $directory.ResolveLinkTarget($true)
            if ($null -eq $target -or (Test-GhAppRepositoryPath (Join-Path $target.FullName '.gh-app-path-check') $Visited)) { return $true }
        }
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
    # A helper or key inside a repository could be changed by repository work, then run with App rights.
    foreach ($field in @('tokenHelperPath', 'dpapiPath')) {
        if (Test-GhAppRepositoryPath $config[$field]) { throw "Config field '$field' must be outside every repository." }
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
    $helperError = $null
    try {
        $executable = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $arguments = @('-NoProfile', '-NonInteractive', '-File', $Context.tokenHelperPath,
            '-Mode', 'token', '-Owner', $Context.owner, '-Repo', $Context.repo,
            '-ClientId', $Context.clientId, '-InstallationId', [string]$Context.installationId,
            '-DpapiPath', $Context.dpapiPath)
        # Only a push that changes workflow files sets this flag, and only for that one call chain.
        if ($Context.ContainsKey('includeWorkflowsWrite') -and $Context.includeWorkflowsWrite) { $arguments += '-IncludeWorkflowsWrite' }
        $process = [Diagnostics.Process]::Start((New-GhAppProcessInfo $executable $arguments))
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $token = $outputTask.GetAwaiter().GetResult()
        $helperError = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or $token -cnotmatch '^[\x21-\x7e]+\z') { throw 'Invalid helper result.' }
        return $token
    } catch {
        # Report only the helper's exit code. Its output and error text may hold secrets.
        # One fixed message is safe to show: GitHub did not grant the workflows permission to the installation.
        $code = $(if ($null -ne $process -and $process.HasExited) { $process.ExitCode } else { 'none' })
        $known = $(if ($helperError -clike '*Installation token lacks the requested workflows permission.*') { ': Installation token lacks the requested workflows permission.' } else { '' })
        throw "HTTP unknown $ApiPath (token helper exit $code)$known"
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
        $token = $null
    }
}

function Invoke-GhAppGh {
    param([hashtable]$Context, [string[]]$Arguments, [string]$ApiPath,
        [AllowNull()][string]$InputText, [switch]$AsApp, [int[]]$AllowedExitCodes = @(0), [switch]$AllowNotFound,
        [switch]$Read)
    # Only reads retry. A write that fails after GitHub applied it must never repeat, for example a comment or a merge.
    $attempts = $(if ($Read) { 3 } else { 1 })
    [void]$PSBoundParameters.Remove('Read')
    # A token failure happens before gh starts, so nothing reached GitHub and a write may retry it too.
    # A read already retries the whole call, so its token step runs once per attempt.
    $PSBoundParameters['TokenAttempts'] = $(if ($Read) { 1 } else { 3 })
    for ($attempt = 1; ; $attempt++) {
        try { return Invoke-GhAppGhOnce @PSBoundParameters }
        catch {
            $message = $_.Exception.Message
            # Retry unknown failures, rate limits and server errors. Other 4xx answers are final.
            if ($message -cnotmatch '^HTTP (unknown|429|5\d\d) ') { throw }
            if ($attempt -ge $attempts) {
                if ($attempts -gt 1) { throw "$message (after $attempts attempts)" }
                throw
            }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

function Invoke-GhAppGhOnce {
    param([hashtable]$Context, [string[]]$Arguments, [string]$ApiPath,
        [AllowNull()][string]$InputText, [switch]$AsApp, [int[]]$AllowedExitCodes = @(0), [switch]$AllowNotFound,
        [int]$TokenAttempts = 1)
    $process = $null
    $token = $null
    $info = $null
    $failure = "HTTP unknown $ApiPath"
    try {
        $executable = (Get-Command gh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        # gh receives the App token, so a copy that repository work could change is refused.
        if (Test-GhAppRepositoryPath $executable) { throw 'gh inside a repository.' }
        $info = New-GhAppProcessInfo $executable $Arguments
        $info.Environment['GH_HOST'] = 'github.com'
        $info.Environment['GH_REPO'] = "$($Context.owner)/$($Context.repo)"
        $info.Environment['GH_PROMPT_DISABLED'] = '1'
        if ($AsApp) {
            for ($attempt = 1; $null -eq $token; $attempt++) {
                try { $token = Get-GhAppToken $Context $ApiPath }
                catch {
                    $failure = $_.Exception.Message
                    if ($attempt -lt $TokenAttempts) { Start-Sleep -Seconds (2 * $attempt); continue }
                    if ($TokenAttempts -gt 1) { $failure = "$failure (after $TokenAttempts attempts)" }
                    throw
                }
            }
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
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -InputText $inputText -Read:($Method -ceq 'GET') `
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
        '--json', 'state,headRefOid,headRefName,baseRefName,baseRefOid,mergeStateStatus,isCrossRepository,mergeCommit,isDraft,author')
    $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -Read
    try { $pr = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop }
    catch { throw "HTTP unknown $path" }
    if ($pr.isCrossRepository) { throw "Repository mismatch. Expected $($Context.owner)/$($Context.repo)." }
    Assert-GhAppBranch $pr.headRefName
    $null = Assert-GhAppSha "head of #$Number" $pr.headRefOid
    $pr
}

function Test-GhAppBotAuthor {
    param([hashtable]$Context, $Author)
    # REST reports the App as "<name>[bot]". gh pr view reports it as "app/<name>" with is_bot set.
    if ($null -eq $Author) { return $false }
    if ([string]$Author.login -ceq $Context.botLogin) { return $true }
    $appLogin = 'app/' + ($Context.botLogin -creplace '\[bot\]\z', '')
    return ($Author.is_bot -eq $true -and [string]$Author.login -ceq $appLogin)
}

function Get-GhAppLedgerPath {
    param([hashtable]$Context, [string]$LedgerPath)
    if (-not $LedgerPath) {
        $LedgerPath = Join-Path (Split-Path (Get-GhAppConfigPath $Context.owner $Context.repo)) 'review-ledger.jsonl'
    }
    $LedgerPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LedgerPath)
    if (Test-GhAppRepositoryPath $LedgerPath) { throw 'The review ledger must be outside every repository.' }
    foreach ($protected in @((Get-GhAppConfigPath $Context.owner $Context.repo), $Context.tokenHelperPath, $Context.dpapiPath)) {
        if ([string]::Equals($LedgerPath, [IO.Path]::GetFullPath($protected), [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The review ledger must not name a config, helper, or DPAPI file.'
        }
    }
    $LedgerPath
}

function Request-GhAppOwnerReview {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Number,
        [string]$LedgerPath, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $pr = Get-GhAppPullRequest $context $Number
    if ($pr.state -cne 'OPEN' -or $pr.isDraft) { throw "#$Number must be open and not a draft before requesting owner review." }
    if (-not (Test-GhAppBotAuthor $context $pr.author)) { throw "#$Number PR author does not match botLogin." }
    Assert-GhAppBranch $pr.baseRefName
    $entry = [pscustomobject][ordered]@{ owner = $context.owner; repo = $context.repo; number = $Number;
        head = (Assert-GhAppSha "head of #$Number" $pr.headRefOid);
        requestedAt = (Get-GhAppUtcNow).ToUniversalTime().ToString('o'); base = $pr.baseRefName }
    try {
        $line = ConvertTo-Json -InputObject $entry -Compress
        [IO.File]::AppendAllText($LedgerPath, $line + "`n", [Text.UTF8Encoding]::new($false))
    } catch { throw 'The review ledger could not be appended.' }
    $entry
}

function Get-GhAppReviewRequest {
    param([hashtable]$Context, [int]$Number, [string]$LedgerPath)
    $latest = $null
    if ([IO.File]::Exists($LedgerPath)) {
        $reader = $null
        try {
            $reader = [IO.StreamReader]::new($LedgerPath, [Text.UTF8Encoding]::new($false, $true))
            while ($null -ne ($line = $reader.ReadLine())) {
                # Keep timestamps as JSON strings on every supported PowerShell version.
                $document = [Text.Json.JsonDocument]::Parse($line)
                try {
                    $entry = $document.RootElement
                    if ($entry.GetProperty('owner').GetString() -cne $Context.owner -or
                        $entry.GetProperty('repo').GetString() -cne $Context.repo -or $entry.GetProperty('number').GetInt32() -ne $Number) { continue }
                    $head = Assert-GhAppSha 'review request head' $entry.GetProperty('head').GetString()
                    $base = $entry.GetProperty('base').GetString()
                    Assert-GhAppBranch $base
                    $stamp = $entry.GetProperty('requestedAt').GetString()
                    if ($stamp -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$') { throw 'Expected UTC ISO 8601.' }
                    $time = [DateTimeOffset]::Parse($stamp, [Globalization.CultureInfo]::InvariantCulture)
                    $latest = [pscustomobject]@{ Head = $head; RequestedAt = $time; Base = $base }
                } finally { $document.Dispose() }
            }
        } catch { throw 'The review ledger contains an invalid entry or could not be read.' }
        finally { if ($null -ne $reader) { $reader.Dispose() } }
    }
    if ($null -eq $latest) { throw "#$Number stop: no review ledger entry; send the head to the owner and record a review request." }
    $latest
}

function Get-GhAppOwnerApproval {
    param([hashtable]$Context, [int]$Number, [DateTimeOffset]$RequestedAt)
    # A COMMENTED review does not change approval on GitHub, so only state-changing reviews count.
    $reviews = @(Get-GhAppItems $Context "pulls/$Number/reviews" | Where-Object {
        $_.user.login -ieq $Context.owner -and $_.state -cin @('APPROVED', 'CHANGES_REQUESTED', 'DISMISSED', 'PENDING') })
    if ($reviews.Count -eq 0) { return $null }
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
    $latest = $ranked | Sort-Object -Property Time, Id | Select-Object -Last 1
    if ($latest.Review.state -ceq 'APPROVED' -and $latest.Time -gt $RequestedAt) { return $latest.Review }
    return $null
}

function Invoke-GhAppGit {
    param([string]$Worktree, [string[]]$Arguments, [switch]$Bytes, [switch]$OwnerCredentials,
        [int[]]$AllowedExitCodes = @(0), [switch]$Result)
    $process = $null
    $buffer = [IO.MemoryStream]::new()
    try {
        $Worktree = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Worktree)
        $executable = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $info = New-GhAppProcessInfo $executable (@('--no-replace-objects', '-C', $Worktree) + $Arguments)
        # Fetch uses the owner's normal Git credential setup, never an App token.
        if ($OwnerCredentials) {
            foreach ($item in Get-ChildItem Env:) {
                if ($item.Name -match '^(GCM_|GIT_CONFIG_)' -or $item.Name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')) {
                    $info.Environment[$item.Name] = $item.Value
                }
            }
        }
        foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_COMMON_DIR', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES')) {
            [void]$info.Environment.Remove($name)
        }
        $process = [Diagnostics.Process]::Start($info)
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.BaseStream.CopyToAsync($buffer)
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -notin $AllowedExitCodes) { throw 'Local git command failed.' }
        if ($Result) { return [pscustomobject]@{ Output = [Text.Encoding]::UTF8.GetString($buffer.ToArray()); ExitCode = $process.ExitCode } }
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
        [switch]$IncludeWorkflowsWrite, [string]$WorkflowsLogPath,
        [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    $parents = @(Assert-GhAppSha 'RemoteParent' $RemoteParent)
    $LocalBase = Assert-GhAppSha 'LocalBase' $LocalBase
    if ($ExtraParent) { $parents += Assert-GhAppSha 'ExtraParent' $ExtraParent }
    Assert-GhAppBranch $Branch
    $message = Read-GhAppBody $MessageFile
    # The diff from LocalBase to HEAD becomes the remote commit. A LocalBase that HEAD does not contain would turn
    # every newer base file into a deletion, so require HEAD to be built on it.
    $ancestor = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-base', '--is-ancestor', $LocalBase, 'HEAD') -AllowedExitCodes @(0, 1) -Result
    if ($ancestor.ExitCode -ne 0) { throw "LocalBase $LocalBase is not an ancestor of HEAD; rebase onto the remote base first." }
    if ($IncludeWorkflowsWrite) {
        # The switch is off by default. It is allowed only for a push whose tree changes a workflow file.
        if ([string]::IsNullOrWhiteSpace($WorkflowsLogPath)) { throw 'IncludeWorkflowsWrite requires WorkflowsLogPath.' }
        $changes = @(Get-GhAppTreeChanges $Worktree $LocalBase)
        $workflowFiles = @($changes | Where-Object { $_.Path.StartsWith('.github/workflows/', [StringComparison]::Ordinal) })
        if ($workflowFiles.Count -eq 0) { throw 'IncludeWorkflowsWrite refused: the push changes no file under .github/workflows/.' }
        $logPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkflowsLogPath)
        $line = "$([DateTimeOffset]::UtcNow.ToString('o')) workflows-write push branch=$Branch files=$($changes.Count) workflowFiles=$($workflowFiles.Count)"
        try { [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
        catch { throw 'The workflows write log could not be written.' }
        # A copy carries the flag, so the stored configuration and every other call stay unchanged.
        $context = $context.Clone()
        $context.includeWorkflowsWrite = $true
    }
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

function Sync-GhAppReviewCommits {
    param([hashtable]$Context, [string]$Worktree, [string[]]$Commits)
    if ((Invoke-GhAppGit $Worktree @('rev-parse', '--is-inside-work-tree')).Trim() -cne 'true') {
        throw 'Worktree must name a local Git clone.'
    }
    if ((Invoke-GhAppGit $Worktree @('rev-parse', '--is-shallow-repository')).Trim() -cne 'false') {
        throw 'Review proof requires a complete clone; shallow history is refused.'
    }
    $url = "https://github.com/$($Context.owner)/$($Context.repo).git"
    $null = Invoke-GhAppGit -Worktree $Worktree -OwnerCredentials -Arguments (@('-c', 'fetch.writeCommitGraph=false',
        'fetch', '--no-tags', '--no-prune', '--refmap=', '--no-write-fetch-head', '--no-auto-maintenance',
        '--recurse-submodules=no', $url) + @($Commits | Select-Object -Unique))
}

function Assert-GhAppReviewHistory {
    param([hashtable]$Context, [int]$Number, [string]$Worktree, $Pr, $Request)
    $head = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
    if ($Pr.baseRefName -cne $Request.Base) { throw "#$Number stop: review request base changed; owner approval and a new review request required." }
    # Returns the proven chain from the current head back to the review request head, both included.
    if ($head -ceq $Request.Head) { return ,@($head) }
    $base = Assert-GhAppSha "base of #$Number" $Pr.baseRefOid
    Sync-GhAppReviewCommits $Context $Worktree @($head, $Request.Head, $base)
    $version = Invoke-GhAppGit $Worktree @('--version')
    if ($version -notmatch '^git version (\d+)\.(\d+)' -or [version]"$($Matches[1]).$($Matches[2])" -lt [version]'2.38') {
        throw "#$Number stop: clean merge proof needs Git 2.38 or later for merge-tree --write-tree."
    }
    $chain = [Collections.Generic.List[string]]::new()
    $current = $head
    for ($count = 0; $current -cne $Request.Head; $count++) {
        $chain.Add($current)
        if ($count -ge 20) { throw "#$Number stop: review request $($Request.Head) was not reached within 20 first-parent commits from $head." }
        $parents = (Invoke-GhAppGit $Worktree @('rev-list', '--parents', '-n', '1', $current)).Trim().Split(' ')
        if ($parents.Length -ne 3) {
            throw "#$Number stop: head $head adds commits after review request $($Request.Head); owner approval and a new review request required."
        }
        $first = Assert-GhAppSha 'first parent' $parents[1]
        $second = Assert-GhAppSha 'second parent' $parents[2]
        $contained = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-base', '--is-ancestor', $second, $base) -AllowedExitCodes @(0, 1) -Result
        if ($contained.ExitCode -ne 0) { throw "#$Number stop: second parent $second is not contained in $($Pr.baseRefName); owner approval and a new review request required." }
        # Every merge is recomputed. A blob comparison alone would accept a conflict resolved
        # wholly to either parent, so no shortcut is taken.
        # merge-tree writes only objects: it leaves the index, worktree, and refs untouched.
        $result = Invoke-GhAppGit -Worktree $Worktree -Arguments @('merge-tree', '--write-tree', $first, $second) -AllowedExitCodes @(0, 1) -Result
        if ($result.ExitCode -ne 0) { throw "#$Number stop: recomputed merge conflicts; manual resolution requires owner approval." }
        $tree = Assert-GhAppSha 'recomputed merge tree' ($result.Output.Split("`n")[0].Trim())
        $actual = (Invoke-GhAppGit $Worktree @('rev-parse', '--verify', "$current^{tree}")).Trim()
        if ($tree -cne $actual) { throw "#$Number stop: merge tree differs from the clean automatic merge; manual resolution requires owner approval." }
        $current = $first
    }
    $chain.Add($Request.Head)
    return ,$chain.ToArray()
}

function Get-GhAppTimelineObservation {
    param([hashtable]$Context, [int]$Number, $Approval)
    # Timeline order is an observation only. Commit timestamps can predate a push.
    $query = 'query($owner:String!,$repo:String!,$number:Int!,$cursor:String){repository(owner:$owner,name:$repo){pullRequest(number:$number){timelineItems(first:100,after:$cursor,itemTypes:[PULL_REQUEST_COMMIT,PULL_REQUEST_REVIEW]){nodes{__typename ... on PullRequestCommit{commit{oid}} ... on PullRequestReview{fullDatabaseId}} pageInfo{hasNextPage endCursor}}}}}'
    $cursor = $null
    $lastCommit = 'none'
    try {
        for ($page = 0; $page -lt 1000; $page++) {
            $body = @{ query = $query; variables = @{ owner = $Context.owner; repo = $Context.repo; number = $Number; cursor = $cursor } }
            $result = Invoke-GhAppGh -Context $Context -Read -Arguments @('api', 'graphql', '--hostname', 'github.com', '--method', 'POST', '--input', '-') `
                -ApiPath '/graphql' -InputText (ConvertTo-Json $body -Depth 10 -Compress)
            $data = ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop
            if ($data.PSObject.Properties['errors']) { return 'unavailable' }
            $timeline = $data.data.repository.pullRequest.timelineItems
            foreach ($node in $timeline.nodes) {
                if ($node.__typename -ceq 'PullRequestCommit') { $lastCommit = Assert-GhAppSha 'timeline commit' $node.commit.oid }
                elseif ($node.__typename -ceq 'PullRequestReview' -and [string]$node.fullDatabaseId -ceq [string]$Approval.id) { return $lastCommit }
            }
            if (-not $timeline.pageInfo.hasNextPage) { return 'unavailable' }
            $cursor = $timeline.pageInfo.endCursor
        }
    } catch { return 'unavailable' }
    return 'unavailable'
}

function Get-GhAppApprovedReviewRequest {
    param([hashtable]$Context, [int]$Number, [string]$Worktree, $Pr, [string]$LedgerPath)
    $request = Get-GhAppReviewRequest $Context $Number $LedgerPath
    $approval = Get-GhAppOwnerApproval $Context $Number $request.RequestedAt
    if ($null -eq $approval) { throw "#$Number stop: no owner approval after review request $($request.Head)." }
    $chain = Assert-GhAppReviewHistory $Context $Number $Worktree $Pr $request
    # The ledger time comes from this machine's clock. Binding the approval to the proven chain means a
    # skewed clock can never let an approval of an older head count for the request.
    if ([string]$approval.commit_id -cnotin $chain) {
        throw "#$Number stop: owner approval is on $($approval.commit_id), not on review request $($request.Head) or a clean base merge after it."
    }
    [pscustomobject]@{ Request = $request; Approval = $approval }
}

function Wait-GhAppApprovedHead {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$Worktree, [string]$LedgerPath, [int]$TimeoutSeconds, [int]$PollSeconds)
    $deadline = (Get-GhAppUtcNow).AddSeconds($TimeoutSeconds)
    $updates = 0
    $updatingHead = $null
    while ($true) {
        if ($Pr.state -cne 'OPEN') { throw "#$Number stop: state $($Pr.state)." }
        if ($Pr.isDraft -or -not (Test-GhAppBotAuthor $Context $Pr.author)) { throw "#$Number stop: PR must be a non-draft authored by botLogin." }
        $current = Assert-GhAppSha "head of #$Number" $Pr.headRefOid
        if ($Pr.mergeStateStatus -ceq 'DIRTY') { throw "#$Number stop: merge conflict with $($Pr.baseRefName); manual resolution requires owner approval." }
        if ((Get-GhAppUtcNow) -ge $deadline) { throw "#$Number stop: timed out in state $($Pr.mergeStateStatus)." }
        # While an update is pending the head has not moved, so skip the proof until it does.
        # A PR that is no longer behind needs no new head, so stop waiting for one.
        if ($updatingHead -ceq $current -and $Pr.mergeStateStatus -ceq 'BEHIND') {
            Start-Sleep -Seconds $PollSeconds
            $Pr = Get-GhAppPullRequest $Context $Number
            continue
        }
        $updatingHead = $null
        $null = Get-GhAppApprovedReviewRequest $Context $Number $Worktree $Pr $LedgerPath
        if ($updates -ge 3 -and $Pr.mergeStateStatus -ceq 'BEHIND') { throw "#$Number stop: still behind $($Pr.baseRefName) after three updates." }
        if ($Pr.mergeStateStatus -ceq 'BEHIND') {
            try { $null = Invoke-GhAppApi $Context PUT "pulls/$Number/update-branch" @{ expected_head_sha = $current } -AsApp }
            catch { throw "#$Number stop: branch update failed; manual resolution requires owner approval." }
            $updates++
            $updatingHead = $current
            # The update is asynchronous. Poll readiness and prove any new head before checking it.
            Start-Sleep -Seconds $PollSeconds
            $Pr = Get-GhAppPullRequest $Context $Number
            continue
        }
        $path = "/repos/$($Context.owner)/$($Context.repo)/pulls/$Number/checks"
        $arguments = @('pr', 'checks', [string]$Number, '--repo', "$($Context.owner)/$($Context.repo)", '--required', '--json', 'name,bucket,state')
        $result = Invoke-GhAppGh -Context $Context -Arguments $arguments -ApiPath $path -AllowedExitCodes @(0, 1, 8) -Read
        try { $checks = @(ConvertFrom-Json -InputObject $result.Output -ErrorAction Stop) }
        catch { throw "HTTP unknown $path" }
        if (@($checks | Where-Object { $_.bucket -in @('fail', 'cancel') }).Count -gt 0) { throw "#$Number stop: required checks failed." }
        $pending = @($checks | Where-Object { $_.bucket -notin @('pass', 'skipping') }).Count -gt 0
        if (-not $pending -and $result.ExitCode -eq 0 -and $Pr.mergeStateStatus -ceq 'CLEAN') {
            # Recheck both the head and the latest owner review after checking CI.
            $fresh = Get-GhAppPullRequest $Context $Number
            if ($fresh.headRefOid -ceq $current -and $fresh.baseRefOid -ceq $Pr.baseRefOid -and
                $fresh.baseRefName -ceq $Pr.baseRefName -and $fresh.state -ceq 'OPEN' -and $fresh.mergeStateStatus -ceq 'CLEAN') {
                if ($fresh.isDraft -or -not (Test-GhAppBotAuthor $Context $fresh.author)) { throw "#$Number stop: PR must be a non-draft authored by botLogin." }
                $evidence = Get-GhAppApprovedReviewRequest $Context $Number $Worktree $fresh $LedgerPath
                return [pscustomobject]@{ Pr = $fresh; Request = $evidence.Request; Approval = $evidence.Approval }
            }
            $Pr = $fresh
            continue
        }
        Start-Sleep -Seconds $PollSeconds
        $Pr = Get-GhAppPullRequest $Context $Number
    }
}

function Remove-GhAppMergedBranch {
    param([hashtable]$Context, [int]$Number, $Pr, [string]$Head, [string]$LogPath, [string]$Prefix)
    $branch = $Pr.headRefName
    $base = $Pr.baseRefName
    $encoded = [Uri]::EscapeDataString($branch)
    # Deletion is opt-in by prefix, so release and other long-lived branches are never deleted.
    if (-not $Prefix -or -not $branch.StartsWith($Prefix, [StringComparison]::Ordinal)) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; no matching delete prefix"
        return
    }
    if ($branch -ceq 'main' -or $branch -ceq $base) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; base branch"
        return
    }
    $ref = Invoke-GhAppApi $Context GET "git/ref/heads/$encoded" -AllowNotFound
    if ($null -eq $ref -or $ref.object.sha -cne $Head) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; ref changed or absent"
        return
    }
    $comparison = Invoke-GhAppApi $Context GET "compare/$([Uri]::EscapeDataString($base))...$Head"
    if ($comparison.status -notin @('behind', 'identical') -or $comparison.ahead_by -ne 0) {
        Write-GhAppMergeLog $LogPath "#$Number kept branch $branch; $base does not contain $Head"
        return
    }
    $null = Invoke-GhAppApi $Context DELETE "git/refs/heads/$encoded" -AsApp
    Write-GhAppMergeLog $LogPath "#$Number deleted branch $branch at $Head"
}

function Merge-GhAppApprovedPullRequest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int[]]$Numbers,
        [Parameter(Mandatory)][string]$Worktree, [Parameter(Mandatory)][Alias('Log')][string]$LogPath,
        [string]$LedgerPath, [ValidateRange(1, 3600)][int]$TimeoutSeconds = 1200,
        [ValidateRange(1, 60)][int]$PollSeconds = 15, [string]$DeleteBranchPrefix, [Alias('Repo')][string]$Repository)
    $context = Get-GhAppContext $Repository
    if ($PSBoundParameters.ContainsKey('DeleteBranchPrefix') -and [string]::IsNullOrWhiteSpace($DeleteBranchPrefix)) {
        throw 'DeleteBranchPrefix must be nonempty when given.'
    }
    $LedgerPath = Get-GhAppLedgerPath $context $LedgerPath
    $LogPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogPath)
    foreach ($number in $Numbers) {
        try {
            Write-GhAppMergeLog $LogPath "#$number start"
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'OPEN') { Write-GhAppMergeLog $LogPath "#$number skip: state $($pr.state)"; continue }
            $evidence = Wait-GhAppApprovedHead $context $number $pr $Worktree $LedgerPath $TimeoutSeconds $PollSeconds
            $pr = $evidence.Pr
            $head = Assert-GhAppSha "head of #$number" $pr.headRefOid
            $merged = Invoke-GhAppApi $context PUT "pulls/$number/merge" @{ merge_method = 'merge'; sha = $head } -AsApp
            if (-not $merged.merged) { throw "#$number stop: merge failed." }
            $mergeSha = Assert-GhAppSha "merge commit of #$number" $merged.sha
            $pr = Get-GhAppPullRequest $context $number
            if ($pr.state -cne 'MERGED') { throw "#$number stop: merge was not confirmed." }
            Write-GhAppMergeLog $LogPath "#$number merged $head -> $mergeSha"
            $approvedAt = ([DateTimeOffset]$evidence.Approval.submitted_at).ToUniversalTime().ToString('o')
            Write-GhAppMergeLog $LogPath "#$number review requested at $($evidence.Request.Head); approved $approvedAt; merged head $head; differences only from $($evidence.Request.Base)"
            $observation = Get-GhAppTimelineObservation $context $number $evidence.Approval
            Write-GhAppMergeLog $LogPath "#$number timeline observation only: last PullRequestCommit before approval $observation"
            Remove-GhAppMergedBranch $context $number $pr $head $LogPath $DeleteBranchPrefix
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
    $result = Invoke-GhAppGh -Context $context -Arguments $safeArguments -ApiPath $path -Read
    $result.Output.TrimEnd("`r", "`n")
}

Export-ModuleMember -Function Import-GhAppConfig, Push-GhAppBranch, New-GhAppPullRequest,
    Set-GhAppPullRequestBody, Add-GhAppComment, Add-GhAppReviewRecord, Request-GhAppOwnerReview,
    Merge-GhAppApprovedPullRequest, Close-GhAppPullRequest, Invoke-GhAppRead
