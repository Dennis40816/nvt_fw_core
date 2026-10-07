# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
$credentialNames = @(Get-ChildItem Env: | Where-Object {
    $_.Name -match '^(GH_|GITHUB_|GCM_|BW_|GIT_CONFIG_)' -or $_.Name -in @('GIT_ASKPASS', 'SSH_ASKPASS', 'GIT_TERMINAL_PROMPT')
})
if ($credentialNames.Count -gt 0 -or -not $env:NVT_GHAPP_TEST_ROOT) { throw 'Run tests with Invoke-GhAppTests.ps1 in its isolated process.' }
. "$PSScriptRoot/New-FakeGh.ps1"
$fakeBin = Join-Path $env:NVT_GHAPP_TEST_ROOT 'bin'
New-FakeGh $fakeBin
$env:PATH = $fakeBin + [IO.Path]::PathSeparator + $env:PATH
if ((Get-Command gh -CommandType Application | Select-Object -First 1).Source -cne (Join-Path $fakeBin 'gh.exe')) { throw 'PATH does not select the offline gh executable.' }
Import-Module "$PSScriptRoot/../NvtGhApp.psd1" -Force

InModuleScope NvtGhApp {
    $script:FixtureRoot = Join-Path $env:NVT_GHAPP_TEST_ROOT 'fixtures'
    $null = New-Item -ItemType Directory -Force -Path $script:FixtureRoot
    $script:FixtureConfig = Join-Path $script:FixtureRoot 'example-owner-example-repo.json'
    $script:BodyFile = Join-Path $script:FixtureRoot 'body.txt'
    $script:LogFile = Join-Path $script:FixtureRoot 'merge.log'
    $script:LedgerFile = Join-Path $script:FixtureRoot 'review-ledger.jsonl'
    $script:HeadSha = 'a' * 40
    $script:NextSha = 'b' * 40
    $script:MergeSha = 'c' * 40
    $script:Owner = 'example-owner'
    $script:RepoName = 'example-repo'
    $script:RepositoryName = "$script:Owner/$script:RepoName"
    $env:NVT_GHAPP_CALLS = Join-Path $script:FixtureRoot 'calls.jsonl'
    $env:NVT_GHAPP_RESPONSES = Join-Path $script:FixtureRoot 'responses.json'
    $env:NVT_GHAPP_INDEX = Join-Path $script:FixtureRoot 'index.txt'
    $env:NVT_GHAPP_HELPER_CALLS = Join-Path $script:FixtureRoot 'helper.jsonl'

    function Set-Responses {
        param([object[]]$Steps)
        [IO.File]::WriteAllText($env:NVT_GHAPP_RESPONSES, (ConvertTo-Json -InputObject @{ Steps = @($Steps) } -Depth 40 -Compress))
    }
    function Get-Calls {
        if ([IO.File]::Exists($env:NVT_GHAPP_CALLS)) {
            Get-Content -LiteralPath $env:NVT_GHAPP_CALLS | ForEach-Object { ConvertFrom-Json $_ }
        }
    }
    function Get-Message {
        param([scriptblock]$Action)
        try { $null = & $Action; return 'NO ERROR' } catch { return $_.Exception.Message }
    }
    function New-ApiStep {
        param([string]$Suffix, [string]$Method = 'GET', $Value = @{}, [switch]$App,
            [int]$ExitCode = 0, [string]$ErrorText = '', [string]$RawOutput)
        $arguments = @('api', "/repos/$script:RepositoryName/$Suffix", '--hostname', 'github.com', '--method', $Method)
        if ($Method -notin @('GET', 'DELETE')) { $arguments += @('--input', '-') }
        $output = $(if ($PSBoundParameters.ContainsKey('RawOutput')) { $RawOutput } else { ConvertTo-Json -InputObject $Value -Depth 30 -Compress })
        @{ Arguments = $arguments; App = [bool]$App; Output = $output; Error = $ErrorText; ExitCode = $ExitCode }
    }
    function New-Pr {
        param([string]$Head = $script:HeadSha, [string]$MergeState = 'CLEAN', [string]$State = 'OPEN',
            [string]$Branch = 'test/module', [switch]$Fork, [switch]$Draft,
            [string]$Author = 'app/example-app', [string]$BaseHead = $script:NextSha)
        @{ state = $State; headRefOid = $Head; headRefName = $Branch; baseRefName = 'main';
            baseRefOid = $BaseHead; isDraft = [bool]$Draft; author = @{ login = $Author; is_bot = $Author.StartsWith('app/') -or $Author.EndsWith('[bot]') };
            mergeStateStatus = $MergeState; isCrossRepository = [bool]$Fork; mergeCommit = @{ oid = $script:MergeSha } }
    }
    function New-PrStep {
        param([int]$Number = 7, $Pr = (New-Pr))
        @{ Arguments = @('pr', 'view', [string]$Number, '--repo', $script:RepositoryName, '--json',
            'state,headRefOid,headRefName,baseRefName,baseRefOid,mergeStateStatus,isCrossRepository,mergeCommit,isDraft,author');
            App = $false; Output = (ConvertTo-Json $Pr -Compress -Depth 10); Error = ''; ExitCode = 0 }
    }
    function New-Review {
        param([string]$State = 'APPROVED', [string]$Head = $script:HeadSha, [int]$Id = 1,
            [string]$Login = $script:Owner, [string]$SubmittedAt = '2026-01-01T00:00:00Z')
        @{ id = $Id; state = $State; commit_id = $Head; submitted_at = $SubmittedAt; user = @{ login = $Login } }
    }
    function New-ReviewStep {
        param([int]$Number = 7, [object[]]$Reviews = @((New-Review)))
        New-ApiStep "pulls/$Number/reviews?per_page=100&page=1" -Value @($Reviews)
    }
    function New-ChecksStep {
        param([int]$Number = 7, [string]$Bucket = 'pass', [int]$ExitCode = 0)
        @{ Arguments = @('pr', 'checks', [string]$Number, '--repo', $script:RepositoryName,
            '--required', '--json', 'name,bucket,state'); App = $false;
            Output = (ConvertTo-Json -InputObject @(@{ name = 'required-ci'; bucket = $Bucket; state = 'COMPLETED' }) -Compress);
            Error = ''; ExitCode = $ExitCode }
    }
    function Reset-Fixture {
        foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX, $env:NVT_GHAPP_HELPER_CALLS, $script:LogFile, $script:LedgerFile)) {
            if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
        foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN', 'GH_REPO', 'GH_HOST', 'GH_DEBUG', 'NVT_GHAPP_HELPER_FAIL')) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        $helper = Join-Path $script:FixtureRoot 'helper.ps1'
        $helperSource = @'
param($Mode, $Owner, $Repo, $ClientId, $InstallationId, $DpapiPath)
$record = @{ Arguments = $PSBoundParameters; TokenPresent = [bool]$env:GH_TOKEN; DebugPresent = [bool]$env:GH_DEBUG }
[IO.File]::AppendAllText($env:NVT_GHAPP_HELPER_CALLS, (ConvertTo-Json $record -Compress) + "`n")
# NVT_GHAPP_HELPER_FAIL is the number of calls that fail before the helper succeeds.
if (@([IO.File]::ReadAllLines($env:NVT_GHAPP_HELPER_CALLS)).Count -le [int]$env:NVT_GHAPP_HELPER_FAIL) {
    [Console]::Error.Write('ghs_' + 'FAKE'); exit 1
}
[Console]::Out.Write('ghs_' + 'FAKE')
'@
        [IO.File]::WriteAllText($helper, $helperSource)
        $script:ConfigValues = @{ owner = $script:Owner; repo = $script:RepoName; clientId = 'CLIENT_ID_PLACEHOLDER';
            installationId = '1'; tokenHelperPath = $helper; dpapiPath = (Join-Path $script:FixtureRoot 'never-read.dpapi');
            botLogin = 'example-app[bot]' }
        [IO.File]::WriteAllText($script:FixtureConfig, (ConvertTo-Json $script:ConfigValues))
        [IO.File]::WriteAllText($script:BodyFile, "A synthetic description.`r`nSecond line.")
        Set-Responses @()
        $script:Configuration = $null
    }
    function Import-Fixture {
        Mock Get-GhAppConfigPath { $script:FixtureConfig }
        $null = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
    }
    function Add-LedgerFixture {
        param([int]$Number = 7, [string]$Head = $script:HeadSha,
            [string]$RequestedAt = '2025-12-31T23:00:00Z', [string]$Owner = $script:Owner, [string]$Repo = $script:RepoName)
        $entry = @{ owner = $Owner; repo = $Repo; number = $Number; head = $Head; requestedAt = $RequestedAt; base = 'main' }
        [IO.File]::AppendAllText($script:LedgerFile, (ConvertTo-Json $entry -Compress) + "`n", [Text.UTF8Encoding]::new($false))
    }
    function Assert-NoToken {
        param([string]$Output = '')
        $secret = 'ghs_' + 'FAKE'
        $Output.Contains($secret) | Should Be $false
        foreach ($file in @(Get-ChildItem -LiteralPath $env:NVT_GHAPP_TEST_ROOT -File -Recurse)) {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            [Text.Encoding]::UTF8.GetString($bytes).Contains($secret) | Should Be $false
            [Text.Encoding]::Unicode.GetString($bytes).Contains($secret) | Should Be $false
        }
        ($Error | Out-String).Contains($secret) | Should Be $false
    }

    Describe 'Configuration and repository scope' {
        BeforeEach { Reset-Fixture; Mock Get-GhAppConfigPath { $script:FixtureConfig } }
        It 'imports all fields without reading the DPAPI file or invoking gh' {
            $config = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            $config.botLogin | Should Be 'example-app[bot]'
            [IO.File]::Exists($config.dpapiPath) | Should Be $false
            @(Get-Calls).Count | Should Be 0
        }
        It 'does not allow a returned config object to redirect the active repository' {
            $config = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            $config.repo = 'other-repo'
            (Get-GhAppContext '').repo | Should Be $script:RepoName
        }
        It 'requires a successful import' {
            Get-Message { Add-GhAppComment -Number 7 -BodyFile $script:BodyFile } | Should Be 'Import-GhAppConfig must succeed before using this module.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a missing config file' {
            [IO.File]::Delete($script:FixtureConfig)
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be 'The repository config file is missing.'
            @(Get-Calls).Count | Should Be 0
        }
        foreach ($fieldName in @('owner', 'repo', 'clientId', 'installationId', 'tokenHelperPath', 'dpapiPath', 'botLogin')) {
            It "rejects the missing $fieldName field before gh" {
                $script:ConfigValues.Remove($fieldName)
                [IO.File]::WriteAllText($script:FixtureConfig, (ConvertTo-Json $script:ConfigValues))
                Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Config field '$fieldName' is required."
                @(Get-Calls).Count | Should Be 0
            }
        }
        It 'rejects a config repository mismatch' {
            $script:ConfigValues.repo = 'other-repo'
            [IO.File]::WriteAllText($script:FixtureConfig, (ConvertTo-Json $script:ConfigValues))
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Repository mismatch. Expected $script:RepositoryName."
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a config stored inside a worktree' {
            $marker = Join-Path $script:FixtureRoot '.git'
            [IO.File]::WriteAllText($marker, 'synthetic worktree marker')
            try {
                Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be 'The config file must be outside every repository.'
            } finally { [IO.File]::Delete($marker) }
            @(Get-Calls).Count | Should Be 0
        }
        foreach ($pathField in @('tokenHelperPath', 'dpapiPath')) {
            It "rejects a $pathField inside a repository" {
                $repository = Join-Path $script:FixtureRoot "repo-$pathField"
                [void][IO.Directory]::CreateDirectory((Join-Path $repository '.git'))
                $inside = Join-Path $repository 'file.ps1'
                [IO.File]::Copy($script:ConfigValues.tokenHelperPath, $inside, $true)
                $script:ConfigValues[$pathField] = $inside
                [IO.File]::WriteAllText($script:FixtureConfig, (ConvertTo-Json $script:ConfigValues))
                try {
                    Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Config field '$pathField' must be outside every repository."
                } finally { [IO.Directory]::Delete($repository, $true) }
                @(Get-Calls).Count | Should Be 0
            }
        }
        It 'rejects malformed JSON and clears any earlier configuration' {
            $null = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            [IO.File]::WriteAllText($script:FixtureConfig, '{')
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be 'The repository config file must contain a JSON object.'
            Get-Message { Get-GhAppContext '' } | Should Be 'Import-GhAppConfig must succeed before using this module.'
        }
        foreach ($publicFunction in @('Push-GhAppBranch', 'New-GhAppPullRequest', 'Set-GhAppPullRequestBody', 'Add-GhAppComment',
            'Add-GhAppReviewRecord', 'Request-GhAppOwnerReview', 'Merge-GhAppApprovedPullRequest', 'Close-GhAppPullRequest', 'Invoke-GhAppRead')) {
            It "rejects another repository in $publicFunction before gh" {
                Import-Fixture
                $parameters = @{ Repository = 'other-owner/other-repo' }
                switch ($publicFunction) {
                    'Push-GhAppBranch' { $parameters += @{ Worktree = $script:FixtureRoot; LocalBase = $script:HeadSha; RemoteParent = $script:HeadSha; Branch = 'test/module'; MessageFile = $script:BodyFile } }
                    'New-GhAppPullRequest' { $parameters += @{ Head = 'test/module'; Title = 'Synthetic title'; BodyFile = $script:BodyFile } }
                    'Merge-GhAppApprovedPullRequest' { $parameters += @{ Numbers = @(7); Worktree = $script:FixtureRoot; LogPath = $script:LogFile } }
                    'Request-GhAppOwnerReview' { $parameters += @{ Number = 7 } }
                    'Invoke-GhAppRead' { $parameters += @{ Arguments = @('pr', 'list') } }
                    'Close-GhAppPullRequest' { $parameters += @{ Number = 7; CommentFile = $script:BodyFile } }
                    default { $parameters += @{ Number = 7; BodyFile = $script:BodyFile }; if ($publicFunction -eq 'Add-GhAppReviewRecord') { $parameters.ExpectedHeadSha = $script:HeadSha } }
                }
                Get-Message { & $publicFunction @parameters } | Should Be "Repository mismatch. Expected $script:RepositoryName."
                @(Get-Calls).Count | Should Be 0
                [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
            }
        }
        It 'rejects an environment repository mismatch before gh' {
            Import-Fixture
            $env:GH_REPO = 'other-owner/other-repo'
            Get-Message { Add-GhAppComment 7 $script:BodyFile } | Should Be "Repository mismatch. Expected $script:RepositoryName."
            @(Get-Calls).Count | Should Be 0
        }
    }

    Describe 'Pull request and issue writes' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'opens a draft PR and reads back its App author' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'example-app[bot]' } }))
            $result = New-GhAppPullRequest -Head test/module -Title 'Synthetic title' -BodyFile $script:BodyFile -Draft
            $result.Url | Should Be "https://github.com/$script:RepositoryName/pull/7"
            $calls = @(Get-Calls)
            $body = ConvertFrom-Json $calls[0].Input
            $body.draft | Should Be $true
            $body.base | Should Be main
            $body.body | Should Be "A synthetic description.`nSecond line."
            $calls[1].AppToken | Should Be $false
            Assert-NoToken ($result | Out-String)
        }
        It 'reports the full URL for an incorrect author without closing the PR' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'other-author' } }))
            Get-Message { New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile } |
                Should Be "PR author does not match botLogin. Inspect https://github.com/$script:RepositoryName/pull/7"
            @(Get-Calls).Count | Should Be 2
            Assert-NoToken
        }
        It 'replaces a PR body from a file through the App' {
            Set-Responses @((New-ApiStep pulls/7 PATCH @{ number = 7 } -App))
            $null = Set-GhAppPullRequestBody 7 $script:BodyFile
            (ConvertFrom-Json @(Get-Calls)[0].Input).body | Should Be "A synthetic description.`nSecond line."
            Assert-NoToken
        }
        It 'comments on an issue through the App' {
            Set-Responses @((New-ApiStep issues/9/comments POST @{ id = 1 } -App))
            $null = Add-GhAppComment 9 $script:BodyFile
            @(Get-Calls)[0].AppToken | Should Be $true
            Assert-NoToken
        }
        It 'reads a relative body file from the current PowerShell location' {
            Set-Responses @((New-ApiStep issues/7/comments POST @{} -App))
            Push-Location -LiteralPath $script:FixtureRoot
            try { $null = Add-GhAppComment 7 '.\body.txt' } finally { Pop-Location }
            (ConvertFrom-Json @(Get-Calls)[0].Input).body | Should Be "A synthetic description.`nSecond line."
        }
        foreach ($functionName in @('New-GhAppPullRequest', 'Set-GhAppPullRequestBody', 'Add-GhAppComment')) {
            It "rejects a missing body file for $functionName before gh" {
                $parameters = @{ BodyFile = (Join-Path $script:FixtureRoot 'missing.txt') }
                if ($functionName -eq 'New-GhAppPullRequest') { $parameters += @{ Head = 'test/module'; Title = 'title' } }
                else { $parameters.Number = 7 }
                Get-Message { & $functionName @parameters } | Should Be 'The body or message file is missing.'
                @(Get-Calls).Count | Should Be 0
            }
        }
        It 'rejects a fork head before creation' {
            Get-Message { New-GhAppPullRequest -Head 'other-owner:branch' -Title title -BodyFile $script:BodyFile } |
                Should Be 'Branch must be a valid local repository branch name.'
            @(Get-Calls).Count | Should Be 0
        }
    }

    Describe 'Owner review request ledger' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'appends one UTF-8 object per request beside the config without a GitHub write' {
            Set-Responses @((New-PrStep), (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            $first = Request-GhAppOwnerReview -Number 7
            $firstBytes = [IO.File]::ReadAllBytes($script:LedgerFile)
            $second = Request-GhAppOwnerReview -Number 7
            $lines = [IO.File]::ReadAllLines($script:LedgerFile)
            $lines.Count | Should Be 2
            ($first.PSObject.Properties.Name -join ',') | Should Be 'owner,repo,number,head,requestedAt,base'
            $first.owner | Should Be $script:Owner
            $first.repo | Should Be $script:RepoName
            $first.number | Should Be 7
            $first.head | Should Be $script:HeadSha
            $first.base | Should Be main
            $first.requestedAt | Should Match '^\d{4}-\d{2}-\d{2}T.*\+00:00$'
            $second.head | Should Be $script:NextSha
            $bytes = [IO.File]::ReadAllBytes($script:LedgerFile)
            $bytes[0] | Should Be 123
            [Convert]::ToBase64String($bytes[0..($firstBytes.Length - 1)]) | Should Be ([Convert]::ToBase64String($firstBytes))
            foreach ($line in $lines) { (ConvertFrom-Json $line).number | Should Be 7 }
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
        }
        It 'supports a caller-selected external ledger' {
            $path = Join-Path $script:FixtureRoot 'custom-ledger.jsonl'
            Set-Responses @((New-PrStep))
            try {
                $entry = Request-GhAppOwnerReview 7 -LedgerPath $path
                (ConvertFrom-Json ([IO.File]::ReadAllText($path))).head | Should Be $entry.head
                [IO.File]::Exists($script:LedgerFile) | Should Be $false
            } finally { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
        }
        foreach ($markerType in @('directory', 'file')) {
            It "refuses a ledger below a Git $markerType marker before reading the PR" {
                $directory = Join-Path $script:FixtureRoot "ledger-repo-$markerType"
                [void][IO.Directory]::CreateDirectory($directory)
                $marker = Join-Path $directory '.git'
                if ($markerType -eq 'directory') { [void][IO.Directory]::CreateDirectory($marker) }
                else { [IO.File]::WriteAllText($marker, 'synthetic worktree marker') }
                try {
                    Get-Message { Request-GhAppOwnerReview 7 -LedgerPath (Join-Path $directory 'private.jsonl') } |
                        Should Be 'The review ledger must be outside every repository.'
                    @(Get-Calls).Count | Should Be 0
                } finally { [IO.Directory]::Delete($directory, $true) }
            }
        }
        It 'refuses a ledger inside a bare repository' {
            $directory = Join-Path $script:FixtureRoot 'ledger-bare-repo'
            $null = & git init --bare --quiet $directory
            if ($LASTEXITCODE -ne 0) { throw 'The bare fixture could not be initialized.' }
            try {
                Get-Message { Request-GhAppOwnerReview 7 -LedgerPath (Join-Path $directory 'private.jsonl') } |
                    Should Be 'The review ledger must be outside every repository.'
                @(Get-Calls).Count | Should Be 0
            } finally { [IO.Directory]::Delete($directory, $true) }
        }
        It 'refuses a junction that points to a repository subdirectory' {
            $directory = Join-Path $script:FixtureRoot 'ledger-junction-repo'
            $alias = Join-Path $script:FixtureRoot 'ledger-junction'
            [void][IO.Directory]::CreateDirectory((Join-Path $directory '.git'))
            $target = Join-Path $directory 'nested'
            [void][IO.Directory]::CreateDirectory($target)
            $null = New-Item -ItemType Junction -Path $alias -Target $target
            try {
                Get-Message { Request-GhAppOwnerReview 7 -LedgerPath (Join-Path $alias 'private.jsonl') } |
                    Should Be 'The review ledger must be outside every repository.'
                @(Get-Calls).Count | Should Be 0
            } finally {
                # Delete the junction itself before removing its temporary target.
                [IO.Directory]::Delete($alias)
                [IO.Directory]::Delete($directory, $true)
            }
        }
        foreach ($login in @('app/example-app', 'example-app[bot]')) {
            It "records a review request for the App author form $login" {
                Set-Responses @((New-PrStep -Pr (New-Pr -Author $login)))
                $entry = Request-GhAppOwnerReview 7
                $entry.head | Should Be $script:HeadSha
                @(Get-Content -LiteralPath $script:LedgerFile).Count | Should Be 1
            }
        }
        foreach ($case in @(
            @{ Name = 'draft'; Pr = (New-Pr -Draft); Message = '#7 must be open and not a draft before requesting owner review.' },
            @{ Name = 'closed'; Pr = (New-Pr -State CLOSED); Message = '#7 must be open and not a draft before requesting owner review.' },
            @{ Name = 'non-bot'; Pr = (New-Pr -Author other-author); Message = '#7 PR author does not match botLogin.' },
            @{ Name = 'different App'; Pr = (New-Pr -Author 'app/other-app'); Message = '#7 PR author does not match botLogin.' },
            @{ Name = 'non-bot app-prefixed'; Pr = (& { $pr = New-Pr; $pr.author.is_bot = $false; $pr }); Message = '#7 PR author does not match botLogin.' },
            @{ Name = 'fork'; Pr = (New-Pr -Fork); Message = "Repository mismatch. Expected $script:RepositoryName." },
            @{ Name = 'invalid SHA'; Pr = (New-Pr -Head short); Message = 'head of #7 is not a 40-character SHA.' }
        )) {
            It "refuses a $($case.Name) PR without appending or invoking the helper" {
                Set-Responses @((New-PrStep -Pr $case.Pr))
                Get-Message { Request-GhAppOwnerReview 7 } | Should Be $case.Message
                [IO.File]::Exists($script:LedgerFile) | Should Be $false
                [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
            }
        }
    }

    Describe 'Review records' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'posts COMMENT with the exact checked commit_id' {
            Set-Responses @((New-PrStep), (New-ApiStep pulls/7/reviews POST @{ id = 1 } -App))
            $null = Add-GhAppReviewRecord -Number 7 -ExpectedHeadSha $script:HeadSha -BodyFile $script:BodyFile
            $body = ConvertFrom-Json @(Get-Calls)[1].Input
            $body.event | Should Be COMMENT
            $body.commit_id | Should Be $script:HeadSha
            Assert-NoToken
        }
        foreach ($eventName in @('APPROVE', 'REQUEST_CHANGES')) {
            It "rejects $eventName before any gh or helper call" {
                Get-Message { Add-GhAppReviewRecord 7 $script:HeadSha $script:BodyFile -Event $eventName } |
                    Should Be 'Review event must be COMMENT. Only the owner approves.'
                @(Get-Calls).Count | Should Be 0
                [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
            }
        }
        It 'rejects a stale expected head before posting' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Add-GhAppReviewRecord 7 $script:HeadSha $script:BodyFile } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        It 'rejects a short expected SHA before reading the PR' {
            Get-Message { Add-GhAppReviewRecord 7 bad $script:BodyFile } | Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a fork PR before posting' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Fork)))
            Get-Message { Add-GhAppReviewRecord 7 $script:HeadSha $script:BodyFile } | Should Be "Repository mismatch. Expected $script:RepositoryName."
            @(Get-Calls).Count | Should Be 1
        }
    }

    Describe 'Token confinement and sanitized failures' {
        BeforeEach { Reset-Fixture; Import-Fixture; Mock Start-Sleep {} }
        It 'redacts a token echoed in JSON and discards token-bearing stderr' {
            Set-Responses @((New-ApiStep issues/7/comments POST -App -RawOutput '{"body":"__TOKEN__"}' -ErrorText '__TOKEN__'))
            $output = Add-GhAppComment 7 $script:BodyFile
            $output.body | Should Be '[redacted]'
            Assert-NoToken ($output | Out-String)
        }
        It 'reports only HTTP status and API path when gh fails' {
            Set-Responses @((New-ApiStep issues/7/comments POST -App -ExitCode 1 -ErrorText 'private details __TOKEN__ (HTTP 403)'))
            $message = Get-Message { Add-GhAppComment 7 $script:BodyFile }
            $message | Should Be "HTTP 403 /repos/$script:RepositoryName/issues/7/comments"
            Assert-NoToken $message
        }
        It 'reports an unknown status without returning malformed JSON' {
            Set-Responses @((New-ApiStep issues/7/comments POST -App -RawOutput '__TOKEN__'))
            $message = Get-Message { Add-GhAppComment 7 $script:BodyFile }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/issues/7/comments"
            Assert-NoToken $message
        }
        It 'discards a failing helper error without starting gh' {
            $env:NVT_GHAPP_HELPER_FAIL = '99'
            $message = Get-Message { Add-GhAppComment 7 $script:BodyFile }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/issues/7/comments (token helper exit 1) (after 3 attempts)"
            @(Get-Calls).Count | Should Be 0
            @([IO.File]::ReadAllLines($env:NVT_GHAPP_HELPER_CALLS)).Count | Should Be 3
            Assert-NoToken $message
        }
        It 'retries a failed token request for a write and starts gh once' {
            $env:NVT_GHAPP_HELPER_FAIL = '1'
            Set-Responses @((New-ApiStep issues/7/comments POST @{ id = 1 } -App))
            (Add-GhAppComment 7 $script:BodyFile).id | Should Be 1
            @(Get-Calls).Count | Should Be 1
            @([IO.File]::ReadAllLines($env:NVT_GHAPP_HELPER_CALLS)).Count | Should Be 2
            Assert-MockCalled Start-Sleep -Times 1 -Exactly -Scope It
        }
        It 'sets the App token only in one child and preserves the parent environment' {
            $env:GH_TOKEN = 'SYNTHETIC_PARENT_TOKEN'
            $env:GITHUB_TOKEN = 'SYNTHETIC_PARENT_TOKEN'
            $env:GH_DEBUG = 'api'
            $env:GH_HOST = 'example.invalid'
            Set-Responses @((New-ApiStep issues/7/comments POST @{ id = 1 } -App))
            $null = Add-GhAppComment 7 $script:BodyFile
            $env:GH_TOKEN | Should Be 'SYNTHETIC_PARENT_TOKEN'
            $call = @(Get-Calls)[0]
            $call.AppToken | Should Be $true
            $call.Environment.GITHUB_TOKEN | Should Be $null
            $call.Environment.GH_DEBUG | Should Be $null
            $call.Environment.GH_HOST | Should Be github.com
            $helper = Get-Content $env:NVT_GHAPP_HELPER_CALLS | ConvertFrom-Json
            $helper.TokenPresent | Should Be $false
            $helper.DebugPresent | Should Be $false
            $helper.Arguments.Mode | Should Be token
            $helper.Arguments.Owner | Should Be $script:Owner
            $helper.Arguments.Repo | Should Be $script:RepoName
            Assert-NoToken
        }
    }

    Describe 'Owner read commands' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'runs a PR read with no token and the configured repository' {
            $env:GH_TOKEN = 'SYNTHETIC_PARENT_TOKEN'
            Set-Responses @(@{ Arguments = @('pr', 'list', '--repo', $script:RepositoryName); App = $false; Output = '[]'; Error = ''; ExitCode = 0 })
            Invoke-GhAppRead -Arguments @('pr', 'list') | Should Be '[]'
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
            @(Get-Calls)[0].Environment.GH_TOKEN | Should Be $null
        }
        It 'allows repository-scoped GET API reads' {
            Set-Responses @(@{ Arguments = @('api', "/repos/$script:RepositoryName/pulls", '--method', 'GET'); App = $false; Output = '[]'; Error = ''; ExitCode = 0 })
            Invoke-GhAppRead -Arguments @('api', "/repos/$script:RepositoryName/pulls", '--method', 'GET') | Should Be '[]'
        }
        It 'allows a GET of the configured repository metadata' {
            Set-Responses @(@{ Arguments = @('api', "/repos/$script:RepositoryName"); App = $false; Output = '{}'; Error = ''; ExitCode = 0 })
            Invoke-GhAppRead -Arguments @('api', "/repos/$script:RepositoryName") | Should Be '{}'
        }
        foreach ($readArguments in @(
            @('pr', 'create'), @('pr', 'merge', '7'), @('pr', 'review', '7', '--approve'), @('issue', 'comment', '7'),
            @('repo', 'delete'), @('alias', 'list'), @('extension', 'list'), @('--help', 'pr'),
            @('api', '/repos/example-owner/example-repo/pulls', '-X', 'POST'),
            @('api', '/repos/example-owner/example-repo/pulls', '--input', '-'),
            @('api', '/repos/example-owner/example-repo/pulls', '-f', 'body=x'),
            @('api', '/repos/example-owner/example-repo/pulls', '--hostname', 'example.invalid'),
            @('pr', 'view', 'https://github.com/other-owner/other-repo/pull/7'), @('pr', 'view', '7', '--web'),
            @('pr', 'list', '--search', 'repo:other-owner/other-repo'), @('pr', 'list', '-S', 'is:open OR is:closed')
        )) {
            It "rejects unsafe read arguments: $($readArguments -join ' ')" {
                Get-Message { Invoke-GhAppRead -Arguments $readArguments } | Should Be 'Invoke-GhAppRead accepts only repository-scoped read commands.'
                @(Get-Calls).Count | Should Be 0
            }
        }
        foreach ($readArguments in @(
            @('api', '/repos/other-owner/other-repo/pulls'), @('api', '/graphql'),
            @('api', '/repos/example-owner/example-repo/../other-repo/pulls'),
            @('api', '/repos/example-owner/example-repo/%2e%2e/other-repo/pulls'),
            @('pr', 'list', '--repo', 'other-owner/other-repo'), @('pr', 'list', '-Rother-owner/other-repo'),
            @('pr', 'list', '--repo=other-owner/other-repo'), @('repo', 'view', 'other-owner/other-repo')
        )) {
            It "rejects a read repository mismatch: $($readArguments -join ' ')" {
                Get-Message { Invoke-GhAppRead -Arguments $readArguments } | Should Be "Repository mismatch. Expected $script:RepositoryName."
                @(Get-Calls).Count | Should Be 0
            }
        }
    }

    function New-GitFixture {
        $script:GitRoot = Join-Path $env:NVT_GHAPP_TEST_ROOT 'git-fixture'
        $null = New-Item -ItemType Directory -Force -Path $script:GitRoot
        $null = & git init --quiet $script:GitRoot
        if ($LASTEXITCODE -ne 0) { throw 'The local Git fixture could not be initialized.' }
        $dataFile = Join-Path $script:GitRoot 'fixture.data'
        [IO.File]::WriteAllText($dataFile, 'base content')
        $baseBlob = (Invoke-GhAppGit $script:GitRoot @('hash-object', '-w', $dataFile)).Trim()
        $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--add', '--cacheinfo', "100644,$baseBlob,edit.txt")
        $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--add', '--cacheinfo', "100644,$baseBlob,removed.txt")
        $script:BaseTree = (Invoke-GhAppGit $script:GitRoot @('write-tree')).Trim()
        # Create synthetic objects with commit-tree. Never commit in the host repository.
        $identity = @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgSign=false')
        $script:BaseCommit = (Invoke-GhAppGit $script:GitRoot ($identity + @('commit-tree', $script:BaseTree, '-m', 'base fixture'))).Trim()
        $script:ExpectedBlobs = @()
        foreach ($definition in @(
            @{ Path = 'binary 中文 file.bin'; Mode = '100755'; Bytes = [byte[]]@(0, 255, 127, 13, 10) },
            @{ Path = 'edit.txt'; Mode = '100644'; Bytes = [Text.Encoding]::UTF8.GetBytes('changed content') },
            @{ Path = 'link'; Mode = '120000'; Bytes = [Text.Encoding]::UTF8.GetBytes('edit.txt') }
        )) {
            [IO.File]::WriteAllBytes($dataFile, $definition.Bytes)
            $sha = (Invoke-GhAppGit $script:GitRoot @('hash-object', '-w', $dataFile)).Trim()
            $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--add', '--cacheinfo', "$($definition.Mode),$sha,$($definition.Path)")
            $script:ExpectedBlobs += @{ Path = $definition.Path; Sha = $sha; Content = [Convert]::ToBase64String($definition.Bytes) }
        }
        $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--add', '--cacheinfo', "160000,$script:BaseCommit,module")
        $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--force-remove', 'removed.txt')
        $script:HeadTree = (Invoke-GhAppGit $script:GitRoot @('write-tree')).Trim()
        $script:LocalHead = (Invoke-GhAppGit $script:GitRoot ($identity + @('commit-tree', $script:HeadTree, '-p', $script:BaseCommit, '-m', 'head fixture'))).Trim()
        $null = Invoke-GhAppGit $script:GitRoot @('update-ref', 'HEAD', $script:LocalHead)
    }
    New-GitFixture

    function New-PushSteps {
        param([switch]$ExistingRef, [object[]]$Open = @())
        New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @($Open)
        if ($Open.Count -gt 0) { New-ReviewStep -Reviews @() }
        New-ApiStep "git/commits/$script:HeadSha" -Value @{ tree = @{ sha = $script:BaseTree } }
        foreach ($blob in $script:ExpectedBlobs) { New-ApiStep git/blobs POST @{ sha = $blob.Sha } -App }
        New-ApiStep git/trees POST @{ sha = $script:HeadTree } -App
        New-ApiStep git/commits POST @{ sha = $script:MergeSha } -App
        if ($ExistingRef) {
            New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }
            New-ApiStep 'git/refs/heads/test%2Fmodule' PATCH @{} -App
        } else {
            New-ApiStep 'git/ref/heads/test%2Fmodule' -ExitCode 1 -ErrorText 'HTTP 404'
            New-ApiStep git/refs POST @{} -App
        }
    }
    function Invoke-PushFixture {
        param([switch]$AllowOpenPr, [string]$ExtraParent)
        Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $script:BaseCommit -RemoteParent $script:HeadSha `
            -Branch test/module -MessageFile $script:BodyFile -AllowOpenPr:$AllowOpenPr -ExtraParent $ExtraParent
    }

    Describe 'Read retries' {
        BeforeEach { Reset-Fixture; Import-Fixture; Mock Start-Sleep {} }
        It 'retries a failed read and returns the second answer' {
            Set-Responses @((New-ApiStep 'pulls/7' -ExitCode 1 -ErrorText 'HTTP 502 Bad Gateway'),
                (New-ApiStep 'pulls/7' -Value @{ number = 7 }))
            $context = Get-GhAppContext
            (Invoke-GhAppApi $context GET 'pulls/7').number | Should Be 7
            @(Get-Calls).Count | Should Be 2
            Assert-MockCalled Start-Sleep -Times 1 -Exactly -Scope It
        }
        It 'retries an unknown read failure and stops after three attempts' {
            Set-Responses @((New-ApiStep 'pulls/7' -ExitCode 1), (New-ApiStep 'pulls/7' -ExitCode 1), (New-ApiStep 'pulls/7' -ExitCode 1))
            $context = Get-GhAppContext
            Get-Message { Invoke-GhAppApi $context GET 'pulls/7' } |
                Should Be "HTTP unknown /repos/$script:RepositoryName/pulls/7 (after 3 attempts)"
            @(Get-Calls).Count | Should Be 3
            Assert-MockCalled Start-Sleep -Times 2 -Exactly -Scope It
        }
        It 'retries the pull request view' {
            Set-Responses @(@{ Arguments = (New-PrStep).Arguments; App = $false; Output = ''; Error = 'HTTP 503'; ExitCode = 1 },
                (New-PrStep))
            $context = Get-GhAppContext
            (Get-GhAppPullRequest $context 7).headRefOid | Should Be $script:HeadSha
            @(Get-Calls).Count | Should Be 2
        }
        It 'does not retry a read that GitHub refused with another 4xx' {
            Set-Responses @((New-ApiStep 'pulls/7' -ExitCode 1 -ErrorText 'HTTP 403 Forbidden'))
            $context = Get-GhAppContext
            Get-Message { Invoke-GhAppApi $context GET 'pulls/7' } | Should Be "HTTP 403 /repos/$script:RepositoryName/pulls/7"
            @(Get-Calls).Count | Should Be 1
            Assert-MockCalled Start-Sleep -Times 0 -Exactly -Scope It
        }
        It 'never retries a write' {
            Set-Responses @((New-ApiStep 'issues/7/comments' POST -App -ExitCode 1 -ErrorText 'HTTP 502 Bad Gateway'))
            $context = Get-GhAppContext
            Get-Message { Invoke-GhAppApi $context POST 'issues/7/comments' @{ body = 'x' } -AsApp } |
                Should Be "HTTP 502 /repos/$script:RepositoryName/issues/7/comments"
            @(Get-Calls).Count | Should Be 1
            Assert-MockCalled Start-Sleep -Times 0 -Exactly -Scope It
        }
    }
    Describe 'Git data API branch pushes' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'creates one commit with binary blobs, modes, a gitlink, and a deletion' {
            Set-Responses @(New-PushSteps)
            $result = Invoke-PushFixture -ExtraParent $script:NextSha
            $result.Commit | Should Be $script:MergeSha
            $result.Tree | Should Be $script:HeadTree
            $result.Files | Should Be 5
            $calls = @(Get-Calls)
            for ($index = 0; $index -lt $script:ExpectedBlobs.Count; $index++) {
                $body = ConvertFrom-Json $calls[$index + 2].Input
                $body.content | Should Be $script:ExpectedBlobs[$index].Content
                $body.encoding | Should Be base64
            }
            $tree = ConvertFrom-Json $calls[5].Input
            @($tree.tree).Count | Should Be 5
            ($tree.tree | Where-Object path -eq 'removed.txt').sha | Should Be $null
            ($tree.tree | Where-Object path -eq 'module').type | Should Be commit
            ($tree.tree | Where-Object path -eq 'module').sha | Should Be $script:BaseCommit
            ($tree.tree | Where-Object path -eq 'link').mode | Should Be '120000'
            ($tree.tree | Where-Object path -eq 'binary 中文 file.bin').mode | Should Be '100755'
            $commit = ConvertFrom-Json $calls[6].Input
            @($commit.parents).Count | Should Be 2
            $commit.parents[0] | Should Be $script:HeadSha
            $commit.parents[1] | Should Be $script:NextSha
            (ConvertFrom-Json $calls[8].Input).ref | Should Be refs/heads/test/module
            Assert-NoToken ($result | Out-String)
        }
        It 'refuses a LocalBase that HEAD is not built on before any gh call' {
            $identity = @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgSign=false')
            $stale = (Invoke-GhAppGit $script:GitRoot ($identity + @('commit-tree', $script:BaseTree, '-m', 'unrelated base'))).Trim()
            Set-Responses @()
            Get-Message {
                Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $stale -RemoteParent $script:HeadSha `
                    -Branch test/module -MessageFile $script:BodyFile
            } | Should Be "LocalBase $stale is not an ancestor of HEAD; rebase onto the remote base first."
            @(Get-Calls).Count | Should Be 0
        }
        It 'fast-forwards an existing ref with force false' {
            Set-Responses @(New-PushSteps -ExistingRef)
            $null = Invoke-PushFixture
            (ConvertFrom-Json @(Get-Calls)[8].Input).force | Should Be $false
        }
        It 'creates a commit for an empty tree difference with an empty tree array' {
            Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @()),
                (New-ApiStep "git/commits/$script:HeadSha" -Value @{ tree = @{ sha = $script:HeadTree } }),
                (New-ApiStep git/trees POST @{ sha = $script:HeadTree } -App),
                (New-ApiStep git/commits POST @{ sha = $script:MergeSha } -App),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -ExitCode 1 -ErrorText 'HTTP 404'),
                (New-ApiStep git/refs POST @{} -App))
            Push-Location -LiteralPath $script:GitRoot
            try {
                $result = Push-GhAppBranch -Worktree . -LocalBase $script:LocalHead -RemoteParent $script:HeadSha `
                    -Branch test/module -MessageFile $script:BodyFile
            } finally { Pop-Location }
            $result.Files | Should Be 0
            @((ConvertFrom-Json @(Get-Calls)[2].Input).tree).Count | Should Be 0
        }
        It 'allows an open PR only when explicitly requested and unreviewed by the owner' {
            Set-Responses @(New-PushSteps -Open @(@{ number = 7 }))
            $null = Invoke-PushFixture -AllowOpenPr
            @(Get-Calls).Count | Should Be 10
        }
        It 'refuses an open PR without AllowOpenPr' {
            Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @(@{ number = 7 })),
                (New-ReviewStep -Reviews @()))
            Get-Message { Invoke-PushFixture } | Should Be '#7 is open; push refused. Pass -AllowOpenPr only if it has not been sent to the owner.'
            @(Get-Calls).Count | Should Be 2
        }
        foreach ($reviewState in @('APPROVED', 'COMMENTED', 'CHANGES_REQUESTED', 'DISMISSED')) {
            It "refuses any owner $reviewState review even with AllowOpenPr" {
                Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @(@{ number = 7 })),
                    (New-ReviewStep -Reviews @((New-Review -State $reviewState))))
                Get-Message { Invoke-PushFixture -AllowOpenPr } | Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
                @(Get-Calls).Count | Should Be 2
            }
        }
        It 'reads later review pages before allowing a push' {
            $firstPage = @(1..100 | ForEach-Object { New-Review -Id $_ -Login other-reviewer })
            Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @(@{ number = 7 })),
                (New-ApiStep 'pulls/7/reviews?per_page=100&page=1' -Value $firstPage),
                (New-ApiStep 'pulls/7/reviews?per_page=100&page=2' -Value @((New-Review -Id 101))))
            Get-Message { Invoke-PushFixture -AllowOpenPr } | Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
            @(Get-Calls).Count | Should Be 3
        }
        foreach ($shaParameter in @('LocalBase', 'RemoteParent', 'ExtraParent')) {
            It "refuses a short $shaParameter before gh" {
                $parameters = @{ Worktree = $script:GitRoot; LocalBase = $script:BaseCommit; RemoteParent = $script:HeadSha;
                    Branch = 'test/module'; MessageFile = $script:BodyFile }
                $parameters[$shaParameter] = 'short'
                Get-Message { Push-GhAppBranch @parameters } | Should Be "$shaParameter is not a 40-character SHA."
                @(Get-Calls).Count | Should Be 0
            }
        }
        foreach ($failure in @(
            @{ Index = 1; Value = @{ tree = @{ sha = 'short' } }; Message = 'remote base tree is not a 40-character SHA.'; Count = 2 },
            @{ Index = 2; Value = @{ sha = 'short' }; Message = 'new blob is not a 40-character SHA.'; Count = 3 },
            @{ Index = 5; Value = @{ sha = 'short' }; Message = 'new tree is not a 40-character SHA.'; Count = 6 },
            @{ Index = 6; Value = @{ sha = 'short' }; Message = 'new commit is not a 40-character SHA.'; Count = 7 },
            @{ Index = 7; Value = @{ object = @{ sha = 'short' } }; Message = 'remote branch head is not a 40-character SHA.'; Count = 8 }
        )) {
            It "refuses an invalid API SHA: $($failure.Message)" {
                $steps = @(New-PushSteps -ExistingRef)
                $steps[$failure.Index].Output = ConvertTo-Json $failure.Value -Compress -Depth 10
                Set-Responses $steps
                Get-Message { Invoke-PushFixture } | Should Be $failure.Message
                @(Get-Calls).Count | Should Be $failure.Count
            }
        }
        It 'rejects a base tree mismatch before uploading blobs' {
            $steps = @(New-PushSteps)
            $steps[1].Output = ConvertTo-Json @{ tree = @{ sha = $script:NextSha } } -Compress
            Set-Responses $steps
            Get-Message { Invoke-PushFixture } | Should Be "base tree mismatch: remote $script:NextSha local $script:BaseTree"
            @(Get-Calls).Count | Should Be 2
        }
        It 'rejects a blob mismatch before creating the tree' {
            $steps = @(New-PushSteps)
            $steps[2].Output = ConvertTo-Json @{ sha = $script:NextSha } -Compress
            Set-Responses $steps
            Get-Message { Invoke-PushFixture } | Should Be "blob mismatch $($script:ExpectedBlobs[0].Path)"
            @(Get-Calls).Count | Should Be 3
        }
        It 'rejects a tree mismatch before creating a commit or updating a ref' {
            $steps = @(New-PushSteps)
            $steps[5].Output = ConvertTo-Json @{ sha = $script:NextSha } -Compress
            Set-Responses $steps
            Get-Message { Invoke-PushFixture } | Should Be "tree mismatch: remote $script:NextSha local $script:HeadTree"
            @(Get-Calls).Count | Should Be 6
        }
        It 'does not treat a failed ref lookup as a missing branch' {
            $steps = @(New-PushSteps)
            $steps[7].Error = 'HTTP 403 __TOKEN__'
            Set-Responses $steps
            Get-Message { Invoke-PushFixture } | Should Be "HTTP 403 /repos/$script:RepositoryName/git/ref/heads/test%2Fmodule"
            @(Get-Calls).Count | Should Be 8
            Assert-NoToken
        }
    }

    function New-MergeSteps {
        param([int]$Number = 7, [string]$Head = $script:HeadSha)
        New-PrStep $Number (New-Pr -Head $Head)
        New-ReviewStep $Number @((New-Review -Head $Head))
        New-ChecksStep $Number
        New-PrStep $Number (New-Pr -Head $Head)
        New-ReviewStep $Number @((New-Review -Head $Head))
        New-ApiStep "pulls/$Number/merge" PUT @{ merged = $true; sha = $script:MergeSha } -App
        New-PrStep $Number (New-Pr -Head $Head -State MERGED)
        New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $Head } }
        New-ApiStep "compare/main...$Head" -Value @{ status = 'behind'; ahead_by = 0 }
        New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -RawOutput ''
    }
    function Invoke-MergeFixture {
        param([int[]]$Numbers = @(7), [int]$TimeoutSeconds = 1200)
        Merge-GhAppApprovedPullRequest -Numbers $Numbers -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1 -TimeoutSeconds $TimeoutSeconds -DeleteBranchPrefix 'test/'
    }

    Describe 'Owner-approved merges' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            Add-LedgerFixture 7; Add-LedgerFixture 8
            Mock Get-GhAppTimelineObservation { 'unavailable' }
        }
        It 'merges in order, deletes contained branches, and appends UTC times and SHAs' {
            Set-Responses @((New-MergeSteps 7) + (New-MergeSteps 8))
            Invoke-MergeFixture -Numbers @(7, 8)
            $calls = @(Get-Calls)
            (ConvertFrom-Json $calls[5].Input).sha | Should Be $script:HeadSha
            (ConvertFrom-Json $calls[5].Input).merge_method | Should Be merge
            $log = [IO.File]::ReadAllText($script:LogFile)
            $log | Should Match "(?m)^\d{4}-\d{2}-\d{2}T.*\+00:00 #7 merged $script:HeadSha -> $script:MergeSha"
            $log.IndexOf('#7 merged') | Should BeLessThan $log.IndexOf('#8 merged')
            $log | Should Match 'done: 7,8'
            @(Get-Calls).Count | Should Be 20
            Assert-NoToken $log
        }
        It 'skips a PR that is already closed' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State CLOSED)))
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 1
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 skip: state CLOSED'
        }
        It 'appends a relative log in the current PowerShell location' {
            Set-Responses @(New-MergeSteps)
            Push-Location -LiteralPath $script:FixtureRoot
            try { Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath '.\merge.log' -DeleteBranchPrefix 'test/' } finally { Pop-Location }
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 merged'
            Assert-NoToken
        }
        foreach ($reviews in @(
            @(), @((New-Review -SubmittedAt '2025-12-30T00:00:00Z')), @((New-Review -Login other-reviewer)),
            @((New-Review), (New-Review -State CHANGES_REQUESTED -Id 2)),
            @((New-Review), (New-Review -State DISMISSED -Id 2))
        )) {
            It "rejects an absent, stale, or superseded owner approval ($($reviews.Count) records)" {
                Set-Responses @((New-PrStep), (New-ReviewStep -Reviews $reviews))
                Get-Message { Invoke-MergeFixture -Numbers @(7, 8) } | Should Be "#7 stop: no owner approval after review request $script:HeadSha."
                @(Get-Calls).Count | Should Be 2
            }
        }
        It 'stops when an App branch update fails' {
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BEHIND)), (New-ReviewStep),
                (New-ApiStep pulls/7/update-branch PUT @{} -App -ExitCode 1 -ErrorText 'HTTP 422'))
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: branch update failed; manual resolution requires owner approval.'
            @(Get-Calls).Count | Should Be 3
        }
        It 'keeps the merged branch when no delete prefix is given' {
            Set-Responses @(@(New-MergeSteps)[0..6])
            Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1
            @(Get-Calls).Count | Should Be 7
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'kept branch test/module; no matching delete prefix'
        }
        It 'keeps a release branch that does not match the delete prefix' {
            $steps = @(New-MergeSteps)
            foreach ($index in @(0, 3)) { $steps[$index] = New-PrStep -Pr (New-Pr -Branch '1.2.2') }
            $steps[6] = New-PrStep -Pr (New-Pr -Branch '1.2.2' -State MERGED)
            Set-Responses @($steps[0..6])
            Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1 -DeleteBranchPrefix 'feature/'
            @(Get-Calls).Count | Should Be 7
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'kept branch 1.2.2; no matching delete prefix'
        }
        It 'rejects an empty delete prefix before any call' {
            Get-Message { Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath $script:LogFile -DeleteBranchPrefix ' ' } |
                Should Be 'DeleteBranchPrefix must be nonempty when given.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'uses submission order when a lower review ID requests changes last' {
            $reviews = @((New-Review -State CHANGES_REQUESTED -Id 1 -SubmittedAt '2026-01-01T01:00:00Z'),
                (New-Review -Id 2 -SubmittedAt '2026-01-01T00:00:00Z'))
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews $reviews))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: no owner approval after review request $script:HeadSha."
            @(Get-Calls).Count | Should Be 2
        }
        It 'uses submission order when a lower review ID approves last' {
            $reviews = @((New-Review -Id 1 -SubmittedAt '2026-01-01T01:00:00Z'),
                (New-Review -State CHANGES_REQUESTED -Id 2 -SubmittedAt '2026-01-01T00:00:00Z'))
            $steps = @(New-MergeSteps)
            $steps[1] = New-ReviewStep -Reviews $reviews
            $steps[4] = New-ReviewStep -Reviews $reviews
            Set-Responses $steps
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 10
        }
        It 'does not merge while the owner has a pending review' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @((New-Review), (New-Review -Id 2 -State PENDING))))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: no owner approval after review request $script:HeadSha."
            @(Get-Calls).Count | Should Be 2
        }
        It 'requires manual conflict resolution and owner approval' {
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState DIRTY)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: merge conflict with main; manual resolution requires owner approval.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'waits for pending required checks before merging' {
            $steps = @(New-MergeSteps)
            $steps[2] = New-ChecksStep -Bucket pending -ExitCode 8
            $steps = @($steps[0..2]) + @(New-MergeSteps)
            Set-Responses $steps
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 13
        }
        It 'stops on failed required checks' {
            Set-Responses @((New-PrStep), (New-ReviewStep), (New-ChecksStep -Bucket fail -ExitCode 1))
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: required checks failed.'
            @(Get-Calls).Count | Should Be 3
        }
        It 'stops on cancelled required checks' {
            Set-Responses @((New-PrStep), (New-ReviewStep), (New-ChecksStep -Bucket cancel -ExitCode 1))
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: required checks failed.'
            @(Get-Calls).Count | Should Be 3
        }
        It 'rejects an invalid head SHA before reading approvals' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head short)))
            Get-Message { Invoke-MergeFixture } | Should Be 'head of #7 is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'keeps an approval that a later owner COMMENTED review follows' {
            $steps = @(New-MergeSteps)
            $steps[1] = New-ReviewStep -Reviews @((New-Review), (New-Review -State COMMENTED -Id 2))
            $steps[4] = New-ReviewStep -Reviews @((New-Review), (New-Review -State COMMENTED -Id 2))
            Set-Responses $steps
            Invoke-MergeFixture
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 merged'
        }
        It 'times out when merge readiness remains unknown' {
            # A controlled clock: the first read sets the deadline, every later read is past it.
            $script:ClockReads = 0
            $script:ClockStart = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            Mock Get-GhAppUtcNow { $script:ClockReads++; if ($script:ClockReads -eq 1) { $script:ClockStart } else { $script:ClockStart.AddSeconds(2) } }
            Mock Start-Sleep { }
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState UNKNOWN)), (New-ReviewStep), (New-ChecksStep -Bucket pending -ExitCode 8),
                (New-PrStep -Pr (New-Pr -MergeState UNKNOWN)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture -TimeoutSeconds 1 } | Should Be '#7 stop: timed out in state UNKNOWN.'
        }
        It 'refuses an approval older than a newer request' {
            Add-LedgerFixture -RequestedAt '2026-01-01T01:00:00Z'
            Set-Responses @((New-PrStep), (New-ReviewStep), (New-ChecksStep), (New-PrStep -Pr (New-Pr -Head $script:NextSha)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: no owner approval after review request $script:HeadSha."
            @(Get-Calls).Count | Should Be 2
        }
        It 'does not delete a branch when the merge response says merged false' {
            $steps = @(New-MergeSteps)
            $steps[5].Output = '{"merged":false}'
            Set-Responses $steps
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: merge failed.'
            @(Get-Calls).Count | Should Be 6
        }
        It 'does not delete a branch when the merge is not confirmed' {
            $steps = @(New-MergeSteps)
            $steps[6] = New-PrStep
            Set-Responses $steps
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: merge was not confirmed.'
            @(Get-Calls).Count | Should Be 7
        }
        It 'rejects an invalid merge commit SHA before deleting the branch' {
            $steps = @(New-MergeSteps)
            $steps[5].Output = '{"merged":true,"sha":"short"}'
            Set-Responses $steps
            Get-Message { Invoke-MergeFixture } | Should Be 'merge commit of #7 is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 6
        }
        It 'keeps a branch that changed after merge' {
            $steps = @(New-MergeSteps)
            $steps[7].Output = ConvertTo-Json @{ object = @{ sha = $script:NextSha } } -Compress
            Set-Responses $steps
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 8
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'ref changed or absent'
        }
        It 'keeps a branch that main does not contain' {
            $steps = @(New-MergeSteps)
            $steps[8].Output = '{"status":"ahead","ahead_by":1}'
            Set-Responses $steps
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 9
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'main does not contain'
        }
    }

    function New-ReviewGitFixture {
        $script:ReviewGitRoot = Join-Path $env:NVT_GHAPP_TEST_ROOT 'review-git-fixture'
        $null = & git init --quiet $script:ReviewGitRoot
        if ($LASTEXITCODE -ne 0) { throw 'The review Git fixture could not be initialized.' }
        $script:ReviewIdentity = @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgSign=false')
        $script:ReviewData = Join-Path $script:ReviewGitRoot 'object.data'
        function Set-ReviewBlob {
            param([string]$Path, [string]$Content, [string]$Mode = '100644')
            [IO.File]::WriteAllText($script:ReviewData, $Content, [Text.UTF8Encoding]::new($false))
            $blob = (Invoke-GhAppGit $script:ReviewGitRoot @('hash-object', '-w', $script:ReviewData)).Trim()
            $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-index', '--add', '--cacheinfo', "$Mode,$blob,$Path")
        }
        function New-ReviewCommit {
            param([string[]]$Parents, [string]$Message)
            $tree = (Invoke-GhAppGit $script:ReviewGitRoot @('write-tree')).Trim()
            $arguments = $script:ReviewIdentity + @('commit-tree', $tree, '-m', $Message)
            foreach ($parent in $Parents) { $arguments += @('-p', $parent) }
            (Invoke-GhAppGit $script:ReviewGitRoot $arguments).Trim()
        }
        $original = ((1..12 | ForEach-Object { "line $_" }) -join "`n") + "`n"
        $branch = $original.Replace('line 1' + "`n", 'branch line 1' + "`n")
        $base = $original.Replace('line 12' + "`n", 'base line 12' + "`n")
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', '--empty')
        Set-ReviewBlob 'shared 中文.txt' $original
        Set-ReviewBlob 'deleted.txt' 'to delete'
        $script:ReviewRoot = New-ReviewCommit @() 'review root'
        Set-ReviewBlob 'shared 中文.txt' $branch
        $script:ReviewedSha = New-ReviewCommit @($script:ReviewRoot) 'head sent for review'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ReviewRoot)
        Set-ReviewBlob 'shared 中文.txt' $base
        $script:ReviewBase = New-ReviewCommit @($script:ReviewRoot) 'base same-file change'
        $cleanTree = (Invoke-GhAppGit $script:ReviewGitRoot @('merge-tree', '--write-tree', $script:ReviewedSha, $script:ReviewBase)).Trim()
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $cleanTree)
        $script:CleanMerge = New-ReviewCommit @($script:ReviewedSha, $script:ReviewBase) 'clean same-file merge'
        Set-ReviewBlob 'shared 中文.txt' $branch.Replace('line 12' + "`n", 'base line 12' + "`n").Replace('line 6' + "`n", 'manual line 6' + "`n")
        $script:ManualMerge = New-ReviewCommit @($script:ReviewedSha, $script:ReviewBase) 'manual merge change'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ReviewedSha)
        Set-ReviewBlob 'unreviewed.txt' 'new feature'
        $script:NonMerge = New-ReviewCommit @($script:ReviewedSha) 'unreviewed commit'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ReviewRoot)
        Set-ReviewBlob 'shared 中文.txt' $original.Replace('line 1' + "`n", 'conflicting base line 1' + "`n")
        $script:ConflictBase = New-ReviewCommit @($script:ReviewRoot) 'conflicting base'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ManualMerge)
        $script:ConflictMerge = New-ReviewCommit @($script:ReviewedSha, $script:ConflictBase) 'resolved conflict'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ConflictBase)
        $script:BaseResolvedConflictMerge = New-ReviewCommit @($script:ReviewedSha, $script:ConflictBase) 'conflict resolved wholly to base'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $script:ReviewedSha)
        $script:FirstParentResolvedConflictMerge = New-ReviewCommit @($script:ReviewedSha, $script:ConflictBase) 'conflict resolved wholly to first parent'
        $script:BaseUpdateCommits = @()
        $script:BaseUpdateMerges = @()
        $baseParent = $script:ReviewRoot
        $headParent = $script:ReviewedSha
        for ($round = 1; $round -le 21; $round++) {
            $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $baseParent)
            Set-ReviewBlob "base $round 中文.txt" "base addition $round"
            if ($round -eq 1) { $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-index', '--force-remove', 'deleted.txt') }
            $baseParent = New-ReviewCommit @($baseParent) "base round $round"
            $script:BaseUpdateCommits += $baseParent
            $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', $baseParent)
            Set-ReviewBlob 'shared 中文.txt' $branch
            $headParent = New-ReviewCommit @($headParent, $baseParent) "clean base merge $round"
            $script:BaseUpdateMerges += $headParent
        }
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-ref', 'HEAD', $script:ReviewedSha)
    }
    New-ReviewGitFixture

    function Invoke-ReviewMergeFixture {
        Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:ReviewGitRoot -LogPath $script:LogFile -PollSeconds 1
    }
    function New-ReviewMergeSteps {
        param([string]$Head = $script:ReviewedSha, [string]$Base = $script:ReviewBase,
            [object[]]$Reviews = @((New-Review -Head $script:ReviewedSha)))
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base)
        New-ReviewStep -Reviews $Reviews
        New-ChecksStep
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base)
        New-ReviewStep -Reviews $Reviews
        New-ApiStep pulls/7/merge PUT @{ merged = $true; sha = $script:MergeSha } -App
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base -State MERGED)
    }

    Describe 'Ledger approval and the 10-05 rule' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            # The fixtures already contain every object. Only fetching is replaced; proof uses real Git.
            Mock Sync-GhAppReviewCommits {}
            Mock Get-GhAppTimelineObservation { 'unavailable' }
        }
        It 'refuses a moved commit_id when there is no ledger entry' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:CleanMerge)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:CleanMerge))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no review ledger entry'
            @(Get-Calls).Count | Should Be 1
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
        }
        It 'ignores entries for other repositories and PRs' {
            Add-LedgerFixture -Repo other-repo
            Add-LedgerFixture -Owner other-owner
            Add-LedgerFixture -Number 8
            Set-Responses @((New-PrStep))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no review ledger entry'
            @(Get-Calls).Count | Should Be 1
        }
        It 'uses the latest appended entry even when its timestamp is earlier' {
            Add-LedgerFixture -Head $script:NonMerge -RequestedAt '2026-01-01T01:00:00Z'
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps)
            Invoke-ReviewMergeFixture
            (ConvertFrom-Json @(Get-Calls)[5].Input).sha | Should Be $script:ReviewedSha
        }
        It 'requires approval strictly after the latest request' {
            Add-LedgerFixture -Head $script:ReviewedSha -RequestedAt '2026-01-01T00:00:00Z'
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha)), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no owner approval after review request'
            @(Get-Calls).Count | Should Be 2
        }
        It 'refuses an approval whose commit is not on the proven chain, even when its time is after the request' {
            # A local clock behind GitHub can make an approval of an older head look newer than the request.
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha)), (New-ReviewStep -Reviews @((New-Review -Head $script:NextSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: owner approval is on $script:NextSha, not on review request $script:ReviewedSha or a clean base merge after it."
            Assert-MockCalled Sync-GhAppReviewCommits -Times 0 -Exactly
        }
        It 'accepts approval on S when H is S' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps)
            Invoke-ReviewMergeFixture
            Assert-MockCalled Sync-GhAppReviewCommits -Times 0 -Exactly
        }
        foreach ($rounds in @(1, 2)) {
            It "updates a behind branch $rounds times and logs S, H, and the base" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @()
                $head = $script:ReviewedSha
                for ($round = 0; $round -lt $rounds; $round++) {
                    $steps += (New-PrStep -Pr (New-Pr -Head $head -BaseHead $script:BaseUpdateCommits[$round] -MergeState BEHIND))
                    $steps += New-ReviewStep -Reviews @((New-Review -Head $head))
                    $steps += New-ApiStep pulls/7/update-branch PUT @{ message = 'Updating' } -App
                    $head = $script:BaseUpdateMerges[$round]
                }
                Set-Responses @($steps + @(New-ReviewMergeSteps -Head $head -Base $script:BaseUpdateCommits[$rounds - 1]))
                Invoke-ReviewMergeFixture
                $calls = @(Get-Calls)
                $updates = @($calls | Where-Object { $_.Arguments[1] -match '/update-branch$' })
                $updates.Count | Should Be $rounds
                (ConvertFrom-Json $updates[0].Input).expected_head_sha | Should Be $script:ReviewedSha
                if ($rounds -eq 2) { (ConvertFrom-Json $updates[1].Input).expected_head_sha | Should Be $script:BaseUpdateMerges[0] }
                $merge = $calls | Where-Object { $_.Arguments[1] -match '/merge$' }
                (ConvertFrom-Json $merge.Input).sha | Should Be $head
                [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 review requested at $script:ReviewedSha; approved .*; merged head $head; differences only from main"
            }
        }
        It 'proves base additions and deletions through merge-tree before and after checks' {
            $script:OriginalGhAppGit = (Get-Command Invoke-GhAppGit).ScriptBlock
            Mock Invoke-GhAppGit {
                & $script:OriginalGhAppGit -Worktree $Worktree -Arguments $Arguments -AllowedExitCodes $AllowedExitCodes -Result:$Result
            } -ParameterFilter { $Arguments[0] -eq 'merge-tree' }
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps -Head $script:BaseUpdateMerges[0] -Base $script:BaseUpdateCommits[0])
            Invoke-ReviewMergeFixture
            Assert-MockCalled Invoke-GhAppGit -Times 2 -Exactly -Scope It -ParameterFilter {
                $Arguments[0] -eq 'merge-tree' -and $Arguments[1] -eq '--write-tree' -and
                $Arguments[2] -eq $script:ReviewedSha -and $Arguments[3] -eq $script:BaseUpdateCommits[0]
            }
        }
        It 'waits for an asynchronous update without sending it again for the unchanged head' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $behind = New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -MergeState BEHIND
            Set-Responses (@((New-PrStep -Pr $behind), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr $behind)) + @(New-ReviewMergeSteps -Head $script:BaseUpdateMerges[0] -Base $script:BaseUpdateCommits[0]))
            Invoke-ReviewMergeFixture
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/update-branch$' }).Count | Should Be 1
        }
        foreach ($state in @('CHANGES_REQUESTED', 'DISMISSED', 'PENDING')) {
            It "rechecks a later $state review after checks on the updated head" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @(New-ReviewMergeSteps -Head $script:CleanMerge)
                $steps[4] = New-ReviewStep -Reviews @((New-Review), (New-Review -State $state -Id 2))
                Set-Responses $steps
                Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no owner approval after review request'
                @(Get-Calls).Count | Should Be 5
            }
        }
        It 'stops waiting for a new head when the PR is no longer behind after an update' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $behind = New-Pr -Head $script:ReviewedSha -MergeState BEHIND
            Set-Responses (@((New-PrStep -Pr $behind), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))),
                (New-ApiStep pulls/7/update-branch PUT @{} -App)) + @(New-ReviewMergeSteps))
            Invoke-ReviewMergeFixture
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/update-branch$' }).Count | Should Be 1
        }
        It 'refuses an unreviewed non-merge head that arrives while checks run' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha)), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))), (New-ChecksStep),
                (New-PrStep -Pr (New-Pr -Head $script:NonMerge)), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: head $script:NonMerge adds commits after review request $script:ReviewedSha; owner approval and a new review request required."
            @(Get-Calls).Count | Should Be 5
        }
        It 'proves a real clean same-file merge through merge-tree without changing refs, index, or worktree' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $refs = Invoke-GhAppGit $script:ReviewGitRoot @('show-ref', '--head')
            $index = [IO.File]::ReadAllBytes((Join-Path $script:ReviewGitRoot '.git/index'))
            $data = [IO.File]::ReadAllText($script:ReviewData)
            Set-Responses @(New-ReviewMergeSteps -Head $script:CleanMerge)
            Invoke-ReviewMergeFixture
            (Invoke-GhAppGit $script:ReviewGitRoot @('show-ref', '--head')) | Should Be $refs
            [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $script:ReviewGitRoot '.git/index'))) | Should Be ([Convert]::ToBase64String($index))
            [IO.File]::ReadAllText($script:ReviewData) | Should Be $data
            [IO.File]::Exists((Join-Path $script:ReviewGitRoot '.git/FETCH_HEAD')) | Should Be $false
        }
        It 'refuses a same-line conflict resolved wholly to the base file' {
            (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', '--verify', "$script:BaseResolvedConflictMerge^{tree}")).Trim() |
                Should Be (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', '--verify', "$script:ConflictBase^{tree}")).Trim()
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:BaseResolvedConflictMerge -BaseHead $script:ConflictBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:BaseResolvedConflictMerge))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be '#7 stop: recomputed merge conflicts; manual resolution requires owner approval.'
            @(Get-Calls).Count | Should Be 2
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
        }
        It 'refuses a same-line conflict resolved wholly to the first parent with an unchanged tree' {
            (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', '--verify', "$script:FirstParentResolvedConflictMerge^{tree}")).Trim() |
                Should Be (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', '--verify', "$script:ReviewedSha^{tree}")).Trim()
            (Invoke-GhAppGit $script:ReviewGitRoot @('diff', '--name-only', $script:ReviewedSha, $script:FirstParentResolvedConflictMerge, '--')) |
                Should Be ''
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:FirstParentResolvedConflictMerge -BaseHead $script:ConflictBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:FirstParentResolvedConflictMerge))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be '#7 stop: recomputed merge conflicts; manual resolution requires owner approval.'
            @(Get-Calls).Count | Should Be 2
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
        }
        foreach ($case in @(
            @{ Name = 'non-merge commit'; Head = $script:NonMerge; Base = $script:ReviewBase; Message = "#7 stop: head $script:NonMerge adds commits after review request $script:ReviewedSha; owner approval and a new review request required." },
            @{ Name = 'second parent outside the base'; Head = $script:CleanMerge; Base = $script:ReviewRoot; Message = "#7 stop: second parent $script:ReviewBase is not contained in main; owner approval and a new review request required." },
            @{ Name = 'manual merge change'; Head = $script:ManualMerge; Base = $script:ReviewBase; Message = '#7 stop: merge tree differs from the clean automatic merge; manual resolution requires owner approval.' },
            @{ Name = 'recomputed conflict'; Head = $script:ConflictMerge; Base = $script:ConflictBase; Message = '#7 stop: recomputed merge conflicts; manual resolution requires owner approval.' },
            @{ Name = '21-commit walk'; Head = $script:BaseUpdateMerges[20]; Base = $script:BaseUpdateCommits[20]; Message = "#7 stop: review request $script:ReviewedSha was not reached within 20 first-parent commits from $($script:BaseUpdateMerges[20])." }
        )) {
            It "refuses a $($case.Name) even when commit_id moved to H" {
                Add-LedgerFixture -Head $script:ReviewedSha
                Set-Responses @((New-PrStep -Pr (New-Pr -Head $case.Head -BaseHead $case.Base)),
                    (New-ReviewStep -Reviews @((New-Review -Head $case.Head))))
                Get-Message { Invoke-ReviewMergeFixture } | Should Be $case.Message
                @(Get-Calls).Count | Should Be 2
                [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
            }
        }
        It 'allows a walk of exactly 20 clean base merges' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps -Head $script:BaseUpdateMerges[19] -Base $script:BaseUpdateCommits[19])
            Invoke-ReviewMergeFixture
        }
        It 'stops after three updates if the branch remains behind' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $steps = @()
            for ($round = 0; $round -le 3; $round++) {
                $head = $(if ($round -eq 0) { $script:ReviewedSha } else { $script:BaseUpdateMerges[$round - 1] })
                $steps += New-PrStep -Pr (New-Pr -Head $head -BaseHead $script:BaseUpdateCommits[$round] -MergeState BEHIND)
                $steps += New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))
                if ($round -lt 3) { $steps += New-ApiStep pulls/7/update-branch PUT @{} -App }
            }
            Set-Responses $steps
            Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: still behind main after three updates.'
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/update-branch$' }).Count | Should Be 3
        }
        It 'stops if an update exposes DIRTY' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -MergeState BEHIND)), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))),
                (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -MergeState DIRTY)), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: merge conflict with main; manual resolution requires owner approval.'
        }
        It 'requires an unchanged review base' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $pr = New-Pr -Head $script:ReviewedSha
            $pr.baseRefName = 'release'
            Set-Responses @((New-PrStep -Pr $pr), (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'review request base changed'
        }
        It 'rechecks the ledger and rejects a newer request made while checks run' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $script:LedgerReads = 0
            Mock Get-GhAppReviewRequest {
                $script:LedgerReads++
                [pscustomobject]@{ Head = $script:ReviewedSha; Base = 'main'; RequestedAt = [DateTimeOffset]$(
                    if ($script:LedgerReads -eq 1) { '2025-12-31T23:00:00Z' } else { '2026-01-01T01:00:00Z' }) }
            }
            Set-Responses @(New-ReviewMergeSteps)
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no owner approval after review request'
            @(Get-Calls).Count | Should Be 5
        }
    }

    Describe 'Local fetch and observational timeline' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'fetches only explicit objects using normal Git credentials without refs or FETCH_HEAD' {
            $script:FetchArguments = $null
            $script:FetchOwnerCredentials = $false
            Mock Invoke-GhAppGit {
                if ($Arguments[0] -eq 'rev-parse') {
                    if ($Arguments[1] -eq '--is-inside-work-tree') { return 'true' }
                    return 'false'
                }
                $script:FetchArguments = $Arguments
                $script:FetchOwnerCredentials = [bool]$OwnerCredentials
                return ''
            }
            Sync-GhAppReviewCommits (Get-GhAppContext '') $script:ReviewGitRoot @($script:ReviewedSha, $script:CleanMerge, $script:ReviewBase)
            $script:FetchOwnerCredentials | Should Be $true
            ($script:FetchArguments -join ' ') | Should Be "-c fetch.writeCommitGraph=false fetch --no-tags --no-prune --refmap= --no-write-fetch-head --no-auto-maintenance --recurse-submodules=no https://github.com/$script:RepositoryName.git $script:ReviewedSha $script:CleanMerge $script:ReviewBase"
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects Git older than 2.38 before recomputing a base merge' {
            Mock Sync-GhAppReviewCommits {}
            Mock Invoke-GhAppGit {
                switch ($Arguments[0]) {
                    'rev-list' { return "$($script:BaseUpdateMerges[0]) $script:ReviewedSha $($script:BaseUpdateCommits[0])" }
                    'merge-base' { return [pscustomobject]@{ ExitCode = 0; Output = '' } }
                    '--version' { return 'git version 2.37.9' }
                    default { throw 'Unexpected Git call.' }
                }
            }
            $request = [pscustomobject]@{ Head = $script:ReviewedSha; Base = 'main' }
            Get-Message { Assert-GhAppReviewHistory (Get-GhAppContext '') 7 $script:ReviewGitRoot (New-Pr -Head $script:BaseUpdateMerges[0] -BaseHead $script:BaseUpdateCommits[0]) $request } |
                Should Be '#7 stop: clean merge proof needs Git 2.38 or later for merge-tree --write-tree.'
            Assert-MockCalled Invoke-GhAppGit -Times 0 -Exactly -Scope It -ParameterFilter { $Arguments[0] -eq 'merge-tree' }
        }
        It 'observes the last timeline commit before the selected approval across pages' {
            $script:TimelineBodies = @()
            $script:TimelinePage = 0
            Mock Invoke-GhAppGh {
                $script:TimelineBodies += ConvertFrom-Json $InputText
                $script:TimelinePage++
                $nodes = $(if ($script:TimelinePage -eq 1) {
                    @(@{ __typename = 'PullRequestCommit'; commit = @{ oid = $script:HeadSha } })
                } else {
                    @(@{ __typename = 'PullRequestCommit'; commit = @{ oid = $script:NextSha } },
                        @{ __typename = 'PullRequestReview'; fullDatabaseId = '4294967296' },
                        @{ __typename = 'PullRequestCommit'; commit = @{ oid = $script:MergeSha } })
                })
                [pscustomobject]@{ Output = (ConvertTo-Json @{ data = @{ repository = @{ pullRequest = @{
                    timelineItems = @{ nodes = $nodes; pageInfo = @{ hasNextPage = ($script:TimelinePage -eq 1); endCursor = 'next-page' } }
                } } } } -Depth 15 -Compress) }
            }
            $approval = New-Review
            $approval.id = 4294967296L
            Get-GhAppTimelineObservation (Get-GhAppContext '') 7 $approval | Should Be $script:NextSha
            $script:TimelineBodies[1].variables.cursor | Should Be 'next-page'
            $script:TimelineBodies[0].query | Should Match 'PullRequestCommit'
            $script:TimelineBodies[0].query | Should Match 'fullDatabaseId'
        }
    }

    Describe 'Timeline failure remains observational' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'does not let a failed timeline request block an otherwise proven merge' {
            Add-LedgerFixture
            $steps = @(New-MergeSteps)[0..6]
            $steps += @{ Arguments = @('api', 'graphql', '--hostname', 'github.com', '--method', 'POST', '--input', '-');
                App = $false; Output = ''; Error = 'HTTP 503'; ExitCode = 1 }
            Set-Responses $steps
            Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:ReviewGitRoot -LogPath $script:LogFile
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'timeline observation only: last PullRequestCommit before approval unavailable'
            @(Get-Calls)[7].AppToken | Should Be $false
        }
    }

    Describe 'Close and optional test branch deletion' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'comments before closing and leaves the branch by default' {
            Set-Responses @((New-PrStep), (New-ApiStep issues/7/comments POST @{} -App), (New-ApiStep pulls/7 PATCH @{ state = 'closed' } -App))
            Close-GhAppPullRequest 7 $script:BodyFile
            @(Get-Calls).Count | Should Be 3
            (ConvertFrom-Json @(Get-Calls)[2].Input).state | Should Be closed
            Assert-NoToken
        }
        It 'deletes only a matching test branch after commenting and closing' {
            Set-Responses @((New-PrStep), (New-ApiStep issues/7/comments POST @{} -App), (New-ApiStep pulls/7 PATCH @{ state = 'closed' } -App),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -RawOutput ''))
            Close-GhAppPullRequest 7 $script:BodyFile -DeleteBranch -BranchPrefix 'test/'
            @(Get-Calls).Count | Should Be 5
            Assert-NoToken
        }
        It 'requires a deletion prefix before any gh call' {
            Get-Message { Close-GhAppPullRequest 7 $script:BodyFile -DeleteBranch } | Should Be 'DeleteBranch requires a nonempty BranchPrefix.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'refuses to read the configured DPAPI file as a comment' {
            Get-Message { Close-GhAppPullRequest 7 $script:ConfigValues.dpapiPath } |
                Should Be 'The body or message file must not name the configured DPAPI file.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'refuses a prefix mismatch before commenting or closing' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Branch feature/other)))
            Get-Message { Close-GhAppPullRequest 7 $script:BodyFile -DeleteBranch -BranchPrefix 'test/' } |
                Should Be 'Head branch does not satisfy BranchPrefix; deletion refused.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses a changed branch after closing' {
            Set-Responses @((New-PrStep), (New-ApiStep issues/7/comments POST @{} -App), (New-ApiStep pulls/7 PATCH @{ state = 'closed' } -App),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:NextSha } }))
            Get-Message { Close-GhAppPullRequest 7 $script:BodyFile -DeleteBranch -BranchPrefix 'test/' } | Should Be 'Head branch changed; deletion refused.'
            @(Get-Calls).Count | Should Be 4
        }
        It 'does not close when commenting fails' {
            Set-Responses @((New-PrStep), (New-ApiStep issues/7/comments POST -App -ExitCode 1 -ErrorText 'HTTP 403 __TOKEN__'))
            Get-Message { Close-GhAppPullRequest 7 $script:BodyFile } | Should Be "HTTP 403 /repos/$script:RepositoryName/issues/7/comments"
            @(Get-Calls).Count | Should Be 2
            Assert-NoToken
        }
        It 'does not delete a branch when closing fails' {
            Set-Responses @((New-PrStep), (New-ApiStep issues/7/comments POST @{} -App),
                (New-ApiStep pulls/7 PATCH -App -ExitCode 1 -ErrorText 'HTTP 409 __TOKEN__'))
            Get-Message { Close-GhAppPullRequest 7 $script:BodyFile -DeleteBranch -BranchPrefix 'test/' } |
                Should Be "HTTP 409 /repos/$script:RepositoryName/pulls/7"
            @(Get-Calls).Count | Should Be 3
            Assert-NoToken
        }
    }
}
