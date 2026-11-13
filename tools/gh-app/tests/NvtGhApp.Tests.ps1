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
    $script:FixtureOwnerConfig = Join-Path $script:FixtureRoot 'example-owner.owner-default.json'
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
    function Get-HelperRecords {
        if ([IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS)) {
            @(Get-Content -LiteralPath $env:NVT_GHAPP_HELPER_CALLS | ForEach-Object { ConvertFrom-Json $_ })
        } else { @() }
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
            [string]$Author = 'app/example-app', [string]$BaseHead = $script:NextSha, [string]$BaseName = 'main')
        @{ state = $State; headRefOid = $Head; headRefName = $Branch; baseRefName = $BaseName;
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
        foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX, $env:NVT_GHAPP_HELPER_CALLS, $script:LogFile, $script:LedgerFile,
            $script:FixtureOwnerConfig)) {
            if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
        foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN', 'GH_REPO', 'GH_HOST', 'GH_DEBUG', 'NVT_GHAPP_HELPER_FAIL', 'NVT_GHAPP_HELPER_MESSAGE')) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        $helper = Join-Path $script:FixtureRoot 'helper.ps1'
        $helperSource = @'
param($Mode, $Owner, $Repo, $ClientId, $InstallationId, $DpapiPath, [switch]$IncludeWorkflowsWrite, [switch]$IncludeIssuesWrite)
$record = @{ Arguments = $PSBoundParameters; TokenPresent = [bool]$env:GH_TOKEN; DebugPresent = [bool]$env:GH_DEBUG }
[IO.File]::AppendAllText($env:NVT_GHAPP_HELPER_CALLS, (ConvertTo-Json $record -Compress) + "`n")
# NVT_GHAPP_HELPER_MESSAGE makes every call fail with that text on stderr.
if ($env:NVT_GHAPP_HELPER_MESSAGE) { [Console]::Error.Write($env:NVT_GHAPP_HELPER_MESSAGE); exit 1 }
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
            [string]$RequestedAt = '2025-12-31T23:00:00Z', [string]$Owner = $script:Owner, [string]$Repo = $script:RepoName,
            [string]$PatchId, [string]$Base = 'main')
        $entry = [ordered]@{ owner = $Owner; repo = $Repo; number = $Number; head = $Head; requestedAt = $RequestedAt; base = $Base }
        if ($PatchId) { $entry.patchId = $PatchId }
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
        BeforeEach {
            Reset-Fixture
            Mock Get-GhAppConfigPath { $script:FixtureConfig }
            Mock Get-GhAppOwnerConfigPath { $script:FixtureOwnerConfig }
        }
        # The owner default is the repository config without its repo field. Only a repository with no config of its own uses it.
        function Write-OwnerDefault {
            param([hashtable]$Values = $script:ConfigValues.Clone(), [switch]$KeepRepo)
            if (-not $KeepRepo) { $Values.Remove('repo') }
            [IO.File]::WriteAllText($script:FixtureOwnerConfig, (ConvertTo-Json $Values))
        }
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
        It 'rejects a missing config file and names both expected paths' {
            [IO.File]::Delete($script:FixtureConfig)
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } |
                Should Be "The repository config file is missing. Expected $script:FixtureConfig or the owner default $script:FixtureOwnerConfig."
            @(Get-Calls).Count | Should Be 0
        }
        It 'imports the owner default for a repository with no config file of its own' {
            [IO.File]::Delete($script:FixtureConfig)
            Write-OwnerDefault
            $config = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            $config.owner | Should Be $script:Owner
            $config.repo | Should Be $script:RepoName
            $config.botLogin | Should Be 'example-app[bot]'
            (Get-GhAppContext '').repo | Should Be $script:RepoName
            @(Get-Calls).Count | Should Be 0
        }
        It 'prefers the repository config file over the owner default' {
            $other = $script:ConfigValues.Clone()
            $other.botLogin = 'owner-default[bot]'
            Write-OwnerDefault $other
            (Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName).botLogin | Should Be 'example-app[bot]'
        }
        It 'still rejects a repository config mismatch when an owner default exists' {
            Write-OwnerDefault
            $script:ConfigValues.repo = 'other-repo'
            [IO.File]::WriteAllText($script:FixtureConfig, (ConvertTo-Json $script:ConfigValues))
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Repository mismatch. Expected $script:RepositoryName."
        }
        It 'rejects an owner default for another owner' {
            [IO.File]::Delete($script:FixtureConfig)
            $other = $script:ConfigValues.Clone()
            $other.owner = 'other-owner'
            Write-OwnerDefault $other
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Repository mismatch. Expected $script:RepositoryName."
            Get-Message { Get-GhAppContext '' } | Should Be 'Import-GhAppConfig must succeed before using this module.'
        }
        It 'rejects an owner default that names a repository' {
            [IO.File]::Delete($script:FixtureConfig)
            Write-OwnerDefault -KeepRepo
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } |
                Should Be "The owner default config file must not contain a 'repo' field."
        }
        It 'rejects an owner default stored inside a worktree' {
            [IO.File]::Delete($script:FixtureConfig)
            $repository = Join-Path $script:FixtureRoot 'repo-owner-default'
            [void][IO.Directory]::CreateDirectory((Join-Path $repository '.git'))
            $inside = Join-Path $repository 'example-owner.owner-default.json'
            Mock Get-GhAppOwnerConfigPath { $inside }
            $values = $script:ConfigValues.Clone()
            $values.Remove('repo')
            [IO.File]::WriteAllText($inside, (ConvertTo-Json $values))
            try {
                Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be 'The config file must be outside every repository.'
            } finally { [IO.Directory]::Delete($repository, $true) }
        }
        foreach ($fieldName in @('clientId', 'installationId', 'tokenHelperPath', 'dpapiPath', 'botLogin')) {
            It "applies the repository checks to the owner default: missing $fieldName" {
                [IO.File]::Delete($script:FixtureConfig)
                $values = $script:ConfigValues.Clone()
                $values.Remove($fieldName)
                Write-OwnerDefault $values
                Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be "Config field '$fieldName' is required."
            }
        }
        It 'rejects an owner default helper inside a repository' {
            [IO.File]::Delete($script:FixtureConfig)
            $repository = Join-Path $script:FixtureRoot 'repo-owner-default-helper'
            [void][IO.Directory]::CreateDirectory((Join-Path $repository '.git'))
            $inside = Join-Path $repository 'file.ps1'
            [IO.File]::Copy($script:ConfigValues.tokenHelperPath, $inside, $true)
            $values = $script:ConfigValues.Clone()
            $values.tokenHelperPath = $inside
            Write-OwnerDefault $values
            try {
                Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } |
                    Should Be "Config field 'tokenHelperPath' must be outside every repository."
            } finally { [IO.Directory]::Delete($repository, $true) }
        }
        It 'does not let a review ledger or an issues log overwrite the owner default' {
            [IO.File]::Delete($script:FixtureConfig)
            Write-OwnerDefault
            $null = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            Get-Message { Get-GhAppLedgerPath (Get-GhAppContext '') $script:FixtureOwnerConfig } |
                Should Be 'The review ledger must not name a config, helper, or DPAPI file.'
            Get-Message { Get-GhAppIssuesLogPath (Get-GhAppContext '') $script:FixtureOwnerConfig } |
                Should Be 'The issues write log must not name a config, helper, or DPAPI file.'
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
            'Add-GhAppReviewRecord', 'Request-GhAppOwnerReview', 'Merge-GhAppApprovedPullRequest', 'Close-GhAppPullRequest', 'Invoke-GhAppRead',
            'Set-GhAppPullRequestReady', 'Set-GhAppPullRequestBase')) {
            It "rejects another repository in $publicFunction before gh" {
                Import-Fixture
                $parameters = @{ Repository = 'other-owner/other-repo' }
                switch ($publicFunction) {
                    'Push-GhAppBranch' { $parameters += @{ Worktree = $script:FixtureRoot; LocalBase = $script:HeadSha; RemoteParent = $script:HeadSha; Branch = 'test/module'; MessageFile = $script:BodyFile } }
                    'New-GhAppPullRequest' { $parameters += @{ Head = 'test/module'; Title = 'Synthetic title'; BodyFile = $script:BodyFile } }
                    'Merge-GhAppApprovedPullRequest' { $parameters += @{ Numbers = @(7); Worktree = $script:FixtureRoot; LogPath = $script:LogFile } }
                    'Request-GhAppOwnerReview' { $parameters += @{ Number = 7 } }
                    'Set-GhAppPullRequestReady' { $parameters += @{ Number = 7; ExpectedHeadSha = $script:HeadSha; Worktree = $script:FixtureRoot } }
                    'Set-GhAppPullRequestBase' { $parameters += @{ Number = 7; NewBase = 'release'; ExpectedHeadSha = $script:HeadSha; Worktree = $script:FixtureRoot; LogPath = $script:LogFile } }
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
        It 'closes an incorrect-author PR through the App before throwing when requested' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App),
                (New-ApiStep pulls/7 -Value @{ user = @{ login = 'other-author' } }),
                (New-ApiStep pulls/7 PATCH @{ state = 'closed' } -App))
            Get-Message { New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile -CloseOnWrongAuthor } |
                Should Be "PR author does not match botLogin. #7 was closed. https://github.com/$script:RepositoryName/pull/7"
            $calls = @(Get-Calls)
            $calls.Count | Should Be 3
            (ConvertFrom-Json $calls[2].Input).state | Should Be closed
            $calls[2].AppToken | Should Be $true
            Assert-NoToken
        }
        It 'leaves a correct-author PR open with CloseOnWrongAuthor and registers it, because it is not a draft' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App),
                (New-ApiStep pulls/7 -Value @{ user = @{ login = 'example-app[bot]' } }), (New-PrStep))
            $result = New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile -CloseOnWrongAuthor
            $result.Number | Should Be 7
            $result.Url | Should Be "https://github.com/$script:RepositoryName/pull/7"
            $result.ReviewRequest.head | Should Be $script:HeadSha
            @(Get-Calls).Count | Should Be 3
            Assert-NoToken ($result | Out-String)
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

    Describe 'Issues write comment' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            $script:IssuesLog = Join-Path $script:FixtureRoot 'issues.log'
            if ([IO.File]::Exists($script:IssuesLog)) { [IO.File]::Delete($script:IssuesLog) }
        }
        It 'keeps the flag off for an ordinary comment even with a log path' {
            Set-Responses @((New-ApiStep issues/9/comments POST @{ id = 1 } -App))
            $null = Add-GhAppComment 9 $script:BodyFile -IssuesLogPath $script:IssuesLog
            @(Get-Calls).Count | Should Be 1
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 1
            foreach ($record in $records) { @($record.Arguments.PSObject.Properties.Name) -contains 'IncludeIssuesWrite' | Should Be $false }
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
            Assert-NoToken
        }
        It 'requires a log path with the switch before any call' {
            Get-Message { Add-GhAppComment 9 $script:BodyFile -IncludeIssuesWrite } |
                Should Be 'IncludeIssuesWrite requires IssuesLogPath.'
            @(Get-Calls).Count | Should Be 0
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
        }
        It 'refuses a pull request after a read without logging or writing' {
            Set-Responses @((New-ApiStep issues/7 -Value @{ number = 7; pull_request = @{} }))
            Get-Message { Add-GhAppComment 7 $script:BodyFile -IncludeIssuesWrite -IssuesLogPath $script:IssuesLog } |
                Should Be 'IncludeIssuesWrite refused: #7 is a pull request.'
            $calls = @(Get-Calls)
            $calls.Count | Should Be 1
            $calls[0].AppToken | Should Be $false
            $calls[0].Arguments[-1] | Should Be GET
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
        }
        It 'requests permission for one issue comment and writes one UTC log line without a token' {
            Set-Responses @((New-ApiStep issues/9 -Value @{ number = 9 }),
                (New-ApiStep issues/9/comments POST @{ id = 1 } -App),
                (New-ApiStep issues/9/comments POST @{ id = 2 } -App))
            $result = Add-GhAppComment 9 $script:BodyFile -IncludeIssuesWrite -IssuesLogPath $script:IssuesLog
            $result.id | Should Be 1
            $calls = @(Get-Calls)
            $calls.Count | Should Be 2
            $calls[0].AppToken | Should Be $false
            $calls[1].AppToken | Should Be $true
            (ConvertFrom-Json $calls[1].Input).body | Should Be "A synthetic description.`nSecond line."
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 1
            $records[0].Arguments.IncludeIssuesWrite | Should Be $true
            @($records[0].Arguments.PSObject.Properties.Name) -contains 'IncludeWorkflowsWrite' | Should Be $false
            $lines = @(Get-Content -LiteralPath $script:IssuesLog)
            $lines.Count | Should Be 1
            $lines[0] -match '^\d{4}-\d\d-\d\dT[\d:.]+\+00:00 issues-write comment number=9$' | Should Be $true
            Assert-NoToken ($lines | Out-String)
            (Get-GhAppContext).ContainsKey('includeIssuesWrite') | Should Be $false
            # The next call makes only its normal POST and does not request or log extra permission.
            (Add-GhAppComment 9 $script:BodyFile).id | Should Be 2
            @(Get-Calls).Count | Should Be 3
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 2
            @($records[1].Arguments.PSObject.Properties.Name) -contains 'IncludeIssuesWrite' | Should Be $false
            [IO.File]::ReadAllText($script:IssuesLog) | Should Be ($lines[0] + [Environment]::NewLine)
        }
        It 'does not request a token or post when the log cannot be written' {
            Set-Responses @((New-ApiStep issues/9 -Value @{ number = 9 }))
            Get-Message { Add-GhAppComment 9 $script:BodyFile -IncludeIssuesWrite -IssuesLogPath $script:FixtureRoot } |
                Should Be 'The issues write log could not be written.'
            $calls = @(Get-Calls)
            $calls.Count | Should Be 1
            $calls[0].AppToken | Should Be $false
            $calls[0].Arguments[-1] | Should Be GET
            @(Get-HelperRecords).Count | Should Be 0
        }
        It 'shows the fixed message when the installation lacks the issues permission' {
            $env:NVT_GHAPP_HELPER_MESSAGE = 'Installation token lacks the requested issues permission. ghs_' + 'FAKE'
            Set-Responses @((New-ApiStep issues/9 -Value @{ number = 9 }))
            $message = Get-Message { Add-GhAppComment 9 $script:BodyFile -IncludeIssuesWrite -IssuesLogPath $script:IssuesLog }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/issues/9/comments (token helper exit 1): Installation token lacks the requested issues permission. (after 3 attempts)"
            @(Get-Calls).Count | Should Be 1
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 3
            foreach ($record in $records) { $record.Arguments.IncludeIssuesWrite | Should Be $true }
            @(Get-Content -LiteralPath $script:IssuesLog).Count | Should Be 1
            Assert-NoToken $message
        }
    }

    Describe 'Label sync' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            $script:IssuesLog = Join-Path $script:FixtureRoot 'issues.log'
            if ([IO.File]::Exists($script:IssuesLog)) { [IO.File]::Delete($script:IssuesLog) }
            $script:LabelFile = Join-Path $script:FixtureRoot 'labels.json'
            [IO.File]::WriteAllText($script:LabelFile, '{"labels":[{"name":"needs-owner","color":"B60205","description":"Owner approval required."}]}')
        }
        It 'plans a create without a token or a write in a dry run' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @()))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile)
            $plan.Count | Should Be 1
            $plan[0].Action | Should Be 'create'
            $plan[0].Name | Should Be 'needs-owner'
            $plan[0].Color | Should Be 'b60205'
            $calls = @(Get-Calls)
            $calls.Count | Should Be 1
            $calls[0].AppToken | Should Be $false
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
        }
        It 'requires a log path with -Apply before any call' {
            Get-Message { Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply } | Should Be 'Apply requires IssuesLogPath.'
            @(Get-Calls).Count | Should Be 0
            @(Get-HelperRecords).Count | Should Be 0
        }
        It 'creates a missing label with one permission request and one log line' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(@{ name = 'bug'; color = 'd73a4a'; description = 'x' })),
                (New-ApiStep labels POST @{ name = 'needs-owner' } -App))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath $script:IssuesLog)
            $plan[0].Action | Should Be 'create'
            $calls = @(Get-Calls)
            $calls.Count | Should Be 2
            $calls[1].AppToken | Should Be $true
            $body = ConvertFrom-Json $calls[1].Input
            $body.name | Should Be 'needs-owner'
            $body.color | Should Be 'b60205'
            $body.description | Should Be 'Owner approval required.'
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 1
            $records[0].Arguments.IncludeIssuesWrite | Should Be $true
            $lines = @(Get-Content -LiteralPath $script:IssuesLog)
            $lines.Count | Should Be 1
            $lines[0] -match '^\d{4}-\d\d-\d\dT[\d:.]+\+00:00 issues-write label create name=needs-owner$' | Should Be $true
            (Get-GhAppContext).ContainsKey('includeIssuesWrite') | Should Be $false
            Assert-NoToken ($lines | Out-String)
        }
        It 'patches the changed label by its definition name, sends all three fields, leaves other labels and sends no DELETE' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(
                        @{ name = 'Needs-Owner'; color = 'FFFFFF'; description = 'Owner approval required.' },
                        @{ name = 'unrelated'; color = '000000'; description = '' })),
                (New-ApiStep labels/needs-owner PATCH @{ name = 'needs-owner' } -App))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath $script:IssuesLog)
            $plan.Count | Should Be 1
            $plan[0].Action | Should Be 'update'
            $plan[0].Changes | Should Be 'name,color'
            $calls = @(Get-Calls)
            $calls.Count | Should Be 2
            $calls[1].Arguments -contains 'PATCH' | Should Be $true
            $calls[1].Arguments -contains 'DELETE' | Should Be $false
            $calls[1].Arguments -contains "/repos/$script:RepositoryName/labels/needs-owner" | Should Be $true
            @($calls | Where-Object { $_.Arguments -contains 'DELETE' }).Count | Should Be 0
            $body = ConvertFrom-Json $calls[1].Input
            $body.new_name | Should Be 'needs-owner'
            $body.color | Should Be 'b60205'
            $body.description | Should Be 'Owner approval required.'
            ([IO.File]::ReadAllText($script:IssuesLog)) -match 'issues-write label update name=needs-owner' | Should Be $true
        }
        It 'plans an update in a dry run without a token, a write or a log line' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(
                        @{ name = 'NEEDS-OWNER'; color = 'b60205'; description = 'Owner approval required.' })))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile)
            $plan[0].Action | Should Be 'update'
            $plan[0].Changes | Should Be 'name'
            @(Get-Calls).Count | Should Be 1
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
        }
        It 'asks for one permission per changed label and writes one log line per write' {
            $two = Join-Path $script:FixtureRoot 'two-labels.json'
            [IO.File]::WriteAllText($two, '{"labels":[{"name":"needs-owner","color":"B60205","description":"Owner approval required."},{"name":"extra","color":"0E8A16","description":"Second."}]}')
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(
                        @{ name = 'needs-owner'; color = 'FFFFFF'; description = 'Owner approval required.' })),
                (New-ApiStep labels/needs-owner PATCH @{ name = 'needs-owner' } -App),
                (New-ApiStep labels POST @{ name = 'extra' } -App))
            $plan = @(Sync-GhAppLabels -DefinitionPath $two -Apply -IssuesLogPath $script:IssuesLog)
            @($plan | ForEach-Object Action) -join ',' | Should Be 'update,create'
            @(Get-Calls).Count | Should Be 3
            @(Get-HelperRecords).Count | Should Be 2
            @(Get-Content -LiteralPath $script:IssuesLog).Count | Should Be 2
            (Get-GhAppContext).ContainsKey('includeIssuesWrite') | Should Be $false
        }
        It 'does not match a remote name that only folds to the same ASCII letters' {
            $kind = Join-Path $script:FixtureRoot 'kind.json'
            [IO.File]::WriteAllText($kind, '{"labels":[{"name":"kind","color":"B60205","description":"d"}]}')
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(
                        @{ name = "$([char]0x212A)ind"; color = 'b60205'; description = 'd' })))
            $plan = @(Sync-GhAppLabels -DefinitionPath $kind)
            $plan[0].Action | Should Be 'create'
            $plan[0].Existing | Should Be $null
        }
        It 'finds a label on the second page of a full first page' {
            $page = @(1..100 | ForEach-Object { @{ name = "label-$_"; color = '000000'; description = '' } })
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value $page),
                (New-ApiStep 'labels?per_page=100&page=2' -Value @(
                        @{ name = 'needs-owner'; color = 'b60205'; description = 'Owner approval required.' })))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile)
            $plan[0].Action | Should Be 'unchanged'
            @(Get-Calls).Count | Should Be 2
        }
        It 'resolves a relative definition path against the PowerShell location' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @()))
            Push-Location -LiteralPath $script:FixtureRoot
            try { $plan = @(Sync-GhAppLabels -DefinitionPath 'labels.json') } finally { Pop-Location }
            $plan[0].Action | Should Be 'create'
        }
        It 'refuses a log path inside a repository before any call' {
            $directory = Join-Path $script:FixtureRoot 'labels-log-repo'
            [void][IO.Directory]::CreateDirectory((Join-Path $directory '.git'))
            try {
                Get-Message { Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath (Join-Path $directory 'issues.log') } |
                    Should Be 'The issues write log must be outside every repository.'
                @(Get-Calls).Count | Should Be 0
                @(Get-HelperRecords).Count | Should Be 0
            } finally { [IO.Directory]::Delete($directory, $true) }
        }
        It 'refuses the helper file as the log path before any call' {
            Get-Message { Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath (Get-GhAppContext).tokenHelperPath } |
                Should Be 'The issues write log must not name a config, helper, or DPAPI file.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'makes no write, no token request and no log line when the label is current' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @(
                        @{ name = 'needs-owner'; color = 'b60205'; description = 'Owner approval required.' })))
            $plan = @(Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath $script:IssuesLog)
            $plan[0].Action | Should Be 'unchanged'
            @(Get-Calls).Count | Should Be 1
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:IssuesLog) | Should Be $false
        }
        It 'does not post when the log cannot be written' {
            Set-Responses @((New-ApiStep 'labels?per_page=100&page=1' -Value @()))
            Get-Message { Sync-GhAppLabels -DefinitionPath $script:LabelFile -Apply -IssuesLogPath $script:FixtureRoot } |
                Should Be 'The issues write log could not be written.'
            @(Get-Calls).Count | Should Be 1
            @(Get-HelperRecords).Count | Should Be 0
        }
        foreach ($case in @(
                @{ Name = 'a missing file'; Json = $null; Message = 'The label definition file is missing.' },
                @{ Name = 'invalid JSON'; Json = '{'; Message = 'The label definition file is not valid JSON.' },
                @{ Name = 'no labels'; Json = '{"labels":[]}'; Message = 'The label definition file lists no labels.' },
                @{ Name = 'a name with a space'; Json = '{"labels":[{"name":"needs owner","color":"B60205","description":"d"}]}'; Message = 'Invalid label name: needs owner' },
                @{ Name = 'a color with a hash'; Json = '{"labels":[{"name":"a","color":"#B60205","description":"d"}]}'; Message = 'Invalid label color for a.' },
                @{ Name = 'a long description'; Json = ('{"labels":[{"name":"a","color":"B60205","description":"' + ('x' * 101) + '"}]}'); Message = 'Invalid label description for a.' },
                @{ Name = 'a duplicate name'; Json = '{"labels":[{"name":"a","color":"B60205","description":"d"},{"name":"A","color":"B60205","description":"d"}]}'; Message = 'Duplicate label name: A' },
                @{ Name = 'an extra field'; Json = '{"labels":[{"name":"a","color":"B60205","description":"d","x":1}]}'; Message = 'Each label has exactly the fields name, color and description.' },
                @{ Name = 'a missing field'; Json = '{"labels":[{"name":"a","color":"B60205"}]}'; Message = 'Each label has exactly the fields name, color and description.' },
                @{ Name = 'a null label'; Json = '{"labels":[null]}'; Message = 'Each label has exactly the fields name, color and description.' },
                @{ Name = 'a document without labels'; Json = '{}'; Message = 'The label definition file lists no labels.' },
                @{ Name = 'a null document'; Json = 'null'; Message = 'The label definition file lists no labels.' },
                @{ Name = 'a name with a final newline'; Json = '{"labels":[{"name":"a\n","color":"B60205","description":"d"}]}'; Message = "Invalid label name: a`n" },
                @{ Name = 'a number as the name'; Json = '{"labels":[{"name":5,"color":"B60205","description":"d"}]}'; Message = 'Invalid label name: 5' },
                @{ Name = 'a color with a final newline'; Json = '{"labels":[{"name":"a","color":"B60205\n","description":"d"}]}'; Message = 'Invalid label color for a.' },
                @{ Name = 'a color of five digits'; Json = '{"labels":[{"name":"a","color":"B6020","description":"d"}]}'; Message = 'Invalid label color for a.' },
                @{ Name = 'a description with a control character'; Json = '{"labels":[{"name":"a","color":"B60205","description":"d\u0007"}]}'; Message = 'Invalid label description for a.' },
                @{ Name = 'a number as the description'; Json = '{"labels":[{"name":"a","color":"B60205","description":5}]}'; Message = 'Invalid label description for a.' })) {
            It "rejects $($case.Name) before any call" {
                $path = Join-Path $script:FixtureRoot 'bad-labels.json'
                if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
                if ($null -ne $case.Json) { [IO.File]::WriteAllText($path, $case.Json) }
                Get-Message { Sync-GhAppLabels -DefinitionPath $path } | Should Be $case.Message
                @(Get-Calls).Count | Should Be 0
            }
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
        New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $Head } }
        New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -RawOutput ''
    }
    function Invoke-MergeFixture {
        param([int[]]$Numbers = @(7), [int]$TimeoutSeconds = 1200)
        Merge-GhAppApprovedPullRequest -Numbers $Numbers -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1 -TimeoutSeconds $TimeoutSeconds -DeleteBranchPrefix 'test/'
    }

    Describe 'Workflows write push' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            $script:WorkflowsLog = Join-Path $script:FixtureRoot 'workflows.log'
            if ([IO.File]::Exists($script:WorkflowsLog)) { [IO.File]::Delete($script:WorkflowsLog) }
        }
        function Set-WorkflowHead {
            # HEAD becomes the base plus one workflow file. The caller restores HEAD afterwards.
            $identity = @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgSign=false')
            $file = Join-Path $script:GitRoot 'workflow.data'
            [IO.File]::WriteAllText($file, 'name: synthetic')
            $sha = (Invoke-GhAppGit $script:GitRoot @('hash-object', '-w', $file)).Trim()
            $null = Invoke-GhAppGit $script:GitRoot @('read-tree', $script:BaseTree)
            $null = Invoke-GhAppGit $script:GitRoot @('update-index', '--add', '--cacheinfo', "100644,$sha,.github/workflows/ci.yml")
            $tree = (Invoke-GhAppGit $script:GitRoot @('write-tree')).Trim()
            $commit = (Invoke-GhAppGit $script:GitRoot ($identity + @('commit-tree', $tree, '-p', $script:BaseCommit, '-m', 'workflow fixture'))).Trim()
            $null = Invoke-GhAppGit $script:GitRoot @('update-ref', 'HEAD', $commit)
            @{ Tree = $tree; Blob = $sha }
        }
        function Restore-Head {
            $null = Invoke-GhAppGit $script:GitRoot @('update-ref', 'HEAD', $script:LocalHead)
            $null = Invoke-GhAppGit $script:GitRoot @('read-tree', $script:HeadTree)
        }
        It 'keeps the flag off for an ordinary push' {
            Set-Responses @(New-PushSteps)
            $null = Invoke-PushFixture
            $records = @(Get-HelperRecords)
            $records.Count | Should BeGreaterThan 0
            foreach ($record in $records) { @($record.Arguments.PSObject.Properties.Name) -contains 'IncludeWorkflowsWrite' | Should Be $false }
            [IO.File]::Exists($script:WorkflowsLog) | Should Be $false
        }
        It 'refuses the switch when the push changes no workflow file, before any call' {
            Set-Responses @()
            Get-Message {
                Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $script:BaseCommit -RemoteParent $script:HeadSha `
                    -Branch test/module -MessageFile $script:BodyFile -IncludeWorkflowsWrite -WorkflowsLogPath $script:WorkflowsLog
            } | Should Be 'IncludeWorkflowsWrite refused: the push changes no file under .github/workflows/.'
            @(Get-Calls).Count | Should Be 0
            @(Get-HelperRecords).Count | Should Be 0
            [IO.File]::Exists($script:WorkflowsLog) | Should Be $false
        }
        It 'requires a log path with the switch' {
            Set-Responses @()
            Get-Message {
                Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $script:BaseCommit -RemoteParent $script:HeadSha `
                    -Branch test/module -MessageFile $script:BodyFile -IncludeWorkflowsWrite
            } | Should Be 'IncludeWorkflowsWrite requires WorkflowsLogPath.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'passes the flag to every token request of a workflow push and writes one log line without a token' {
            $workflow = Set-WorkflowHead
            try {
                Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @()),
                    (New-ApiStep "git/commits/$script:HeadSha" -Value @{ tree = @{ sha = $script:BaseTree } }),
                    (New-ApiStep git/blobs POST @{ sha = $workflow.Blob } -App),
                    (New-ApiStep git/trees POST @{ sha = $workflow.Tree } -App),
                    (New-ApiStep git/commits POST @{ sha = $script:MergeSha } -App),
                    (New-ApiStep 'git/ref/heads/test%2Fmodule' -ExitCode 1 -ErrorText 'HTTP 404'),
                    (New-ApiStep git/refs POST @{} -App))
                $result = Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $script:BaseCommit -RemoteParent $script:HeadSha `
                    -Branch test/module -MessageFile $script:BodyFile -IncludeWorkflowsWrite -WorkflowsLogPath $script:WorkflowsLog
            } finally { Restore-Head }
            $result.Files | Should Be 1
            $records = @(Get-HelperRecords)
            $records.Count | Should Be 4
            foreach ($record in $records) { $record.Arguments.IncludeWorkflowsWrite | Should Be $true }
            $lines = @(Get-Content -LiteralPath $script:WorkflowsLog)
            $lines.Count | Should Be 1
            $lines[0] -match '^\d{4}-\d\d-\d\dT[\d:.]+\+00:00 workflows-write push branch=test/module files=1 workflowFiles=1$' | Should Be $true
            Assert-NoToken ($lines | Out-String)
            # The stored configuration is unchanged, so the next push is an ordinary one.
            (Get-GhAppContext).ContainsKey('includeWorkflowsWrite') | Should Be $false
        }
        It 'shows the fixed message when the installation lacks the workflows permission' {
            $workflow = Set-WorkflowHead
            $env:NVT_GHAPP_HELPER_MESSAGE = 'Installation token lacks the requested workflows permission. ghs_' + 'FAKE'
            try {
                Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @()),
                    (New-ApiStep "git/commits/$script:HeadSha" -Value @{ tree = @{ sha = $script:BaseTree } }))
                $message = Get-Message {
                    Push-GhAppBranch -Worktree $script:GitRoot -LocalBase $script:BaseCommit -RemoteParent $script:HeadSha `
                        -Branch test/module -MessageFile $script:BodyFile -IncludeWorkflowsWrite -WorkflowsLogPath $script:WorkflowsLog
                }
            } finally { Restore-Head }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/git/blobs (token helper exit 1): Installation token lacks the requested workflows permission. (after 3 attempts)"
            Assert-NoToken $message
        }
        It 'does not show any other helper error text' {
            $env:NVT_GHAPP_HELPER_MESSAGE = 'some other failure ghs_' + 'FAKE'
            Set-Responses @((New-ApiStep pulls/7 PATCH @{} -App))
            $message = Get-Message { Set-GhAppPullRequestBody 7 $script:BodyFile }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/pulls/7 (token helper exit 1) (after 3 attempts)"
            Assert-NoToken $message
        }
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
            @(Get-Calls).Count | Should Be 22
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
        It 'warns when the merged branch is kept because no delete prefix was given' {
            Set-Responses @(@(New-MergeSteps)[0..6])
            $output = & { Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1 } 3>&1
            $warnings = @($output | Where-Object { $_ -is [Management.Automation.WarningRecord] })
            $warnings.Count | Should Be 1
            $warnings[0].Message | Should Match 'kept branch test/module because no delete prefix was given'
            $warnings[0].Message | Should Match 'Remove-GhAppMergedBranch'
        }
        It 'does not warn when a delete prefix is given, even if the branch does not match it' {
            $steps = @(New-MergeSteps)
            foreach ($index in @(0, 3)) { $steps[$index] = New-PrStep -Pr (New-Pr -Branch '1.2.2') }
            $steps[6] = New-PrStep -Pr (New-Pr -Branch '1.2.2' -State MERGED)
            Set-Responses @($steps[0..6])
            $output = & { Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:GitRoot -LogPath $script:LogFile -PollSeconds 1 -DeleteBranchPrefix 'feature/' } 3>&1
            @($output | Where-Object { $_ -is [Management.Automation.WarningRecord] }).Count | Should Be 0
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
            @(Get-Calls).Count | Should Be 11
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
            @(Get-Calls).Count | Should Be 14
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
        It 'keeps a branch that changed between the last checks and the delete' {
            $steps = @(New-MergeSteps)
            $steps[9].Output = ConvertTo-Json @{ object = @{ sha = $script:NextSha } } -Compress
            Set-Responses $steps
            Invoke-MergeFixture
            @(Get-Calls).Count | Should Be 10
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'ref changed before delete'
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
        # Rebased heads: one parent, the new base, and the tree of the branch on that base.
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', "$($script:CleanMerge)^{tree}")
        $script:Rebased = New-ReviewCommit @($script:ReviewBase) 'rebased onto base'
        Set-ReviewBlob 'unreviewed.txt' 'new feature'
        $script:RebasedChanged = New-ReviewCommit @($script:ReviewBase) 'rebased with an extra change'
        # Only white space differs from the rebased head: patch-id ignores it, the tree id does not.
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', "$($script:CleanMerge)^{tree}")
        $cleanContent = $branch.Replace('line 12' + "`n", 'base line 12' + "`n")
        Set-ReviewBlob 'shared 中文.txt' $cleanContent.Replace('branch line 1' + "`n", 'branch  line 1' + "`n")
        $script:RebasedWhitespace = New-ReviewCommit @($script:ReviewBase) 'rebased with a white space change'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', "$($script:BaseUpdateMerges[0])^{tree}")
        $script:RebasedAgain = New-ReviewCommit @($script:BaseUpdateCommits[0]) 'rebased again onto a newer base'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('read-tree', "$($script:BaseUpdateMerges[1])^{tree}")
        $script:RebasedAgainMerge = New-ReviewCommit @($script:RebasedAgain, $script:BaseUpdateCommits[1]) 'clean base merge on a rebased head'
        $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-ref', 'HEAD', $script:ReviewedSha)
    }
    New-ReviewGitFixture

    function Invoke-ReviewMergeFixture {
        Merge-GhAppApprovedPullRequest -Numbers 7 -Worktree $script:ReviewGitRoot -LogPath $script:LogFile -PollSeconds 1
    }
    function New-ReviewMergeSteps {
        param([string]$Head = $script:ReviewedSha, [string]$Base = $script:ReviewBase,
            [object[]]$Reviews = @((New-Review -Head $script:ReviewedSha)), [string]$BaseName = 'main')
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base -BaseName $BaseName)
        New-ReviewStep -Reviews $Reviews
        New-ChecksStep
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base -BaseName $BaseName)
        New-ReviewStep -Reviews $Reviews
        New-ApiStep pulls/7/merge PUT @{ merged = $true; sha = $script:MergeSha } -App
        New-PrStep -Pr (New-Pr -Head $Head -BaseHead $Base -BaseName $BaseName -State MERGED)
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
        It 'refuses to merge when the ledger cannot be opened, and does not call it a missing entry' {
            [void][IO.Directory]::CreateDirectory($script:LedgerFile)
            try {
                Set-Responses @((New-PrStep))
                Get-Message { Invoke-ReviewMergeFixture } | Should Match 'The review ledger contains an invalid entry or could not be read\.'
                @(Get-Calls).Count | Should Be 1
            } finally { [IO.Directory]::Delete($script:LedgerFile) }
        }
        It 'treats an empty ledger as no entry when it merges' {
            [IO.File]::WriteAllText($script:LedgerFile, '')
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

    function New-Run {
        param([int]$Id = 1, [string]$Name = 'Core', [int]$WorkflowId = 10, [string]$Status = 'completed',
            [string]$Conclusion = 'success', [string]$CreatedAt = '2026-01-01T00:00:00Z', [string]$Event = 'pull_request')
        @{ id = $Id; name = $Name; workflow_id = $WorkflowId; event = $Event; status = $Status;
            conclusion = $(if ($Status -eq 'completed') { $Conclusion } else { $null });
            created_at = $CreatedAt; html_url = "https://example.test/runs/$Id" }
    }
    function New-RunsStep {
        param([object[]]$Runs = @((New-Run)), [string]$Head = $script:HeadSha, [int]$Page = 1, [int]$Total = -1)
        if ($Total -lt 0) { $Total = @($Runs).Count }
        New-ApiStep "actions/runs?head_sha=$Head&per_page=100&page=$Page" -Value @{ total_count = $Total; workflow_runs = @($Runs) }
    }
    function New-RunPages {
        param([int]$Pages, [int]$Total)
        foreach ($page in 1..$Pages) {
            $runs = @(1..100 | ForEach-Object { New-Run -Id ($page * 1000 + $_) -Name Core -WorkflowId 10 })
            New-RunsStep $runs -Page $page -Total $Total
        }
    }
    function New-FilesStep {
        param([int]$Number = 7, [object[]]$Files = @(@{ filename = 'tools/example.ps1'; additions = 3; deletions = 1 }))
        New-ApiStep "pulls/$Number/files?per_page=100&page=1" -Value @($Files)
    }
    function New-RecordReview {
        param([string]$Head = $script:HeadSha, [string]$Body = "Review record: $script:HeadSha accept`n`nChecked.",
            [int]$Id = 5, [string]$State = 'COMMENTED', [string]$SubmittedAt = '2026-01-01T00:00:00Z')
        @{ id = $Id; state = $State; commit_id = $Head; body = $Body; submitted_at = $SubmittedAt; user = @{ login = 'example-app[bot]' } }
    }
    function New-ReadinessSteps {
        param([int]$Number = 7, $Pr = (New-Pr -MergeState BLOCKED), [object[]]$Reviews = @((New-RecordReview)),
            [object[]]$Runs = @((New-Run)), [string]$Head = $script:HeadSha)
        New-PrStep $Number $Pr
        New-ReviewStep $Number $Reviews
        New-RunsStep $Runs $Head
        New-FilesStep $Number
    }
    function Get-Row {
        param($Readiness, [string]$Name)
        @($Readiness.Checks | Where-Object { $_.Name -ceq $Name })[0]
    }

    Describe 'Pull request readiness' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'passes every check for a ready PR and lists its files, using only owner reads' {
            Set-Responses @(New-ReadinessSteps)
            $result = Get-GhAppPullRequestReadiness -Number 7 -ExpectedHeadSha $script:HeadSha
            $result.Ready | Should Be $true
            $result.Head | Should Be $script:HeadSha
            @($result.Checks | Where-Object { $_.Result -cne 'PASS' }).Count | Should Be 0
            (@($result.Checks | ForEach-Object { $_.Name }) -join ',') | Should Be 'State,Draft,Author,Head,MergeState,ReviewRecord,Run: Core'
            @($result.Files).Count | Should Be 1
            $result.Files[0].Path | Should Be 'tools/example.ps1'
            $result.Files[0].Added | Should Be 3
            @(Get-Calls).Count | Should Be 4
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
            [IO.File]::Exists($env:NVT_GHAPP_HELPER_CALLS) | Should Be $false
        }
        It 'does not need an expected head' {
            Set-Responses @(New-ReadinessSteps)
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $true
            (Get-Row $result 'Head').Detail | Should Match 'no expected head given'
        }
        foreach ($case in @(
            @{ Title = 'a closed PR'; Row = 'State'; Pr = (New-Pr -MergeState BLOCKED -State CLOSED) },
            @{ Title = 'a draft PR'; Row = 'Draft'; Pr = (New-Pr -MergeState BLOCKED -Draft) },
            @{ Title = 'a PR by another author'; Row = 'Author'; Pr = (New-Pr -MergeState BLOCKED -Author other-author) },
            @{ Title = 'a BEHIND PR'; Row = 'MergeState'; Pr = (New-Pr -MergeState BEHIND) },
            @{ Title = 'a DIRTY PR'; Row = 'MergeState'; Pr = (New-Pr -MergeState DIRTY) },
            @{ Title = 'an UNKNOWN merge state'; Row = 'MergeState'; Pr = (New-Pr -MergeState UNKNOWN) }
        )) {
            It "fails the $($case.Row) check for $($case.Title) and still reports the other checks" {
                Set-Responses @(New-ReadinessSteps -Pr $case.Pr)
                $result = Get-GhAppPullRequestReadiness -Number 7
                $result.Ready | Should Be $false
                (Get-Row $result $case.Row).Result | Should Be 'FAIL'
                @($result.Checks | Where-Object { $_.Result -ceq 'FAIL' }).Count | Should Be 1
            }
        }
        foreach ($state in @('CLEAN', 'BLOCKED', 'UNSTABLE')) {
            It "accepts the $state merge state" {
                Set-Responses @(New-ReadinessSteps -Pr (New-Pr -MergeState $state))
                (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'MergeState').Result | Should Be 'PASS'
            }
        }
        It 'fails the head check when the head is not the expected one' {
            Set-Responses @(New-ReadinessSteps -Pr (New-Pr -Head $script:NextSha -MergeState BLOCKED) -Head $script:NextSha `
                -Reviews @((New-RecordReview -Head $script:NextSha -Body "Review record: $script:NextSha accept")))
            $result = Get-GhAppPullRequestReadiness -Number 7 -ExpectedHeadSha $script:HeadSha
            $result.Ready | Should Be $false
            (Get-Row $result 'Head').Result | Should Be 'FAIL'
            (Get-Row $result 'Head').Detail | Should Be "head is $script:NextSha; expected $script:HeadSha"
        }
        It 'judges each workflow by its latest run and ignores an older failed run' {
            $runs = @((New-Run -Id 1 -Conclusion failure -CreatedAt '2026-01-01T00:00:00Z' -Event pull_request_review),
                (New-Run -Id 2 -Conclusion success -CreatedAt '2026-01-01T01:00:00Z'))
            Set-Responses @(New-ReadinessSteps -Runs $runs)
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $true
            $row = Get-Row $result 'Run: Core'
            $row.Result | Should Be 'PASS'
            $row.Detail | Should Match '1 older run\(s\) of this workflow ignored'
            $row.Detail | Should Match 'run 2 is completed/success'
        }
        It 'fails when the latest run of a workflow failed, even if an older run succeeded' {
            $runs = @((New-Run -Id 1 -Conclusion success -CreatedAt '2026-01-01T00:00:00Z'),
                (New-Run -Id 2 -Conclusion failure -CreatedAt '2026-01-01T01:00:00Z'))
            Set-Responses @(New-ReadinessSteps -Runs $runs)
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Run: Core').Detail | Should Match 'run 2 is completed/failure'
        }
        It 'breaks a tie between runs created in the same second by the run ID' {
            # 9 and 10 sort differently as numbers and as text, so the test tells the two orders apart.
            $runs = @((New-Run -Id 10 -Conclusion success), (New-Run -Id 9 -Conclusion failure))
            Set-Responses @(New-ReadinessSteps -Runs $runs)
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'Run: Core').Result | Should Be 'PASS'
        }
        It 'fails while the latest run is still in progress' {
            Set-Responses @(New-ReadinessSteps -Runs @((New-Run -Status in_progress)))
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Run: Core').Detail | Should Match 'is in_progress'
        }
        It 'reports each workflow on its own row and fails when one of them failed' {
            $runs = @((New-Run -Id 1 -Name Core -WorkflowId 10), (New-Run -Id 2 -Name 'approval-check' -WorkflowId 20 -Conclusion failure))
            Set-Responses @(New-ReadinessSteps -Runs $runs)
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Run: Core').Result | Should Be 'PASS'
            (Get-Row $result 'Run: approval-check').Result | Should Be 'FAIL'
        }
        It 'reads every page of runs, so a failure that sits on a later page is found' {
            $first = @(1..100 | ForEach-Object { New-Run -Id (1000 + $_) -Name Core -WorkflowId 10 -CreatedAt '2026-01-02T00:00:00Z' })
            $second = @((New-Run -Id 5 -Name 'approval-check' -WorkflowId 20 -Conclusion failure -CreatedAt '2026-01-01T00:00:00Z'))
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))),
                (New-RunsStep $first -Total 101), (New-RunsStep $second -Page 2 -Total 101), (New-FilesStep))
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Run: Core').Result | Should Be 'PASS'
            (Get-Row $result 'Run: approval-check').Result | Should Be 'FAIL'
            @(Get-Calls).Count | Should Be 5
        }
        It 'fails when fewer runs were read than GitHub reports' {
            $first = @(1..100 | ForEach-Object { New-Run -Id (1000 + $_) -Name Core -WorkflowId 10 })
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))),
                (New-RunsStep $first -Total 150), (New-RunsStep @() -Page 2 -Total 150), (New-FilesStep))
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Runs').Detail | Should Be 'only 100 of 150 runs of this head were read'
        }
        It 'keeps two workflows with the same name apart by their IDs' {
            $runs = @((New-Run -Id 1 -Name Core -WorkflowId 10), (New-Run -Id 2 -Name Core -WorkflowId 11 -Conclusion failure))
            Set-Responses @(New-ReadinessSteps -Runs $runs)
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            @($result.Checks | Where-Object { $_.Name -ceq 'Run: Core' }).Count | Should Be 2
            @($result.Checks | Where-Object { $_.Name -ceq 'Run: Core' -and $_.Result -ceq 'FAIL' }).Count | Should Be 1
        }
        It 'accepts exactly 1,000 runs over ten pages as a complete read' {
            $steps = @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))))
            $steps += @(New-RunPages -Pages 10 -Total 1000)
            $steps += (New-FilesStep)
            Set-Responses $steps
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $true
            @($result.Checks | Where-Object { $_.Name -ceq 'Runs' }).Count | Should Be 0
            @(Get-Calls).Count | Should Be 13
        }
        It 'fails at 1,001 runs after ten pages and asks for no eleventh page' {
            $steps = @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))))
            $steps += @(New-RunPages -Pages 10 -Total 1001)
            $steps += (New-FilesStep)
            Set-Responses $steps
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Runs').Detail | Should Be 'only 1000 of 1001 runs of this head were read'
            @(Get-Calls).Count | Should Be 13
        }
        It 'fails when a short page ends the read before total_count' {
            $short = @(1..40 | ForEach-Object { New-Run -Id (2000 + $_) -Name Core -WorkflowId 10 })
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))),
                (New-RunsStep $short -Total 150), (New-FilesStep))
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Runs').Detail | Should Be 'only 40 of 150 runs of this head were read'
            @(Get-Calls).Count | Should Be 4
        }
        It 'stops with the API error when a later page cannot be read, and returns no readiness' {
            $first = @(1..100 | ForEach-Object { New-Run -Id (1000 + $_) -Name Core -WorkflowId 10 })
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BLOCKED)), (New-ReviewStep -Reviews @((New-RecordReview))),
                (New-RunsStep $first -Total 150),
                (New-ApiStep "actions/runs?head_sha=$script:HeadSha&per_page=100&page=2" -ExitCode 1 -ErrorText 'HTTP 403'))
            Get-Message { Get-GhAppPullRequestReadiness -Number 7 } |
                Should Be "HTTP 403 /repos/$script:RepositoryName/actions/runs?head_sha=$script:HeadSha&per_page=100&page=2"
            @(Get-Calls).Count | Should Be 4
        }
        It 'groups runs by name when the workflow ID is missing' {
            $older = New-Run -Id 1 -Name Core -CreatedAt '2026-01-01T00:00:00Z'
            $older.Remove('workflow_id')
            $newer = New-Run -Id 2 -Name Core -Conclusion failure -CreatedAt '2026-01-01T01:00:00Z'
            $newer.Remove('workflow_id')
            Set-Responses @(New-ReadinessSteps -Runs @($older, $newer))
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            @($result.Checks | Where-Object { $_.Name -ceq 'Run: Core' }).Count | Should Be 1
            (Get-Row $result 'Run: Core').Detail | Should Match 'run 2 is completed/failure'
        }
        It 'fails when the head has no workflow run' {
            Set-Responses @(New-ReadinessSteps -Runs @())
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'Runs').Detail | Should Be 'no workflow run found for this head'
        }
        It 'fails without a review record on the head' {
            Set-Responses @(New-ReadinessSteps -Reviews @())
            $result = Get-GhAppPullRequestReadiness -Number 7
            $result.Ready | Should Be $false
            (Get-Row $result 'ReviewRecord').Detail | Should Match '^no review on this head matches the pattern '
        }
        It 'does not count a record that sits on another commit, names another head, or is still pending' {
            $reviews = @((New-RecordReview -Head $script:NextSha), (New-RecordReview -Body "Review record: $script:NextSha accept" -Id 6),
                (New-RecordReview -State PENDING -Id 7))
            Set-Responses @(New-ReadinessSteps -Reviews $reviews)
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'FAIL'
        }
        It 'counts only the first line: a rejection that quotes an acceptance later does not pass' {
            $body = "Review record: $script:HeadSha changes`n`nReview record: $script:HeadSha accept"
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body $body)))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'FAIL'
        }
        It 'does not count a dismissed acceptance, or a state that is not an acceptance' {
            foreach ($state in @('DISMISSED', 'CHANGES_REQUESTED')) {
                foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX)) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
                Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -State $state)))
                (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'FAIL'
            }
        }
        It 'lets a later rejection, or a later dismissed record, cancel an earlier acceptance' {
            $later = @(
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -Body "Review record: $script:HeadSha changes"),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -State DISMISSED),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -State CHANGES_REQUESTED -Body 'Please fix'))
            foreach ($review in $later) {
                foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX)) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
                Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview), $review))
                $row = Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord'
                $row.Result | Should Be 'FAIL'
                $row.Detail | Should Match 'the latest verdict on this head is review 6'
            }
        }
        It 'reads the first non-empty line, so a record with leading blank lines counts' {
            $body = "`n  `nReview record: $script:HeadSha accept`n`nChecked."
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body $body)))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'still ignores later lines when the first non-empty line is a rejection' {
            $body = "`nReview record: $script:HeadSha changes`n`nReview record: $script:HeadSha accept"
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body $body)))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'FAIL'
        }
        It 'reads the first non-empty line with a custom pattern too' {
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body "`nVerdict: accept`n`nChecked.")))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern '^Verdict: accept$') 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'lets a later rejection, a later dismissed record or a later review with a body cancel an acceptance in a custom format' {
            $custom = '^Verdict: accept$'
            $later = @(
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -Body "Verdict: reject`n`nP1 found."),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -State DISMISSED -Body "Verdict: accept`n`nChecked."),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -State CHANGES_REQUESTED -Body 'Please fix'),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -Body 'Withdrawn.'))
            foreach ($review in $later) {
                foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX)) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
                Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body "Verdict: accept`n`nChecked."), $review))
                $row = Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern $custom) 'ReviewRecord'
                $row.Result | Should Be 'FAIL'
                $row.Detail | Should Match 'the latest verdict on this head is review 6'
            }
        }
        It 'counts a new acceptance after a rejection in a custom format' {
            $reviews = @((New-RecordReview -Id 5 -Body "Verdict: reject"), (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -Body "Verdict: accept`n`nChecked."))
            Set-Responses @(New-ReadinessSteps -Reviews $reviews)
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern '^Verdict: accept$') 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'does not treat a review without a body as a verdict in a custom format' {
            $reviews = @((New-RecordReview -Body "Verdict: accept`n`nChecked."), (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z' -Body ''))
            Set-Responses @(New-ReadinessSteps -Reviews $reviews)
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern '^Verdict: accept$') 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'counts a new acceptance after a rejection' {
            $reviews = @((New-RecordReview -Id 5 -Body "Review record: $script:HeadSha changes"),
                (New-RecordReview -Id 6 -SubmittedAt '2026-01-01T01:00:00Z'))
            Set-Responses @(New-ReadinessSteps -Reviews $reviews)
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'does not count another repository format under the default pattern' {
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body "Verdict: accept`n`nChecked.")))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7) 'ReviewRecord').Result | Should Be 'FAIL'
        }
        It 'accepts another repository format through the pattern' {
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body "Verdict: accept`n`nChecked.")))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern '^Verdict: accept$') 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'puts the head into the pattern at {HEAD}' {
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body "Checked $script:HeadSha today")))
            (Get-Row (Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern 'Checked {HEAD} today') 'ReviewRecord').Result | Should Be 'PASS'
        }
        It 'rejects an invalid pattern before any call' {
            Get-Message { Get-GhAppPullRequestReadiness -Number 7 -ReviewRecordPattern '(' } | Should Be 'ReviewRecordPattern is not a valid regular expression.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a short expected head before any call' {
            Get-Message { Get-GhAppPullRequestReadiness -Number 7 -ExpectedHeadSha bad } | Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'refuses a fork PR after reading it once' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Fork)))
            Get-Message { Get-GhAppPullRequestReadiness -Number 7 } | Should Be "Repository mismatch. Expected $script:RepositoryName."
            @(Get-Calls).Count | Should Be 1
        }
    }

    Describe 'Review request with the readiness check' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'records the request after every check passes, with one read of the PR' {
            Set-Responses @(New-ReadinessSteps)
            $entry = Request-GhAppOwnerReview -Number 7 -CheckReadiness -ExpectedHeadSha $script:HeadSha
            $entry.head | Should Be $script:HeadSha
            @(Get-Content -LiteralPath $script:LedgerFile).Count | Should Be 1
            @(Get-Calls).Count | Should Be 4
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
        }
        It 'refuses and writes no ledger line when a check fails, naming the failed checks' {
            Set-Responses @(New-ReadinessSteps -Reviews @() -Runs @((New-Run -Conclusion failure)))
            $message = Get-Message { Request-GhAppOwnerReview -Number 7 -CheckReadiness }
            $message | Should Match '^#7 is not ready for owner review\. ReviewRecord: no review on this head matches'
            $message | Should Match '; Run: Core: pull_request run 1 is completed/failure'
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'refuses a head other than the expected one before the readiness reads' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Request-GhAppOwnerReview -Number 7 -CheckReadiness -ExpectedHeadSha $script:HeadSha } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'checks the expected head without the readiness check too' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Request-GhAppOwnerReview -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'passes a repository specific pattern to the check' {
            Set-Responses @(New-ReadinessSteps -Reviews @((New-RecordReview -Body 'Verdict: accept')))
            $entry = Request-GhAppOwnerReview -Number 7 -CheckReadiness -ReviewRecordPattern '^Verdict: accept$'
            $entry.head | Should Be $script:HeadSha
        }
        It 'refuses a pattern without the readiness check before any call' {
            Get-Message { Request-GhAppOwnerReview -Number 7 -ReviewRecordPattern 'x' } | Should Be 'ReviewRecordPattern requires CheckReadiness.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a short expected head before any call' {
            Get-Message { Request-GhAppOwnerReview -Number 7 -ExpectedHeadSha bad } | Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
        }
    }

    Describe 'Body edit head guard' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        It 'replaces the body after it reads the expected head' {
            Set-Responses @((New-PrStep), (New-ApiStep pulls/7 PATCH @{ number = 7 } -App))
            $null = Set-GhAppPullRequestBody -Number 7 -BodyFile $script:BodyFile -ExpectedHeadSha $script:HeadSha
            $calls = @(Get-Calls)
            $calls.Count | Should Be 2
            $calls[0].AppToken | Should Be $false
            $calls[1].AppToken | Should Be $true
            (ConvertFrom-Json $calls[1].Input).body | Should Be "A synthetic description.`nSecond line."
        }
        It 'keeps Repository as the third positional parameter' {
            Set-Responses @((New-ApiStep pulls/7 PATCH @{ number = 7 } -App))
            $null = Set-GhAppPullRequestBody 7 $script:BodyFile $script:RepositoryName
            @(Get-Calls).Count | Should Be 1
            @(Get-Calls)[0].AppToken | Should Be $true
        }
        It 'refuses a changed head and writes nothing' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Set-GhAppPullRequestBody -Number 7 -BodyFile $script:BodyFile -ExpectedHeadSha $script:HeadSha } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        It 'rejects a short expected head before any call' {
            Get-Message { Set-GhAppPullRequestBody -Number 7 -BodyFile $script:BodyFile -ExpectedHeadSha bad } | Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'refuses a changed head given as the fourth positional parameter' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Set-GhAppPullRequestBody 7 $script:BodyFile $script:RepositoryName $script:HeadSha } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        It 'reads the body file before the PR' {
            Get-Message { Set-GhAppPullRequestBody -Number 7 -BodyFile (Join-Path $script:FixtureRoot 'missing.txt') -ExpectedHeadSha $script:HeadSha } |
                Should Be 'The body or message file is missing.'
            @(Get-Calls).Count | Should Be 0
        }
    }

    Describe 'Branch update' {
        BeforeEach { Reset-Fixture; Import-Fixture; Mock Start-Sleep {} }
        It 'updates through the App with the expected head and returns the new head' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep), (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            $result = Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha
            $result.Number | Should Be 7
            $result.OldHead | Should Be $script:HeadSha
            $result.NewHead | Should Be $script:NextSha
            $calls = @(Get-Calls)
            $calls.Count | Should Be 5
            $calls[2].AppToken | Should Be $true
            (ConvertFrom-Json $calls[2].Input).expected_head_sha | Should Be $script:HeadSha
            Assert-MockCalled Start-Sleep -Times 2 -Exactly -Scope It
            Assert-NoToken ($result | Out-String)
        }
        It 'updates a draft PR' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Draft)), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:NextSha -Draft)))
            (Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha).NewHead | Should Be $script:NextSha
        }
        It 'refuses a stale expected head before any write' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        foreach ($case in @(
            @{ Title = 'closed'; Pr = (New-Pr -State CLOSED); Message = '#7 must be open to update its branch.' },
            @{ Title = 'merged'; Pr = (New-Pr -State MERGED); Message = '#7 must be open to update its branch.' },
            @{ Title = 'non-bot'; Pr = (New-Pr -Author other-author); Message = '#7 PR author does not match botLogin.' }
        )) {
            It "refuses a $($case.Title) PR" {
                Set-Responses @((New-PrStep -Pr $case.Pr))
                Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } | Should Be $case.Message
                @(Get-Calls).Count | Should Be 1
            }
        }
        It 'refuses a PR that was sent for owner review, from the ledger and without a GitHub write' {
            Add-LedgerFixture 7
            Set-Responses @((New-PrStep))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be '#7 was sent for owner review; update refused. Merge-GhAppApprovedPullRequest updates the branch after approval.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'does not let a ledger entry of another PR block the update' {
            Add-LedgerFixture 8
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            (Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha).NewHead | Should Be $script:NextSha
        }
        It 'refuses a PR that has an owner review' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @((New-Review -State COMMENTED))))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be '#7 has an owner review; update refused. Open a follow-up PR instead.'
            @(Get-Calls).Count | Should Be 2
        }
        It 'refuses an unreadable ledger instead of updating' {
            [IO.File]::WriteAllText($script:LedgerFile, 'not json')
            Set-Responses @((New-PrStep))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be 'The review ledger contains an invalid entry or could not be read.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses when the ledger cannot be opened for reading, not only when it holds bad JSON' {
            # File.Exists is false for a directory, so a check that relied on it would have called the ledger empty.
            [void][IO.Directory]::CreateDirectory($script:LedgerFile)
            try {
                Set-Responses @((New-PrStep))
                Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                    Should Be 'The review ledger contains an invalid entry or could not be read.'
                @(Get-Calls).Count | Should Be 1
            } finally { [IO.Directory]::Delete($script:LedgerFile) }
        }
        It 'refuses when the ledger is locked by another process' {
            Add-LedgerFixture 8
            $stream = [IO.File]::Open($script:LedgerFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            try {
                Set-Responses @((New-PrStep))
                Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                    Should Be 'The review ledger contains an invalid entry or could not be read.'
            } finally { $stream.Dispose() }
        }
        It 'treats a ledger in a missing folder as no ledger' {
            $missing = Join-Path $script:FixtureRoot 'no-such-folder/review-ledger.jsonl'
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            (Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LedgerPath $missing).NewHead | Should Be $script:NextSha
        }
        It 'treats an empty ledger file as no entry' {
            [IO.File]::WriteAllText($script:LedgerFile, '')
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            (Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha).NewHead | Should Be $script:NextSha
        }
        It 'closes the ledger file after a read that succeeds' {
            Add-LedgerFixture 8
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            $null = Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha
            # File.Delete fails with a sharing violation while a reader still holds the file.
            [IO.File]::Delete($script:LedgerFile)
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'closes the ledger file after a read that fails' {
            [IO.File]::WriteAllText($script:LedgerFile, 'not json')
            Set-Responses @((New-PrStep))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be 'The review ledger contains an invalid entry or could not be read.'
            [IO.File]::Delete($script:LedgerFile)
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'reports a refused update without retrying' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()),
                (New-ApiStep pulls/7/update-branch PUT @{} -App -ExitCode 1 -ErrorText 'HTTP 422'))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha } |
                Should Be "HTTP 422 /repos/$script:RepositoryName/pulls/7/update-branch"
            @(Get-Calls).Count | Should Be 3
        }
        It 'stops waiting when the head does not change in time' {
            $script:ClockReads = 0
            $script:ClockStart = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            Mock Get-GhAppUtcNow { $script:ClockReads++; if ($script:ClockReads -eq 1) { $script:ClockStart } else { $script:ClockStart.AddSeconds(500) } }
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @()), (New-ApiStep pulls/7/update-branch PUT @{} -App), (New-PrStep))
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha $script:HeadSha -TimeoutSeconds 120 } |
                Should Be '#7 update was accepted but the head did not change within 120 seconds. Check the PR before trying again.'
        }
        It 'rejects a short expected head before any call' {
            Get-Message { Update-GhAppPullRequestBranch -Number 7 -ExpectedHeadSha bad } | Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
        }
    }

    Describe 'Merged branch removal' {
        BeforeEach { Reset-Fixture; Import-Fixture }
        function New-RemovalSteps {
            New-PrStep -Pr (New-Pr -State MERGED)
            New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }
            New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }
            New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }
            New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -RawOutput ''
        }
        It 'deletes a merged branch that matches the prefix and logs it' {
            Set-Responses @(New-RemovalSteps)
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Deleted | Should Be $true
            $result.Branch | Should Be 'test/module'
            $result.Reason | Should Be 'deleted'
            @(Get-Calls).Count | Should Be 5
            @(Get-Calls)[4].AppToken | Should Be $true
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 deleted branch test/module at $script:HeadSha"
            Assert-NoToken
        }
        It 'accepts the DeleteBranchPrefix and Log names of the merge function' {
            Set-Responses @(New-RemovalSteps)
            (Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -Log $script:LogFile -DeleteBranchPrefix 'test/').Deleted | Should Be $true
        }
        It 'keeps a branch that does not match the prefix, without a branch call' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'feature/'
            $result.Deleted | Should Be $false
            $result.Reason | Should Be 'no matching delete prefix'
            @(Get-Calls).Count | Should Be 1
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'kept branch test/module; no matching delete prefix'
        }
        It 'keeps main even when the prefix matches it' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED -Branch main)))
            (Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'ma').Reason | Should Be 'base branch'
            @(Get-Calls).Count | Should Be 1
        }
        It 'keeps a branch whose ref moved after the merge' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:NextSha } }))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Deleted | Should Be $false
            $result.Reason | Should Be 'ref changed or absent'
            @(Get-Calls).Count | Should Be 2
        }
        It 'keeps a branch that the base does not contain' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'ahead'; ahead_by = 2 }))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Deleted | Should Be $false
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
        }
        It 'keeps a branch that was pushed after the checks and before the delete' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:NextSha } }))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Deleted | Should Be $false
            $result.Reason | Should Be 'ref changed before delete'
            @(Get-Calls).Count | Should Be 4
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'kept branch test/module; ref changed before delete'
        }
        It 'keeps a branch that is gone at the last read' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -ExitCode 1 -ErrorText 'HTTP 404'))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Reason | Should Be 'ref changed before delete'
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
        }
        It 'gets the App token before the last ref read, so a helper retry cannot sit between that read and the delete' {
            $env:NVT_GHAPP_HELPER_FAIL = '2'
            $script:SleepAt = @()
            Mock Start-Sleep { $script:SleepAt += @(Get-Calls).Count }
            Set-Responses @(New-RemovalSteps)
            (Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/').Deleted | Should Be $true
            # Both waits of the helper retry came after the third gh call, before the last ref read (call 4) and the delete (call 5).
            ($script:SleepAt -join ',') | Should Be '3,3'
            @(Get-HelperRecords).Count | Should Be 3
            @(Get-Calls).Count | Should Be 5
            @(Get-Calls)[3].AppToken | Should Be $false
            @(Get-Calls)[4].AppToken | Should Be $true
        }
        It 'keeps a branch that was pushed while the token helper retried, and sends no delete' {
            $env:NVT_GHAPP_HELPER_FAIL = '2'
            $script:SleepAt = @()
            Mock Start-Sleep { $script:SleepAt += @(Get-Calls).Count }
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:NextSha } }))
            $result = Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/'
            $result.Deleted | Should Be $false
            $result.Reason | Should Be 'ref changed before delete'
            ($script:SleepAt -join ',') | Should Be '3,3'
            @(Get-HelperRecords).Count | Should Be 3
            @(Get-Calls).Count | Should Be 4
            @(Get-Calls | Where-Object AppToken).Count | Should Be 0
        }
        It 'stops before the last ref read when the App token cannot be fetched' {
            $env:NVT_GHAPP_HELPER_FAIL = '99'
            Mock Start-Sleep {}
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED)),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }))
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/' } |
                Should Be "HTTP unknown /repos/$script:RepositoryName/git/refs/heads/test%2Fmodule (token helper exit 1) (after 3 attempts)"
            @(Get-Calls).Count | Should Be 3
            @(Get-HelperRecords).Count | Should Be 3
        }
        It 'does not repeat a delete that failed' {
            Mock Start-Sleep {}
            $steps = @(New-RemovalSteps)
            $steps[4] = New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -ExitCode 1 -ErrorText 'HTTP 502'
            Set-Responses $steps
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/' } |
                Should Be "HTTP 502 /repos/$script:RepositoryName/git/refs/heads/test%2Fmodule"
            @(Get-Calls).Count | Should Be 5
            @(Get-HelperRecords).Count | Should Be 1
            Assert-MockCalled Start-Sleep -Times 0 -Exactly -Scope It
        }
        It 'refuses a PR that is not merged' {
            Set-Responses @((New-PrStep))
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/' } |
                Should Be '#7 is OPEN, not merged; branch deletion refused.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses a head other than the expected one' {
            Set-Responses @((New-PrStep -Pr (New-Pr -State MERGED -Head $script:NextSha)))
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix 'test/' } |
                Should Be "#7 head changed. Expected $script:HeadSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        It 'rejects an empty prefix and a short head before any call' {
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -BranchPrefix ' ' } |
                Should Be 'BranchPrefix must be nonempty.'
            Get-Message { Remove-GhAppMergedBranch -Number 7 -ExpectedHeadSha bad -LogPath $script:LogFile -BranchPrefix 'test/' } |
                Should Be 'ExpectedHeadSha is not a 40-character SHA.'
            @(Get-Calls).Count | Should Be 0
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

    Describe 'Review request binding by patch-id' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            # The fixtures already contain every object. Only fetching is replaced; patch-ids and proof use real Git.
            Mock Sync-GhAppReviewCommits {}
            Mock Get-GhAppTimelineObservation { 'unavailable' }
            $script:PatchId = Get-GhAppPatchId $script:ReviewGitRoot $script:ReviewedSha $script:ReviewBase
            $script:OtherPatchId = Get-GhAppPatchId $script:ReviewGitRoot $script:BaseUpdateMerges[0] $script:ReviewBase
            $script:GraphqlReady = @{ Arguments = @('api', 'graphql', '--hostname', 'github.com', '--input', '-'); App = $true;
                Output = '{"data":{"markPullRequestReadyForReview":{"pullRequest":{"number":7,"isDraft":false}}}}'; Error = ''; ExitCode = 0 }
        }
        It 'gives the same patch-id to the same content on another base and another one to other content' {
            $script:PatchId | Should Match '^[0-9a-f]{40}$'
            Get-GhAppPatchId $script:ReviewGitRoot $script:ReviewedSha $script:BaseUpdateCommits[0] | Should Be $script:PatchId
            Get-GhAppPatchId $script:ReviewGitRoot $script:BaseUpdateMerges[0] $script:BaseUpdateCommits[0] | Should Be $script:PatchId
            $script:OtherPatchId | Should Not Be $script:PatchId
        }
        It 'refuses to identify an empty diff' {
            Get-Message { Get-GhAppPatchId $script:ReviewGitRoot $script:ReviewedSha $script:ReviewedSha } | Should Be 'The pull request has no diff to identify.'
        }
        It 'records the patch-id when a worktree is given and keeps the old shape otherwise' {
            $pr = New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase
            Set-Responses @((New-PrStep -Pr $pr), (New-PrStep -Pr $pr))
            $withTree = Request-GhAppOwnerReview 7 -Worktree $script:ReviewGitRoot
            $withoutTree = Request-GhAppOwnerReview 7
            ($withTree.PSObject.Properties.Name -join ',') | Should Be 'owner,repo,number,head,requestedAt,base,patchId'
            $withTree.patchId | Should Be $script:PatchId
            ($withoutTree.PSObject.Properties.Name -join ',') | Should Be 'owner,repo,number,head,requestedAt,base'
            @([IO.File]::ReadAllLines($script:LedgerFile)).Count | Should Be 2
        }
        It 'registers a pull request that opens ready, with its patch-id' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'example-app[bot]' } }),
                (New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)))
            $result = New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile -Worktree $script:ReviewGitRoot
            $result.ReviewRequest.head | Should Be $script:ReviewedSha
            $result.ReviewRequest.patchId | Should Be $script:PatchId
            @([IO.File]::ReadAllLines($script:LedgerFile)).Count | Should Be 1
        }
        It 'registers a pull request that opens ready, without a patch-id when no worktree is given' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'example-app[bot]' } }), (New-PrStep))
            $result = New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile
            $result.ReviewRequest.head | Should Be $script:HeadSha
            ($result.ReviewRequest.PSObject.Properties.Name -join ',') | Should Be 'owner,repo,number,head,requestedAt,base'
        }
        It 'does not register a draft pull request' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'example-app[bot]' } }))
            $result = New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile -Draft -Worktree $script:ReviewGitRoot
            $result.ReviewRequest | Should Be $null
            @(Get-Calls).Count | Should Be 2
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'does not register a pull request whose author is wrong' {
            Set-Responses @((New-ApiStep pulls POST @{ number = 7 } -App), (New-ApiStep pulls/7 -Value @{ user = @{ login = 'other-author' } }))
            Get-Message { New-GhAppPullRequest -Head test/module -Title title -BodyFile $script:BodyFile } | Should Match '^PR author does not match botLogin'
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'marks a draft ready through the App and registers the head with its patch-id' {
            $draft = New-Pr -Draft -Head $script:ReviewedSha -BaseHead $script:ReviewBase
            Set-Responses @((New-PrStep -Pr $draft), (New-ApiStep pulls/7 -Value @{ node_id = 'PR_node_1' }), $script:GraphqlReady,
                (New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)))
            $entry = Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot
            $entry.head | Should Be $script:ReviewedSha
            $entry.patchId | Should Be $script:PatchId
            $calls = @(Get-Calls)
            $calls.Count | Should Be 4
            $calls[2].AppToken | Should Be $true
            (ConvertFrom-Json $calls[2].Input).variables.id | Should Be 'PR_node_1'
            @([IO.File]::ReadAllLines($script:LedgerFile)).Count | Should Be 1
            Assert-NoToken
        }
        It 'does not register when the ready change fails' {
            $draft = New-Pr -Draft -Head $script:ReviewedSha -BaseHead $script:ReviewBase
            $failed = @{ Arguments = $script:GraphqlReady.Arguments; App = $true; Output = ''; Error = 'HTTP 403 __TOKEN__'; ExitCode = 1 }
            Set-Responses @((New-PrStep -Pr $draft), (New-ApiStep pulls/7 -Value @{ node_id = 'PR_node_1' }), $failed)
            Get-Message { Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot } | Should Be 'HTTP 403 /graphql'
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'refuses a pull request that is already ready, so an approval is not voided' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)))
            Get-Message { Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot } |
                Should Be '#7 is already ready. Use Request-GhAppOwnerReview to register it.'
            @(Get-Calls).Count | Should Be 1
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'refuses to mark a draft ready when its head is not the expected head' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Draft -Head $script:NextSha)))
            Get-Message { Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot } |
                Should Be "#7 head changed. Expected $script:ReviewedSha; found $script:NextSha."
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses to mark a pull request ready that the App did not open' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Draft -Author other-author -Head $script:ReviewedSha)))
            Get-Message { Set-GhAppPullRequestReady -Number 7 -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot } |
                Should Be '#7 PR author does not match botLogin.'
        }
        It 'changes the base when the patch-id stays, keeps the request time, and logs the carry' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)),
                (New-ApiStep git/ref/heads/release -Value @{ object = @{ sha = $script:BaseUpdateCommits[0] } }),
                (New-ApiStep pulls/7 PATCH @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)))
            $result = Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile
            $calls = @(Get-Calls)
            $calls.Count | Should Be 4
            (ConvertFrom-Json $calls[2].Input).base | Should Be release
            $calls[2].AppToken | Should Be $true
            $lines = @([IO.File]::ReadAllLines($script:LedgerFile))
            $lines.Count | Should Be 2
            $lines[1] | Should Match '"requestedAt":"2025-12-31T23:00:00\.0000000\+00:00"'
            $lines[1] | Should Match '"base":"release"'
            $lines[1] | Should Match "`"patchId`":`"$script:PatchId`""
            $lines[1] | Should Match '"reason":"base-change"'
            $lines[1] | Should Match '"fromBase":"main"'
            $result.NewBase | Should Be release
            $log = [IO.File]::ReadAllText($script:LogFile)
            $log | Should Match "#7 base changed main -> release; patch-id $script:PatchId unchanged; review request kept with its original time 2025-12-31T23:00:00\.0000000\+00:00 \(registered again at "
            Assert-NoToken $log
            # The owner's approval, given after the original request, still counts. It is older than the second registration.
            [IO.File]::Delete($env:NVT_GHAPP_INDEX)
            Set-Responses @(New-ReviewMergeSteps -Head $script:ReviewedSha -Base $script:BaseUpdateCommits[0] -BaseName release)
            Invoke-ReviewMergeFixture
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:ReviewedSha -> $script:MergeSha"
        }
        It 'stops before the change when the new base changes the patch-id' {
            Add-LedgerFixture -Head $script:BaseUpdateMerges[0] -PatchId $script:OtherPatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:BaseUpdateMerges[0] -BaseHead $script:ReviewBase)),
                (New-ApiStep git/ref/heads/release -Value @{ object = @{ sha = $script:BaseUpdateCommits[0] } }))
            Get-Message { Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:BaseUpdateMerges[0] -Worktree $script:ReviewGitRoot -LogPath $script:LogFile } |
                Should Be "#7 stop: base release changes the patch-id from $script:OtherPatchId to $script:PatchId; the base was not changed. Owner approval and a new review request required."
            @(Get-Calls).Count | Should Be 2
            @([IO.File]::ReadAllLines($script:LedgerFile)).Count | Should Be 1
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 base change main -> release refused: patch-id $script:OtherPatchId would become $script:PatchId; base not changed"
        }
        It 'changes the base of a pull request without a review request and writes no entry' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)),
                (New-ApiStep pulls/7 PATCH @{} -App),
                (New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)))
            $result = Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile
            $result.ReviewRequest | Should Be $null
            @(Get-Calls).Count | Should Be 3
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 base changed main -> release; no review request on file'
        }
        It 'refuses a base change for a request without a patch-id before any write' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)))
            Get-Message { Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile } |
                Should Be '#7 stop: the review request has no patch-id; register it again with a worktree before the base change.'
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses a base change when the request is for another head' {
            Add-LedgerFixture -Head $script:NextSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:ReviewBase)))
            Get-Message { Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile } |
                Should Be "#7 stop: the review request is for $script:NextSha, not for the current head; send the head for review again."
            @(Get-Calls).Count | Should Be 1
        }
        It 'refuses a base change when the head is not the expected head or the base is already the new base' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Set-GhAppPullRequestBase -Number 7 -NewBase release -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile } |
                Should Be "#7 head changed. Expected $script:ReviewedSha; found $script:NextSha."
            [IO.File]::Delete($env:NVT_GHAPP_INDEX)
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha)))
            Get-Message { Set-GhAppPullRequestBase -Number 7 -NewBase main -ExpectedHeadSha $script:ReviewedSha -Worktree $script:ReviewGitRoot -LogPath $script:LogFile } |
                Should Be '#7 is already based on main.'
        }
        It 'stops a merge when no review request is on file, even with a renamed base' {
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'no review ledger entry'
            @(Get-Calls).Count | Should Be 1
        }
        It 'accepts a renamed base when the request has the same patch-id, without a new registration' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @(New-ReviewMergeSteps -Head $script:ReviewedSha -Base $script:BaseUpdateCommits[0] -BaseName release)
            Invoke-ReviewMergeFixture
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:ReviewedSha -> $script:MergeSha"
            # The proof runs before and after the checks, and each run fetches once.
            Assert-MockCalled Sync-GhAppReviewCommits -Times 2 -Exactly -Scope It
        }
        It 'refuses a renamed base when the patch-id differs' {
            Add-LedgerFixture -Head $script:BaseUpdateMerges[0] -PatchId $script:OtherPatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:BaseUpdateMerges[0] -BaseHead $script:BaseUpdateCommits[0] -BaseName release)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:BaseUpdateMerges[0]))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be '#7 stop: review request base changed and the patch-id differs; owner approval and a new review request required.'
        }
        It 'refuses a renamed base for a request without a patch-id' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'review request base changed'
            Assert-MockCalled Sync-GhAppReviewCommits -Times 0 -Exactly -Scope It
        }
        It 'refuses a renamed base with the same patch-id when the head moved after the request' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:BaseUpdateMerges[0] -BaseHead $script:BaseUpdateCommits[0] -BaseName release)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'review request base changed'
        }
        It 'refuses an approval that is older than the request, also after a base change' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId -RequestedAt '2026-01-01T01:00:00Z' -Base release
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Be "#7 stop: no owner approval after review request $script:ReviewedSha."
            @(Get-Calls).Count | Should Be 2
        }
        It 'refuses an approval that is not on the current head, also after a base change' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:ReviewedSha -BaseHead $script:BaseUpdateCommits[0] -BaseName release)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:NextSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: owner approval is on $script:NextSha, not on review request $script:ReviewedSha or a clean base merge after it."
        }
    }

    Describe 'Owner approval reuse once' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            # The fixtures already contain every object. Only fetching is replaced; patch-ids and proof use real Git.
            Mock Sync-GhAppReviewCommits {}
            Mock Get-GhAppTimelineObservation { 'unavailable' }
            $script:PatchId = Get-GhAppPatchId $script:ReviewGitRoot $script:ReviewedSha $script:ReviewBase
        }
        function Add-ReuseFixture {
            param([string]$OldHead = $script:ReviewedSha, [string]$NewHead, [long]$ApprovalId = 1, [string]$ApprovalCommit = $script:ReviewedSha,
                [string]$RequestedAt = '2025-12-31T23:00:00Z')
            $entry = [ordered]@{ kind = 'reuse'; owner = $script:Owner; repo = $script:RepoName; number = 7; mode = 'tree-equal'; requestedAt = $RequestedAt;
                oldHead = $OldHead; newHead = $NewHead; approvalId = $ApprovalId; approvalCommit = $ApprovalCommit; reusedAt = '2026-01-01T02:00:00.0000000+00:00' }
            [IO.File]::AppendAllText($script:LedgerFile, (ConvertTo-Json $entry -Compress) + "`n", [Text.UTF8Encoding]::new($false))
        }
        function Get-ReuseLines { @([IO.File]::ReadAllLines($script:LedgerFile) | Where-Object { $_ -match '"kind":"reuse"' } | ForEach-Object { ConvertFrom-Json $_ }) }
        function Get-LedgerRequest { Get-GhAppReviewRequest (Get-GhAppContext '') 7 $script:LedgerFile }
        # A reuse merge reads the pull request once more right before the merge, and the merge commit after it.
        function New-ReuseMergeSteps {
            param([string]$Head = $script:ReviewedSha, [string]$Base = $script:ReviewBase, [object[]]$Reviews = @((New-Review -Head $script:ReviewedSha)),
                [string]$NowBase = $Base, [string]$MergedTree = (Get-GhAppIntegratedTree $script:ReviewGitRoot $Base $Head))
            $steps = @(New-ReviewMergeSteps -Head $Head -Base $Base -Reviews $Reviews)
            @($steps[0..4]) + @((New-PrStep -Pr (New-Pr -Head $Head -BaseHead $NowBase)), $steps[5], $steps[6],
                (New-ApiStep "git/commits/$script:MergeSha" -Value @{ tree = @{ sha = $MergedTree } }))
        }

        It 'reuses a live approval after a rebase with the same content, and records it only after the merge' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @(New-ReuseMergeSteps -Head $script:Rebased -Base $script:ReviewBase)
            $result = Invoke-ReviewMergeFixture
            $result.TreeMismatch | Should Be $false
            $result.Reused | Should Be $true
            $reuse = @(Get-ReuseLines)
            $reuse.Count | Should Be 1
            $reuse[0].mode | Should Be 'tree-equal'
            $reuse[0].oldHead | Should Be $script:ReviewedSha
            $reuse[0].newHead | Should Be $script:Rebased
            $reuse[0].approvalId | Should Be 1
            $reuse[0].approvalCommit | Should Be $script:ReviewedSha
            $reuse[0].patchId | Should Be $script:PatchId
            ([DateTimeOffset]$reuse[0].requestedAt) -eq [DateTimeOffset]'2025-12-31T23:00:00Z' | Should Be $true
            $log = [IO.File]::ReadAllText($script:LogFile)
            $log | Should Match "#7 owner approval 1 reused from $script:ReviewedSha \(tree-equal\); merged head $script:Rebased"
            $log | Should Match "#7 merged $script:Rebased -> $script:MergeSha"
        }
        It 'refuses the reuse when the base moved after the proof, sends no merge and spends nothing' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            $steps = @(New-ReuseMergeSteps -Head $script:Rebased -Base $script:ReviewBase -NowBase $script:BaseUpdateCommits[0])
            Set-Responses @($steps[0..5])
            Get-Message { Invoke-ReviewMergeFixture } | Should Match '^#7 stop: the base branch moved after the reuse proof \('
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            @(Get-ReuseLines).Count | Should Be 0
            [IO.File]::ReadAllText($script:LogFile) | Should Not Match ' merged '
        }
        It 'flags a merge commit whose tree differs from the proven base plus head, and still spends the approval' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            $other = Get-GhAppIntegratedTree $script:ReviewGitRoot $script:ReviewBase $script:RebasedChanged
            Set-Responses @(New-ReuseMergeSteps -Head $script:Rebased -Base $script:ReviewBase -MergedTree $other)
            $result = Invoke-ReviewMergeFixture 3>$null
            $result.TreeMismatch | Should Be $true
            $result.MergeCommit | Should Be $script:MergeSha
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 TREE MISMATCH after reuse: merge commit $script:MergeSha"
            @(Get-ReuseLines).Count | Should Be 1
        }
        It 'does not re-read the base or the merge commit when no approval is reused' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps)
            $result = Invoke-ReviewMergeFixture
            $result.Reused | Should Be $false
            $result.TreeMismatch | Should Be $false
            @(Get-Calls).Count | Should Be 7
        }
        It 'does not spend the approval when the merge does not happen' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            $steps = @(New-ReviewMergeSteps -Head $script:Rebased -Base $script:ReviewBase)
            Set-Responses @($steps[0], $steps[1], (New-ChecksStep -Bucket fail -ExitCode 1))
            Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: required checks failed.'
            @(Get-ReuseLines).Count | Should Be 0
        }
        It 'refuses a second use of the same approval for another head' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Add-ReuseFixture -NewHead $script:Rebased
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:RebasedAgain -BaseHead $script:BaseUpdateCommits[0])),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: owner approval 1 was already reused for $script:Rebased; a new owner approval is required."
            @(Get-ReuseLines).Count | Should Be 1
            @(Get-Calls).Count | Should Be 2
        }
        It 'refuses a rebase that changes the patch-id' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:RebasedChanged -BaseHead $script:ReviewBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: head $script:RebasedChanged is not review request $script:ReviewedSha or a clean base merge after it, and its patch-id differs from the request; owner approval and a new review request required."
            @(Get-ReuseLines).Count | Should Be 0
        }
        It 'refuses a rebase whose patch-id matches but whose content differs only in white space' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            (Get-GhAppPatchId $script:ReviewGitRoot $script:RebasedWhitespace $script:ReviewBase) | Should Be $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:RebasedWhitespace -BaseHead $script:ReviewBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: head $script:RebasedWhitespace does not have the content of approved commit $script:ReviewedSha once both are merged into main; owner approval and a new review request required."
            @(Get-ReuseLines).Count | Should Be 0
        }
        It 'still needs a new approval after a rebase when the request has no patch-id' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:Rebased -BaseHead $script:ReviewBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: head $script:Rebased adds commits after review request $script:ReviewedSha; owner approval and a new review request required."
            @(Get-ReuseLines).Count | Should Be 0
        }
        It 'accepts a clean base merge on top of a rebased head, because the content matches' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @(New-ReuseMergeSteps -Head $script:RebasedAgainMerge -Base $script:BaseUpdateCommits[1])
            Invoke-ReviewMergeFixture
            @(Get-ReuseLines).Count | Should Be 1
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:RebasedAgainMerge -> $script:MergeSha"
        }
        It 'accepts the owner approval that sits on a clean base merge after the request head, after a rebase' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @(New-ReuseMergeSteps -Head $script:Rebased -Base $script:ReviewBase -Reviews @((New-Review -Head $script:CleanMerge)))
            Invoke-ReviewMergeFixture
            $reuse = @(Get-ReuseLines)
            $reuse.Count | Should Be 1
            $reuse[0].approvalCommit | Should Be $script:CleanMerge
            $reuse[0].oldHead | Should Be $script:ReviewedSha
        }
        It 'refuses a rebase when the owner approval sits on a commit outside the reviewed history' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:Rebased -BaseHead $script:ReviewBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:NonMerge))))
            Get-Message { Invoke-ReviewMergeFixture } |
                Should Be "#7 stop: owner approval is on $script:NonMerge, not on review request $script:ReviewedSha or a clean base merge after it."
            @(Get-ReuseLines).Count | Should Be 0
        }
        It 'does not let a reuse record authorize a head: a forged record for a changed head changes nothing' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Add-ReuseFixture -NewHead $script:RebasedChanged
            Set-Responses @((New-PrStep -Pr (New-Pr -Head $script:RebasedChanged -BaseHead $script:ReviewBase)),
                (New-ReviewStep -Reviews @((New-Review -Head $script:ReviewedSha))))
            Get-Message { Invoke-ReviewMergeFixture } | Should Match 'patch-id differs from the request'
            @(Get-Calls).Count | Should Be 2
        }
        It 'binds a reuse record to its request by head and time' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Add-ReuseFixture -NewHead $script:Rebased
            (Get-LedgerRequest).Reuses.Count | Should Be 1
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            (Get-LedgerRequest).Reuses.Count | Should Be 1
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId -RequestedAt '2026-01-02T00:00:00Z'
            (Get-LedgerRequest).Reuses.Count | Should Be 0
            Add-ReuseFixture -NewHead $script:Rebased -RequestedAt '2025-12-31T23:00:00Z'
            (Get-LedgerRequest).Reuses.Count | Should Be 0
            Add-LedgerFixture -Head $script:CleanMerge -RequestedAt '2026-01-03T00:00:00Z'
            Add-ReuseFixture -OldHead $script:ReviewedSha -NewHead $script:Rebased -RequestedAt '2026-01-03T00:00:00Z'
            (Get-LedgerRequest).Reuses.Count | Should Be 0
        }
        It 'fails closed on a ledger entry of an unknown kind for the pull request' {
            Add-LedgerFixture -Head $script:ReviewedSha
            [IO.File]::AppendAllText($script:LedgerFile, '{"kind":"other","owner":"' + $script:Owner + '","repo":"' + $script:RepoName + '","number":7}' + "`n")
            Get-Message { Get-LedgerRequest } | Should Be 'The review ledger contains an invalid entry or could not be read.'
        }
        It 'fails closed on a reuse record without its approval commit' {
            Add-LedgerFixture -Head $script:ReviewedSha
            [IO.File]::AppendAllText($script:LedgerFile, '{"kind":"reuse","owner":"' + $script:Owner + '","repo":"' + $script:RepoName + '","number":7,"mode":"tree-equal","oldHead":"' + $script:ReviewedSha + '","newHead":"' + $script:Rebased + '","approvalId":1}' + "`n")
            Get-Message { Get-LedgerRequest } | Should Be 'The review ledger contains an invalid entry or could not be read.'
        }
        It 'refuses a second merge call of the same pull request while one is running' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $lock = [IO.FileStream]::new("$script:LedgerFile.$($script:Owner)_$($script:RepoName)_7.lock", [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                Set-Responses @()
                Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: another merge of this pull request is running.'
                @(Get-Calls).Count | Should Be 0
            } finally { $lock.Dispose() }
        }
        It 'releases the lock after a merge' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps)
            Invoke-ReviewMergeFixture
            @(Get-ChildItem -LiteralPath (Split-Path $script:LedgerFile) -Filter '*.lock').Count | Should Be 0
        }
    }

    Describe 'Push after an owner review' {
        BeforeEach {
            Reset-Fixture; Import-Fixture
            # The fixtures already contain every object. Only fetching is replaced; the probe commit and patch-ids use real Git.
            Mock Sync-GhAppReviewCommits {}
            $script:PushLogFile = Join-Path $script:FixtureRoot 'push-after-review.log'
            if ([IO.File]::Exists($script:PushLogFile)) { [IO.File]::Delete($script:PushLogFile) }
            $script:PatchId = Get-GhAppPatchId $script:ReviewGitRoot $script:ReviewedSha $script:ReviewBase
        }
        AfterEach { $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-ref', 'HEAD', $script:ReviewedSha) }
        function Set-ReviewHead { param([string]$Sha) $null = Invoke-GhAppGit $script:ReviewGitRoot @('update-ref', 'HEAD', $Sha) }
        function New-GuardReadSteps {
            $item = @{ number = 7; base = @{ ref = 'main'; sha = $script:ReviewBase } }
            New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @($item)
            New-ReviewStep -Reviews @((New-Review))
        }
        function New-GuardWriteSteps {
            param([string]$LocalBase, [string]$Head, [string]$Remote)
            $baseTree = (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', "$LocalBase^{tree}")).Trim()
            $headTree = (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', "$Head^{tree}")).Trim()
            $blob = (Invoke-GhAppGit $script:ReviewGitRoot @('rev-parse', "${Head}:shared 中文.txt")).Trim()
            New-ApiStep "git/commits/$Remote" -Value @{ tree = @{ sha = $baseTree } }
            New-ApiStep git/blobs POST @{ sha = $blob } -App
            New-ApiStep git/trees POST @{ sha = $headTree } -App
            New-ApiStep git/commits POST @{ sha = $script:MergeSha } -App
            New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $Remote } }
            New-ApiStep 'git/refs/heads/test%2Fmodule' PATCH @{} -App
        }
        function Invoke-GuardPush {
            param([string]$LocalBase, [string]$Remote, [string]$Extra, [switch]$NoAllow)
            Push-GhAppBranch -Worktree $script:ReviewGitRoot -LocalBase $LocalBase -RemoteParent $Remote -ExtraParent $Extra `
                -Branch test/module -MessageFile $script:BodyFile -AllowOpenPr:(-not $NoAllow) -LedgerPath $script:LedgerFile -PushLogPath $script:PushLogFile
        }

        It 'allows the old head plus one clean base merge, without a patch-id, and logs base-merge' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-ReviewHead $script:CleanMerge
            Set-Responses @((New-GuardReadSteps) + (New-GuardWriteSteps -LocalBase $script:ReviewedSha -Head $script:CleanMerge -Remote $script:ReviewedSha))
            $result = Invoke-GuardPush -LocalBase $script:ReviewedSha -Remote $script:ReviewedSha -Extra $script:ReviewBase
            $result.OwnerReviewPush | Should Be 'base-merge'
            $result.Commit | Should Be $script:MergeSha
            $log = [IO.File]::ReadAllText($script:PushLogFile)
            $log | Should Match "push after owner review kind=base-merge pr=#7 branch=test/module parents=$script:ReviewedSha,$script:ReviewBase"
        }
        It 'allows a rebase that keeps the recorded patch-id and logs rebase' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-ReviewHead $script:Rebased
            # The remote head is not a clean chain from the request, so only the patch-id can prove the content.
            Set-Responses @((New-GuardReadSteps) + (New-GuardWriteSteps -LocalBase $script:ReviewBase -Head $script:Rebased -Remote $script:NonMerge))
            $result = Invoke-GuardPush -LocalBase $script:ReviewBase -Remote $script:NonMerge -Extra $script:ReviewBase
            $result.OwnerReviewPush | Should Be 'rebase'
            [IO.File]::ReadAllText($script:PushLogFile) | Should Match 'kind=rebase pr=#7 branch=test/module'
        }
        It 'refuses a push whose content changed, and writes no log' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-ReviewHead $script:RebasedChanged
            Set-Responses @(New-GuardReadSteps)
            Get-Message { Invoke-GuardPush -LocalBase $script:ReviewBase -Remote $script:ReviewedSha -Extra $script:ReviewBase } |
                Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
            @(Get-Calls).Count | Should Be 2
            [IO.File]::Exists($script:PushLogFile) | Should Be $false
        }
        It 'refuses a rebase whose patch-id matches but whose content differs only in white space' {
            Add-LedgerFixture -Head $script:ReviewedSha -PatchId $script:PatchId
            Set-ReviewHead $script:RebasedWhitespace
            Set-Responses @(New-GuardReadSteps)
            Get-Message { Invoke-GuardPush -LocalBase $script:ReviewBase -Remote $script:NonMerge -Extra $script:ReviewBase } |
                Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
            @(Get-Calls).Count | Should Be 2
            [IO.File]::Exists($script:PushLogFile) | Should Be $false
        }
        It 'verifies and publishes the one commit that HEAD named when the push began' {
            Set-ReviewHead $script:CleanMerge
            Mock Write-GhAppCommit { $script:SeenCandidate = $Candidate; [pscustomobject]@{ Files = 0; Tree = $script:BaseTree; Commit = $script:MergeSha } }
            Set-Responses @((New-ApiStep 'pulls?state=open&head=example-owner%3Atest%2Fmodule&per_page=100&page=1' -Value @()),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -ExitCode 1 -ErrorText 'HTTP 404'), (New-ApiStep git/refs POST @{} -App))
            $null = Invoke-GuardPush -LocalBase $script:ReviewedSha -Remote $script:ReviewedSha
            $script:SeenCandidate | Should Be $script:CleanMerge
        }
        It 'refuses a rebase when the request was recorded without a patch-id' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-ReviewHead $script:Rebased
            Set-Responses @(New-GuardReadSteps)
            Get-Message { Invoke-GuardPush -LocalBase $script:ReviewBase -Remote $script:NonMerge -Extra $script:ReviewBase } |
                Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
            @(Get-Calls).Count | Should Be 2
        }
        It 'refuses when the pull request has no review request in the ledger' {
            Set-ReviewHead $script:CleanMerge
            Set-Responses @(New-GuardReadSteps)
            Get-Message { Invoke-GuardPush -LocalBase $script:ReviewedSha -Remote $script:ReviewedSha -Extra $script:ReviewBase } |
                Should Be '#7 has an owner review; push refused. Open a follow-up PR instead.'
        }
        It 'still needs AllowOpenPr for a push that is allowed after a review' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-ReviewHead $script:CleanMerge
            Set-Responses @(New-GuardReadSteps)
            Get-Message { Invoke-GuardPush -LocalBase $script:ReviewedSha -Remote $script:ReviewedSha -Extra $script:ReviewBase -NoAllow } |
                Should Be '#7 is open; push refused. Pass -AllowOpenPr only if it has not been sent to the owner.'
            [IO.File]::Exists($script:PushLogFile) | Should Be $false
        }
    }

    Describe 'Repositories without required checks' {
        BeforeEach {
            Reset-Fixture; Import-Fixture; Mock Start-Sleep {}
            Mock Sync-GhAppReviewCommits {}
            Mock Get-GhAppTimelineObservation { 'unavailable' }
        }
        # gh 2.92.0 prints this for both calls when the head has no check (cli/cli v2.92.0, pkg/cmd/pr/checks/checks.go).
        $script:GhNoChecks = "no checks reported on the 'test/module' branch"
        function New-NoRequiredStep {
            param([string]$ErrorText = $script:GhNoChecks, [int]$ExitCode = 1)
            @{ Arguments = @('pr', 'checks', '7', '--repo', $script:RepositoryName, '--required', '--json', 'name,bucket,state'); App = $false
                Output = ''; Error = $ErrorText; ExitCode = $ExitCode }
        }
        function New-AllChecksStep {
            param([string]$Output, [int]$ExitCode, [string]$ErrorText = '')
            @{ Arguments = @('pr', 'checks', '7', '--repo', $script:RepositoryName, '--json', 'name,bucket,state'); App = $false
                Output = $Output; Error = $ErrorText; ExitCode = $ExitCode }
        }
        function New-NoRequiredMergeSteps {
            param($AllChecks)
            $steps = @(New-ReviewMergeSteps)
            @($steps[0], $steps[1], (New-NoRequiredStep), $AllChecks) + @($steps[3..6])
        }
        foreach ($case in @(@{ Name = 'prints nothing'; Output = '' }, @{ Name = 'prints an empty list'; Output = '[]' })) {
            It "merges when there are no required checks and the head has no check at all (gh $($case.Name))" {
                Add-LedgerFixture -Head $script:ReviewedSha
                Set-Responses @(New-NoRequiredMergeSteps (New-AllChecksStep -Output $case.Output -ExitCode 1 -ErrorText $script:GhNoChecks))
                Invoke-ReviewMergeFixture
                [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:ReviewedSha -> $script:MergeSha"
                @(Get-Calls | Where-Object { $_.Arguments[0] -ceq 'pr' -and $_.Arguments[1] -ceq 'checks' }).Count | Should Be 2
            }
        }
        It 'merges when there are no required checks and every check of the head passed' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-NoRequiredMergeSteps (New-AllChecksStep -Output '[{"name":"lint","bucket":"pass","state":"SUCCESS"}]' -ExitCode 0))
            Invoke-ReviewMergeFixture
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:ReviewedSha -> $script:MergeSha"
        }
        It 'stops when there are no required checks and a check of the head failed' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $steps = @(New-ReviewMergeSteps)
            Set-Responses @($steps[0], $steps[1], (New-NoRequiredStep), (New-AllChecksStep -Output '[{"name":"lint","bucket":"fail","state":"FAILURE"}]' -ExitCode 1))
            Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: checks failed.'
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
        }
        foreach ($failure in @('authentication failed: HTTP 401', 'error connecting to api.github.com', '')) {
            It "stops when the required-checks command fails and prints nothing ($failure)" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @(New-ReviewMergeSteps)
                $broken = New-NoRequiredStep
                $broken.Error = $failure
                Set-Responses @($steps[0], $steps[1], $broken)
                Get-Message { Invoke-ReviewMergeFixture } | Should Match '^HTTP unknown '
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
            It "stops when the all-checks command fails and prints nothing ($failure)" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @(New-ReviewMergeSteps)
                Set-Responses @($steps[0], $steps[1], (New-NoRequiredStep), (New-AllChecksStep -Output '' -ExitCode 1 -ErrorText $failure))
                Get-Message { Invoke-ReviewMergeFixture } | Should Match '^HTTP unknown '
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
        }
        It "reads all checks when the required call says the older 'no required checks reported' wording" {
            Add-LedgerFixture -Head $script:ReviewedSha
            $older = New-NoRequiredStep -ErrorText "no required checks reported on the 'test/module' branch"
            $steps = @(New-ReviewMergeSteps)
            Set-Responses @(@($steps[0], $steps[1], $older, (New-AllChecksStep -Output '[]' -ExitCode 1 -ErrorText $script:GhNoChecks)) + @($steps[3..6]))
            Invoke-ReviewMergeFixture
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 merged $script:ReviewedSha -> $script:MergeSha"
        }
        foreach ($near in @(
                @{ Name = 'a different case'; Text = "No checks reported on the 'test/module' branch"; Exit = 1 },
                @{ Name = 'text before the message'; Text = "error: no checks reported on the 'test/module' branch"; Exit = 1 },
                @{ Name = 'a second line'; Text = "no checks reported on the 'test/module' branch`nauthentication failed: HTTP 401"; Exit = 1 },
                @{ Name = 'text after the message'; Text = "no checks reported on the 'test/module' branch: HTTP 401"; Exit = 1 },
                @{ Name = 'no branch name'; Text = 'no checks reported'; Exit = 1 },
                @{ Name = 'an exit code other than 1'; Text = $script:GhNoChecks; Exit = 8 })) {
            It "does not take $($near.Name) for a repository without checks, in the required call" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @(New-ReviewMergeSteps)
                Set-Responses @($steps[0], $steps[1], (New-NoRequiredStep -ErrorText $near.Text -ExitCode $near.Exit))
                Get-Message { Invoke-ReviewMergeFixture } | Should Match '^HTTP unknown '
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
            It "does not take $($near.Name) for a repository without checks, in the all-checks call" {
                Add-LedgerFixture -Head $script:ReviewedSha
                $steps = @(New-ReviewMergeSteps)
                Set-Responses @($steps[0], $steps[1], (New-NoRequiredStep), (New-AllChecksStep -Output '' -ExitCode $near.Exit -ErrorText $near.Text))
                Get-Message { Invoke-ReviewMergeFixture } | Should Match '^HTTP unknown '
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
        }
        It 'keeps the old message when a required check failed' {
            Add-LedgerFixture -Head $script:ReviewedSha
            $steps = @(New-ReviewMergeSteps)
            Set-Responses @($steps[0], $steps[1], (New-ChecksStep -Bucket fail -ExitCode 1))
            Get-Message { Invoke-ReviewMergeFixture } | Should Be '#7 stop: required checks failed.'
        }
        It 'does not read all checks when a required check exists' {
            Add-LedgerFixture -Head $script:ReviewedSha
            Set-Responses @(New-ReviewMergeSteps)
            Invoke-ReviewMergeFixture
            @(Get-Calls | Where-Object { $_.Arguments[0] -ceq 'pr' -and $_.Arguments[1] -ceq 'checks' }).Count | Should Be 1
        }
    }

    Describe 'Tier B merge' {
        $script:DefaultTierBScopes = $script:TierBScopes
        $script:AuditFile = Join-Path $script:FixtureRoot 'tier-b-merges.jsonl'
        BeforeEach {
            Reset-Fixture; Import-Fixture
            if ([IO.File]::Exists($script:AuditFile)) { [IO.File]::Delete($script:AuditFile) }
            $script:TierBScopes = @(@{ Repository = $script:RepositoryName; BasePattern = '^main$' })
            $script:CoreScope = $false
        }
        AfterEach { $script:TierBScopes = $script:DefaultTierBScopes; $script:CoreScope = $false }
        # The Core entry of the default list, moved to the test repository. It keeps the path floor and the debt ledgers.
        function Set-CoreScope {
            $core = @($script:DefaultTierBScopes | Where-Object { $_.Repository -ceq 'Dennis40816/nvt_fw_core' })[0]
            $script:TierBScopes = @(@{ Repository = $script:RepositoryName; BasePattern = '^main$'; ProtectedPathPattern = $core.ProtectedPathPattern; DebtLedgers = $core.DebtLedgers })
            $script:CoreScope = $true
        }
        function New-ThreadsStep {
            param([object[]]$Threads = @(), [bool]$HasNext = $false, [string]$RawOutput)
            $data = @{ data = @{ repository = @{ pullRequest = @{ reviewThreads = @{ nodes = @($Threads);
                pageInfo = @{ hasNextPage = $HasNext; endCursor = 'cursor-1' } } } } } }
            $output = $(if ($PSBoundParameters.ContainsKey('RawOutput')) { $RawOutput } else { ConvertTo-Json -InputObject $data -Depth 10 -Compress })
            @{ Arguments = @('api', 'graphql', '--hostname', 'github.com', '--method', 'POST', '--input', '-'); App = $false
                Output = $output; Error = ''; ExitCode = 0 }
        }
        function New-TierBReadSteps {
            param($Pr = (New-Pr), [object[]]$Reviews = @((New-RecordReview)), [object[]]$Runs = @((New-Run)), $Threads, [object[]]$Files = @(@{ filename = 'tools/example.ps1'; additions = 3; deletions = 1 }),
                $ChangedFiles)
            New-PrStep 7 $Pr
            New-ReviewStep 7 $Reviews
            New-RunsStep $Runs $script:HeadSha
            New-FilesStep 7 $Files
            if ($null -eq $Threads) { New-ThreadsStep } else { New-ThreadsStep -Threads @($Threads) }
            # A scope with a path floor reads the file count of the pull request to prove that the file list is complete.
            if ($script:CoreScope) { New-ApiStep 'pulls/7' -Value $(if ($PSBoundParameters.ContainsKey('ChangedFiles')) { $ChangedFiles } else { @{ changed_files = @($Files).Count } }) }
        }
        function New-TierBMergeSteps {
            param($Fresh = (New-Pr))
            New-PrStep 7 $Fresh
            New-ApiStep 'pulls/7/merge' PUT -App -Value @{ merged = $true; sha = $script:MergeSha }
            New-PrStep 7 (New-Pr -State MERGED)
        }
        function Invoke-TierB {
            param([string]$Head = $script:HeadSha, [string]$DeleteBranchPrefix)
            if ($DeleteBranchPrefix) {
                Merge-GhAppTierBPullRequest -Number 7 -ExpectedHeadSha $Head -LogPath $script:LogFile -DeleteBranchPrefix $DeleteBranchPrefix
            } else {
                Merge-GhAppTierBPullRequest -Number 7 -ExpectedHeadSha $Head -LogPath $script:LogFile
            }
        }
        It 'merges when every condition holds, with the head as the merge boundary, and writes one audit line' {
            Set-Responses (@(New-TierBReadSteps) + @(New-TierBMergeSteps))
            $null = Invoke-TierB 3>$null
            $calls = @(Get-Calls)
            $calls.Count | Should Be 8
            @($calls | Where-Object AppToken).Count | Should Be 1
            $merge = $calls[6]
            $merge.AppToken | Should Be $true
            $merge.Arguments[1] | Should Be "/repos/$script:RepositoryName/pulls/7/merge"
            $body = ConvertFrom-Json $merge.Input
            $body.sha | Should Be $script:HeadSha
            $body.merge_method | Should Be 'merge'
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 tier B merged $script:HeadSha -> $script:MergeSha"
            $lines = @([IO.File]::ReadAllLines($script:AuditFile))
            $lines.Count | Should Be 1
            $entry = ConvertFrom-Json $lines[0]
            $entry.number | Should Be 7
            $entry.head | Should Be $script:HeadSha
            $entry.mergeSha | Should Be $script:MergeSha
            $entry.repo | Should Be $script:RepoName
            $entry.base | Should Be 'main'
            $entry.mergedBy | Should Be 'example-app[bot]'
            $entry.basis | Should Match 'review record accepts this head'
            @($entry.workflows) -join ',' | Should Be 'Core'
            $lines[0] | Should Match '"mergedAt":"\d{4}-\d\d-\d\dT'
            Assert-NoToken
        }
        It 'writes the audit file next to the ledger, outside every repository' {
            Set-Responses (@(New-TierBReadSteps) + @(New-TierBMergeSteps))
            $null = Invoke-TierB 3>$null
            [IO.Path]::GetDirectoryName($script:AuditFile) | Should Be ([IO.Path]::GetDirectoryName($script:LedgerFile))
            Test-GhAppRepositoryPath $script:AuditFile | Should Be $false
        }
        It 'needs no review request and no owner approval' {
            Set-Responses (@(New-TierBReadSteps) + @(New-TierBMergeSteps))
            $null = Invoke-TierB 3>$null
            [IO.File]::Exists($script:LedgerFile) | Should Be $false
        }
        It 'merges when the review threads are all resolved' {
            Set-Responses (@(New-TierBReadSteps -Threads @(@{ isResolved = $true })) + @(New-TierBMergeSteps))
            $null = Invoke-TierB 3>$null
            @(Get-Calls).Count | Should Be 8
        }
        It 'deletes the branch only when the prefix matches' {
            Set-Responses (@(New-TierBReadSteps) + @(New-TierBMergeSteps) + @(
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep "compare/main...$script:HeadSha" -Value @{ status = 'behind'; ahead_by = 0 }),
                (New-ApiStep 'git/ref/heads/test%2Fmodule' -Value @{ object = @{ sha = $script:HeadSha } }),
                (New-ApiStep 'git/refs/heads/test%2Fmodule' DELETE -App -RawOutput '')))
            $null = Invoke-TierB -DeleteBranchPrefix 'test/'
            [IO.File]::ReadAllText($script:LogFile) | Should Match "#7 deleted branch test/module at $script:HeadSha"
        }
        It 'keeps the branch when no prefix is given' {
            Set-Responses (@(New-TierBReadSteps) + @(New-TierBMergeSteps))
            $null = Invoke-TierB 3>$null
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 kept branch test/module; no matching delete prefix'
        }
        It 'refuses a repository that is not in the tier B list, before any call' {
            $script:TierBScopes = $script:DefaultTierBScopes
            Get-Message { Invoke-TierB } | Should Be "#7 tier B merge refused: $script:RepositoryName is not a tier B repository. Only the owner approves there."
            @(Get-Calls).Count | Should Be 0
            [IO.File]::Exists($script:AuditFile) | Should Be $false
        }
        It 'lists the four repositories in the default tier B scopes' {
            $names = @($script:DefaultTierBScopes | ForEach-Object { $_.Repository })
            $names.Count | Should Be 4
            foreach ($expected in @('Dennis40816/nvt-event-buffer-replay', 'Dennis40816/nvt-freeform-helper', 'Dennis40816/nvt_fw_combiner', 'Dennis40816/nvt_fw_core')) {
                ($names -ccontains $expected) | Should Be $true
            }
        }
        foreach ($case in @(
                @('src/Nvt.Core.Avalonia/Theme/ToggleTokens.axaml', 'axaml'), @('src/Nvt.Core.Avalonia/Views/MainView.axaml.cs', 'axaml code-behind'),
                @('src/Nvt.Core.Avalonia/Theme/Tokens.cs', 'theme code'), @('src/Nvt.Core.Avalonia/Icons/IconCatalog.cs', 'icon code'),
                @('src/Nvt.Core.Avalonia/Assets/logo.png', 'image'), @('src/Nvt.Core.Fonts/Assets/NotoSansTC/Regular.ttf', 'font'),
                @('src/Nvt.Core.Avalonia/Views/Panel.cs', 'view code'), @('tools/gh-app/NvtGhApp.psm1', 'approval mechanism'), @('tools/repo-checks/repo-health.ps1', 'guard code'),
                @('scripts/pack.ps1', 'pack script'),
                @('.github/workflows/core.yml', 'workflow'), @('eng/code-health/policy.ps1', 'eng policy'), @('eng/code-health/seam-owners.json', 'eng seam owners'),
                @('eng/code-health/schema/x.json', 'eng schema'), @('Directory.Build.props', 'props'),
                @('tests/Nvt.Core.Tests/Nvt.Core.Tests.csproj', 'csproj'), @('src/Nvt.Core/PublicAPI.Shipped.txt', 'public api'),
                @('docs/governance/rules.md', 'governance'), @('TOOLS/GH-APP/x.psm1', 'upper case'),
                @('actions/approval-check/approval_check.py', 'shared action'), @('actions/path-guard/check.ps1', 'shared action ps1'),
                @('tests/approval-check/test_approval_check.py', 'approval-check self-test'),
                @('tests/Nvt.Core.TestSupport/ManualTimeProvider.cs', 'packed test support'),
                @('tests/Nvt.Core.Avalonia.Tests/Theme/ThemeContractTests.cs', 'theme contract test'),
                @('tests/Nvt.Core.Avalonia.Tests/Theme/Baseline/ThemeTokens.xml', 'theme baseline xml'),
                @('tests/Nvt.Core.Tests/Files/RootedPathGuardTests.cs', 'guard test'),
                @('tests/Nvt.Core.Tests/Architecture/LayerTests.cs', 'architecture folder'),
                @('tests/Nvt.Core.TestSupport.Tests/PublicApiTests.cs', 'public api test'),
                @('tests/Nvt.Core.Tests/Launcher/LauncherPublicSurfaceTests.cs', 'public surface test'),
                @('docs/core/adr/0001-record.md', 'nested adr'), @('docs/adr/0002-record.md', 'adr'))) {
            It "refuses a Core PR that changes a protected path ($($case[1])), and makes no merge call" {
                Set-CoreScope
                Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'docs/core/a.md'; additions = 1; deletions = 0 }, @{ filename = $case[0]; additions = 1; deletions = 0 })))
                Get-Message { Invoke-TierB } | Should Match "Paths: $([regex]::Escape($case[0])) is a protected path of $script:RepositoryName; only the owner approves a change there"
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
        }
        It 'refuses a Core PR that renames a file away from a protected path' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'docs/core/moved.md'; previous_filename = 'src/Nvt.Core.Avalonia/Theme/Old.axaml'; additions = 0; deletions = 0 })))
            Get-Message { Invoke-TierB } | Should Match 'Paths: src/Nvt.Core.Avalonia/Theme/Old.axaml is a protected path'
        }
        It 'merges a Core PR that fixes or refactors ordinary src code, other tools and tests (owner 2026-11-06 08:24)' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'src/Nvt.Core/Files/RootedPath.cs'; additions = 6; deletions = 3 },
                        @{ filename = 'src/Nvt.Core.Avalonia/ViewModels/MainViewModel.cs'; additions = 2; deletions = 2 },
                        @{ filename = 'tools/theme-export/export.py'; additions = 1; deletions = 1 },
                        @{ filename = 'tests/Nvt.Core.Tests/Files/RootedPathTests.cs'; additions = 8; deletions = 0 },
                        @{ filename = 'docs/core/modules/Files.md'; additions = 1; deletions = 0 },
                        @{ filename = 'CHANGELOG.md'; additions = 2; deletions = 0 }, @{ filename = 'docs/core/Architecture/overview.md'; additions = 1; deletions = 1 })) + @(New-TierBMergeSteps))
            Invoke-TierB
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 1
        }
        It 'merges a Core PR that changes only tests, existing documents and a debt ledger that loses lines' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'tests/Nvt.Core.Tests/Foo/BarTests.cs'; additions = 4; deletions = 1 }, @{ filename = 'docs/core/modules/Theme.md'; additions = 1; deletions = 1 },
                        @{ filename = 'eng/code-health/baseline.json'; additions = 0; deletions = 9; status = 'modified' }, @{ filename = 'eng/code-health/test-debt.json'; additions = 0; deletions = 2; status = 'modified' },
                        @{ filename = 'README.md'; additions = 1; deletions = 0 })) + @(New-TierBMergeSteps))
            Invoke-TierB
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 1
        }
        foreach ($case in @(
                @{ Name = 'gains a line'; File = @{ filename = 'eng/code-health/baseline.json'; additions = 1; deletions = 3; status = 'modified' } },
                @{ Name = 'is added'; File = @{ filename = 'eng/code-health/test-debt.json'; additions = 0; deletions = 0; status = 'added' } },
                @{ Name = 'is removed'; File = @{ filename = 'eng/code-health/baseline.json'; additions = 0; deletions = 40; status = 'removed' } },
                @{ Name = 'is renamed in'; File = @{ filename = 'eng/code-health/baseline.json'; previous_filename = 'docs/core/old.json'; additions = 0; deletions = 0; status = 'renamed' } })) {
            It "refuses a Core PR when a debt ledger $($case.Name)" {
                Set-CoreScope
                Set-Responses @((New-TierBReadSteps -Files @($case.File)))
                Get-Message { Invoke-TierB } | Should Match 'is a debt ledger and gains lines or is added, removed or renamed'
                @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
            }
        }
        It 'refuses a Core PR whose file list is shorter than the files it changes' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'docs/core/a.md'; additions = 1; deletions = 0 }) -ChangedFiles @{ changed_files = 2 }))
            Get-Message { Invoke-TierB } | Should Match 'Files: the pull request changes 2 file\(s\) and 1 were listed; the path floor cannot be proven'
        }
        It 'refuses a Core PR that changes more files than GitHub lists' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'docs/core/a.md'; additions = 1; deletions = 0 }) -ChangedFiles @{ changed_files = 3001 }))
            Get-Message { Invoke-TierB } | Should Match 'Files: the pull request changes 3001 file\(s\)'
        }
        It 'refuses a Core PR when the file count cannot be read' {
            Set-CoreScope
            Set-Responses @((New-TierBReadSteps -Files @(@{ filename = 'docs/core/a.md'; additions = 1; deletions = 0 }) -ChangedFiles @{}))
            Get-Message { Invoke-TierB } | Should Match 'Files: the pull request changes -1 file\(s\)'
        }
        It 'does not read the file count for a repository without a path floor' {
            Set-Responses @((New-TierBReadSteps) + @(New-TierBMergeSteps))
            Invoke-TierB
            @(Get-Calls | Where-Object { $_.Arguments[1] -ceq '/repos/' + $script:RepositoryName + '/pulls/7' }).Count | Should Be 0
        }
        It 'keeps the other repositories free of a path floor' {
            foreach ($scope in @($script:DefaultTierBScopes | Where-Object { $_.Repository -cne 'Dennis40816/nvt_fw_core' })) { $scope.ContainsKey('ProtectedPathPattern') | Should Be $false }
        }
        It 'limits Core to main in the default list' {
            $core = @($script:DefaultTierBScopes | Where-Object { $_.Repository -ceq 'Dennis40816/nvt_fw_core' })
            $core.Count | Should Be 1
            [regex]::IsMatch('main', $core[0].BasePattern) | Should Be $true
            foreach ($base in @('release/core/1.0.0', 'feature/x', 'main2', '1.2.x', 'Main')) { [regex]::IsMatch($base, $core[0].BasePattern) | Should Be $false }
        }
        It 'limits NFC to its development branches in the default list' {
            $nfc = @($script:DefaultTierBScopes | Where-Object { $_.Repository -ceq 'Dennis40816/nvt_fw_combiner' })[0]
            foreach ($base in @('1.2.x', '1.3.x', '10.12.x')) { [regex]::IsMatch($base, $nfc.BasePattern) | Should Be $true }
            foreach ($base in @('main', '1.2.0', '1.2.x-fix', 'release/1.2.x')) { [regex]::IsMatch($base, $nfc.BasePattern) | Should Be $false }
        }
        It 'refuses a base that is outside the allowed bases, and makes no merge call' {
            $script:TierBScopes = @(@{ Repository = $script:RepositoryName; BasePattern = '^\d+\.\d+\.x$' })
            Set-Responses @((New-TierBReadSteps))
            Get-Message { Invoke-TierB } | Should Match 'Base: main is not a tier B base of example-owner/example-repo'
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
        }
        It 'refuses a PR without a review record on the head, and makes no merge call' {
            Set-Responses @(New-TierBReadSteps -Reviews @())
            Get-Message { Invoke-TierB } | Should Match 'ReviewRecord: no review on this head matches'
            @(Get-Calls).Count | Should Be 5
            [IO.File]::Exists($script:AuditFile) | Should Be $false
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 tier B error: '
        }
        It 'refuses a review record that says changes' {
            $review = New-RecordReview -Body "Review record: $script:HeadSha changes"
            Set-Responses @(New-TierBReadSteps -Reviews @($review))
            Get-Message { Invoke-TierB } | Should Match 'ReviewRecord'
        }
        It 'refuses a review record that names another head' {
            $review = New-RecordReview -Body "Review record: $script:NextSha accept"
            Set-Responses @(New-TierBReadSteps -Reviews @($review))
            Get-Message { Invoke-TierB } | Should Match 'ReviewRecord'
        }
        It 'refuses when the latest run of a workflow did not succeed' {
            Set-Responses @(New-TierBReadSteps -Runs @((New-Run -Conclusion failure)))
            Get-Message { Invoke-TierB } | Should Match 'Run: Core: .*completed/failure'
            @(Get-Calls).Count | Should Be 5
        }
        It 'refuses while the latest run is still in progress' {
            Set-Responses @(New-TierBReadSteps -Runs @((New-Run -Status in_progress)))
            Get-Message { Invoke-TierB } | Should Match 'Run: Core'
        }
        It 'refuses when no workflow run exists for the head' {
            Set-Responses @(New-TierBReadSteps -Runs @())
            Get-Message { Invoke-TierB } | Should Match 'Runs: no workflow run found for this head'
        }
        It 'refuses when the head is not the expected head' {
            Set-Responses @(New-TierBReadSteps)
            Get-Message { Invoke-TierB -Head $script:NextSha } | Should Match "Head: head is $script:HeadSha; expected $script:NextSha"
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
        }
        It 'refuses while a review thread is not resolved' {
            Set-Responses @(New-TierBReadSteps -Threads @(@{ isResolved = $true }, @{ isResolved = $false }))
            Get-Message { Invoke-TierB } | Should Match 'Threads: 1 review thread\(s\) are not resolved'
            @(Get-Calls).Count | Should Be 5
        }
        It 'counts an unresolved thread on a later page' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @((New-RecordReview))), (New-RunsStep), (New-FilesStep),
                (New-ThreadsStep -Threads @(@{ isResolved = $true }) -HasNext $true), (New-ThreadsStep -Threads @(@{ isResolved = $false })))
            Get-Message { Invoke-TierB } | Should Match 'Threads: 1 review thread'
        }
        It 'refuses when the review threads cannot be read' {
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews @((New-RecordReview))), (New-RunsStep), (New-FilesStep),
                (New-ThreadsStep -RawOutput '{"errors":[{"message":"denied"}]}'))
            Get-Message { Invoke-TierB } | Should Be 'The review threads of #7 could not be read.'
            @(Get-Calls | Where-Object { $_.Arguments[1] -match '/merge$' }).Count | Should Be 0
        }
        foreach ($state in @('BLOCKED', 'UNSTABLE', 'BEHIND', 'DIRTY', 'UNKNOWN')) {
            It "refuses the $state merge state, because only CLEAN counts" {
                Set-Responses @(New-TierBReadSteps -Pr (New-Pr -MergeState $state))
                Get-Message { Invoke-TierB } | Should Match "MergeState: must be CLEAN, is $state"
                @(Get-Calls).Count | Should Be 5
            }
        }
        It 'refuses a draft PR' {
            Set-Responses @(New-TierBReadSteps -Pr (New-Pr -Draft))
            Get-Message { Invoke-TierB } | Should Match 'Draft: the PR is still a draft'
        }
        It 'refuses a PR by another author' {
            Set-Responses @(New-TierBReadSteps -Pr (New-Pr -Author other-author))
            Get-Message { Invoke-TierB } | Should Match 'Author: author is other-author'
        }
        It 'refuses a closed PR' {
            Set-Responses @(New-TierBReadSteps -Pr (New-Pr -State CLOSED))
            Get-Message { Invoke-TierB } | Should Match 'State: state is CLOSED'
        }
        It 'reports every failed condition in one message' {
            Set-Responses @(New-TierBReadSteps -Pr (New-Pr -MergeState BLOCKED) -Reviews @() -Runs @((New-Run -Conclusion failure)) -Threads @(@{ isResolved = $false }))
            $message = Get-Message { Invoke-TierB }
            $message | Should Match 'ReviewRecord'
            $message | Should Match 'Run: Core'
            $message | Should Match 'MergeState: must be CLEAN, is BLOCKED'
            $message | Should Match 'Threads: 1'
        }
        It 'refuses when the PR changed between the checks and the merge, and makes no merge call' {
            Set-Responses (@(New-TierBReadSteps) + @(New-PrStep -Pr (New-Pr -Head $script:NextSha)))
            Get-Message { Invoke-TierB } | Should Be '#7 tier B merge refused: the pull request changed during the checks.'
            @(Get-Calls).Count | Should Be 6
            [IO.File]::Exists($script:AuditFile) | Should Be $false
        }
        It 'refuses when the merge state turned from CLEAN just before the merge' {
            Set-Responses (@(New-TierBReadSteps) + @(New-PrStep -Pr (New-Pr -MergeState BEHIND)))
            Get-Message { Invoke-TierB } | Should Match 'changed during the checks'
        }
        It 'stops when the merge call does not report a merge' {
            Set-Responses (@(New-TierBReadSteps) + @((New-PrStep), (New-ApiStep 'pulls/7/merge' PUT -App -Value @{ merged = $false; sha = $script:MergeSha })))
            Get-Message { Invoke-TierB } | Should Be '#7 stop: merge failed.'
            [IO.File]::Exists($script:AuditFile) | Should Be $false
        }
        It 'rejects an empty DeleteBranchPrefix before any call' {
            Get-Message { Merge-GhAppTierBPullRequest -Number 7 -ExpectedHeadSha $script:HeadSha -LogPath $script:LogFile -DeleteBranchPrefix ' ' } |
                Should Be 'DeleteBranchPrefix must be nonempty when given.'
            @(Get-Calls).Count | Should Be 0
        }
        It 'rejects a head that is not a full SHA before any call' {
            Get-Message { Merge-GhAppTierBPullRequest -Number 7 -ExpectedHeadSha 'abc123' -LogPath $script:LogFile } | Should Match 'ExpectedHeadSha'
            @(Get-Calls).Count | Should Be 0
        }
    }
}
