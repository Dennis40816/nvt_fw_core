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
            [string]$Branch = 'test/module', [switch]$Fork)
        @{ state = $State; headRefOid = $Head; headRefName = $Branch; baseRefName = 'main';
            mergeStateStatus = $MergeState; isCrossRepository = [bool]$Fork; mergeCommit = @{ oid = $script:MergeSha } }
    }
    function New-PrStep {
        param([int]$Number = 7, $Pr = (New-Pr))
        @{ Arguments = @('pr', 'view', [string]$Number, '--repo', $script:RepositoryName, '--json',
            'state,headRefOid,headRefName,baseRefName,mergeStateStatus,isCrossRepository,mergeCommit');
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
        foreach ($path in @($env:NVT_GHAPP_CALLS, $env:NVT_GHAPP_INDEX, $env:NVT_GHAPP_HELPER_CALLS, $script:LogFile)) {
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
if ($env:NVT_GHAPP_HELPER_FAIL) { [Console]::Error.Write('ghs_' + 'FAKE'); exit 1 }
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
        It 'rejects malformed JSON and clears any earlier configuration' {
            $null = Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName
            [IO.File]::WriteAllText($script:FixtureConfig, '{')
            Get-Message { Import-GhAppConfig -Owner $script:Owner -Repo $script:RepoName } | Should Be 'The repository config file must contain a JSON object.'
            Get-Message { Get-GhAppContext '' } | Should Be 'Import-GhAppConfig must succeed before using this module.'
        }
        foreach ($publicFunction in @('Push-GhAppBranch', 'New-GhAppPullRequest', 'Set-GhAppPullRequestBody', 'Add-GhAppComment',
            'Add-GhAppReviewRecord', 'Merge-GhAppApprovedPullRequest', 'Close-GhAppPullRequest', 'Invoke-GhAppRead')) {
            It "rejects another repository in $publicFunction before gh" {
                Import-Fixture
                $parameters = @{ Repository = 'other-owner/other-repo' }
                switch ($publicFunction) {
                    'Push-GhAppBranch' { $parameters += @{ Worktree = $script:FixtureRoot; LocalBase = $script:HeadSha; RemoteParent = $script:HeadSha; Branch = 'test/module'; MessageFile = $script:BodyFile } }
                    'New-GhAppPullRequest' { $parameters += @{ Head = 'test/module'; Title = 'Synthetic title'; BodyFile = $script:BodyFile } }
                    'Merge-GhAppApprovedPullRequest' { $parameters += @{ Numbers = @(7); LogPath = $script:LogFile } }
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
        BeforeEach { Reset-Fixture; Import-Fixture }
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
            $env:NVT_GHAPP_HELPER_FAIL = '1'
            $message = Get-Message { Add-GhAppComment 7 $script:BodyFile }
            $message | Should Be "HTTP unknown /repos/$script:RepositoryName/issues/7/comments"
            @(Get-Calls).Count | Should Be 0
            Assert-NoToken $message
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
        Merge-GhAppApprovedPullRequest -Numbers $Numbers -LogPath $script:LogFile -PollSeconds 1 -TimeoutSeconds $TimeoutSeconds
    }

    Describe 'Owner-approved merges' {
        BeforeEach { Reset-Fixture; Import-Fixture; Mock Start-Sleep {} }
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
            try { Merge-GhAppApprovedPullRequest -Numbers 7 -LogPath '.\merge.log' } finally { Pop-Location }
            [IO.File]::ReadAllText($script:LogFile) | Should Match '#7 merged'
            Assert-NoToken
        }
        foreach ($reviews in @(
            @(), @((New-Review -Head $script:NextSha)), @((New-Review -Login other-reviewer)),
            @((New-Review), (New-Review -State CHANGES_REQUESTED -Id 2)),
            @((New-Review), (New-Review -State DISMISSED -Id 2))
        )) {
            It "rejects an absent, stale, or superseded owner approval ($($reviews.Count) records)" {
                Set-Responses @((New-PrStep), (New-ReviewStep -Reviews $reviews))
                Get-Message { Invoke-MergeFixture -Numbers @(7, 8) } | Should Be "#7 stop: no owner approval on $script:HeadSha."
                @(Get-Calls).Count | Should Be 2
            }
        }
        It 'updates a behind branch through the App and checks approval on the new head' {
            $steps = @((New-PrStep -Pr (New-Pr -MergeState BEHIND)), (New-ReviewStep),
                (New-ApiStep pulls/7/update-branch PUT @{} -App)) + @(New-MergeSteps -Head $script:NextSha)
            Set-Responses $steps
            Invoke-MergeFixture
            (ConvertFrom-Json @(Get-Calls)[2].Input).expected_head_sha | Should Be $script:HeadSha
            [IO.File]::ReadAllText($script:LogFile) | Should Match 'approval kept'
            Assert-NoToken
        }
        It 'uses submission order when a lower review ID requests changes last' {
            $reviews = @((New-Review -State CHANGES_REQUESTED -Id 1 -SubmittedAt '2026-01-01T01:00:00Z'),
                (New-Review -Id 2 -SubmittedAt '2026-01-01T00:00:00Z'))
            Set-Responses @((New-PrStep), (New-ReviewStep -Reviews $reviews))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: no owner approval on $script:HeadSha."
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
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: no owner approval on $script:HeadSha."
            @(Get-Calls).Count | Should Be 2
        }
        It 'stops after an update when approval did not carry' {
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BEHIND)), (New-ReviewStep),
                (New-ApiStep pulls/7/update-branch PUT @{} -App), (New-PrStep -Pr (New-Pr -Head $script:NextSha)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: approval did not carry to $script:NextSha."
            @(Get-Calls).Count | Should Be 5
        }
        It 'requires manual conflict resolution and owner approval' {
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState DIRTY)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: merge conflict with main; manual resolution requires owner approval.'
            @(Get-Calls).Count | Should Be 2
        }
        It 'stops on a failed update with only the HTTP status and API path' {
            Set-Responses @((New-PrStep -Pr (New-Pr -MergeState BEHIND)), (New-ReviewStep),
                (New-ApiStep pulls/7/update-branch PUT -App -ExitCode 1 -ErrorText 'HTTP 422 private conflict details __TOKEN__'))
            Get-Message { Invoke-MergeFixture } | Should Be "HTTP 422 /repos/$script:RepositoryName/pulls/7/update-branch"
            Assert-NoToken ([IO.File]::ReadAllText($script:LogFile))
        }
        It 'stops after three updates that leave the branch behind' {
            $steps = @((New-PrStep -Pr (New-Pr -MergeState BEHIND)))
            for ($index = 0; $index -lt 3; $index++) {
                $steps += @((New-ReviewStep), (New-ApiStep pulls/7/update-branch PUT @{} -App), (New-PrStep -Pr (New-Pr -MergeState BEHIND)))
            }
            $steps += New-ReviewStep
            Set-Responses $steps
            Get-Message { Invoke-MergeFixture } | Should Be '#7 stop: still behind after 3 updates.'
            @(Get-Calls).Count | Should Be 11
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
        It 'rechecks the head after checks and refuses a stale approval' {
            Set-Responses @((New-PrStep), (New-ReviewStep), (New-ChecksStep), (New-PrStep -Pr (New-Pr -Head $script:NextSha)), (New-ReviewStep))
            Get-Message { Invoke-MergeFixture } | Should Be "#7 stop: approval did not carry to $script:NextSha."
            @(Get-Calls).Count | Should Be 5
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
