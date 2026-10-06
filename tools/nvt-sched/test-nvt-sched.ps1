# Copyright (c) 2026 Dennis Liu. All rights reserved.
#requires -Version 7.0
# Offline tests: every Task Scheduler call is replaced before exercising commands.
# Fixtures stay beside this script; only an isolated tick copy with mocked inputs is launched.
[CmdletBinding()]
param([switch]$ShowExamples)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nvt-sched.ps1')
$testRoot = Join-Path $PSScriptRoot ('.test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
$previousCommanderDir = $env:COMMANDER_DIR
$commanderRoot = Join-Path $testRoot 'commander space 中文'
$null = New-Item -ItemType Directory -Path $commanderRoot
Set-Content -LiteralPath (Join-Path $commanderRoot 'tick-shadow.ps1') -Value 'exit 0'
$env:COMMANDER_DIR = $commanderRoot
$script:CommanderDir = $commanderRoot
$script:passed = 0
$script:calls = [Collections.Generic.List[string]]::new()
$script:taskXml = $null
$script:fakeState = $null
$script:taskState = 'Ready'
$script:probe = @{ Kind = 'Missing' }
$script:denyLock = $false
$script:denyDisable = $false
$script:schedulerUnavailable = $false
$script:extraTasks = @()
$script:infoUnavailable = $false
$script:legacyXml = $null
$script:targets = [Collections.Generic.List[string]]::new()
$script:denyRegister = $false
$script:badRegistration = $false
$script:infoByKey = @{}
$realReadState = ${function:Read-NvtState}
$realProbe = ${function:Get-NvtProcessProbe}
$realLock = ${function:Open-NvtRunLock}
$realEntry = ${function:Get-NvtEntry}

# Frozen from the source generator's legacy output (EveryMinutes 20, enabled); machine values are placeholders.
# Built independently of New-NvtDefinition so migration tests catch drift from the source action.
$frozenLegacyTemplate = @'
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">

  <Triggers>
    <TimeTrigger>
      <Repetition><Interval>PT20M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
      <StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled>
    </TimeTrigger>
    <LogonTrigger><Enabled>true</Enabled><UserId>{{SID}}</UserId></LogonTrigger>
  </Triggers>
  <Principals><Principal id="CurrentUser"><UserId>{{SID}}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Priority>7</Priority>
  </Settings>
  <Actions Context="CurrentUser"><Exec><Command>{{HOST}}</Command><Arguments>-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File &quot;{{RUNNER}}&quot; -Id commander-tick</Arguments><WorkingDirectory>{{DIR}}</WorkingDirectory></Exec></Actions>
</Task>
'@
function Get-FrozenLegacyXml {
    $escape = { param($value) [Security.SecurityElement]::Escape([string]$value) }
    $frozenLegacyTemplate.Replace('{{SID}}', (& $escape (Get-NvtSid))).
        Replace('{{HOST}}', (& $escape (Get-NvtHostPath))).
        Replace('{{RUNNER}}', (& $escape (Join-Path $PSScriptRoot 'nvt-sched-runner.ps1'))).
        Replace('{{DIR}}', (& $escape (Split-Path (Get-NvtEntry 'commander-tick').Script -Parent)))
}

function Get-NvtSid { 'S-1-5-21-100-200-300-1001' }
# Mock the NTAccount.Translate boundary; no account/domain lookup in tests.
function ConvertTo-NvtAccountSid([string]$UserId) {
    if ($UserId -cne '{{ACCOUNT}}') { throw 'Fake unresolved identity.' }
    return Get-NvtSid
}
function Get-NvtDataRoot { Join-Path $testRoot 'NVT\sched' }
function Read-NvtState { param($TaskId) return $script:fakeState }
function Get-NvtProcessProbe { param($ProcessId) return $script:probe }
function Open-NvtRunLock {
    param($TaskId)
    $script:calls.Add('lock')
    if ($script:denyLock) { Stop-Nvt 7 'Fake busy runner.' }
    return [IO.MemoryStream]::new()
}
function Get-ScheduledTask {
    [CmdletBinding()]param($TaskPath)
    $script:calls.Add('get')
    Assert (-not $TaskPath) 'Enumerate and explicitly filter Scheduler paths.'
    if ($script:schedulerUnavailable) { throw 'Fake service failure.' }
    if ($script:taskXml) { [pscustomobject]@{ TaskName = Get-NvtName 'commander-tick'; TaskPath = '\NVT\'; State = $script:taskState; Author = 'commander'; Description = 'mock' } }
    if ($script:legacyXml) { [pscustomobject]@{ TaskName = Get-NvtName 'commander-tick' -Legacy; TaskPath = '\'; State = 'Ready' } }
    $script:extraTasks
}
function Export-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath)
    $script:calls.Add('export')
    $script:targets.Add("export|$TaskPath$TaskName")
    if ($TaskPath -eq '\') { return $script:legacyXml }
    Assert ($TaskPath -eq '\NVT\' -and $TaskName -eq 'commander-tick') 'Export exact new target.'
    return $script:taskXml
}
function Register-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath, $Xml)
    $script:calls.Add('register')
    $script:targets.Add("register|$TaskPath$TaskName")
    Assert ($TaskPath -ceq '\NVT\' -and $TaskName -ceq 'commander-tick') 'Register exact new target.'
    if ($script:denyRegister) { throw 'Fake registration failure.' }
    $script:taskXml = $Xml
    if ($script:badRegistration) { $script:taskXml = $Xml.Replace('commander</Author>', 'other</Author>') }
}
function Disable-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath = '\NVT\')
    $script:calls.Add('disable')
    $script:targets.Add("disable|$TaskPath$TaskName")
    if ($script:denyDisable) { throw 'Fake disable failure.' }
    $doc = ConvertTo-NvtXml $(if ($TaskPath -eq '\') { $script:legacyXml } else { $script:taskXml })
    $doc.Task.Settings.Enabled = 'false'
    if ($TaskPath -eq '\') { $script:legacyXml = $doc.OuterXml } else { $script:taskXml = $doc.OuterXml }
}
function Unregister-ScheduledTask {
    [CmdletBinding(SupportsShouldProcess)]param($TaskName, $TaskPath)
    $script:calls.Add('unregister')
    $script:targets.Add("unregister|$TaskPath$TaskName")
    if ($TaskPath -eq '\') { $script:legacyXml = $null } else { $script:taskXml = $null }
}
function Start-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath)
    $script:calls.Add('start')
    Assert ($TaskPath -ceq '\NVT\') 'Run only the new path.'
}
function Get-ScheduledTaskInfo {
    [CmdletBinding()]param($TaskName, $TaskPath)
    $script:calls.Add('info')
    Assert ($TaskPath.StartsWith('\')) 'Query info using the exact task path.'
    if ($script:infoUnavailable) { throw 'Fake info failure.' }
    if ($script:infoByKey.ContainsKey($TaskPath + $TaskName)) { return $script:infoByKey[$TaskPath + $TaskName] }
    [pscustomobject]@{ LastRunTime = [datetime]'2026-10-05T12:00:00'; LastTaskResult = 10; NextRunTime = [datetime]'2026-10-05T12:20:00' }
}
# Tripwires: there is no path to a live Scheduler cmdlet from these tests.
function Stop-ScheduledTask { throw 'FORBIDDEN: stop task' }
function Stop-Process { throw 'FORBIDDEN: kill process' }
function Set-ScheduledTask { throw 'FORBIDDEN: overwrite task' }
function Enable-ScheduledTask { throw 'FORBIDDEN: re-enable task' }

function Assert($Condition, [string]$Message = 'Assertion failed') {
    if (-not $Condition) { throw $Message }
}
function Reject([scriptblock]$Action, [int]$Code = 0) {
    $caught = $null
    try { & $Action > $null } catch { $caught = $_ }
    Assert ($null -ne $caught) 'Expected rejection.'
    if ($Code) { Assert ($caught.Exception.Data['NvtCode'] -eq $Code) "Expected code $Code, got $($caught.Exception.Data['NvtCode'])." }
}
function Test([string]$Name, [scriptblock]$Action) {
    $script:calls.Clear()
    $script:taskXml = $script:baseline
    $script:taskState = 'Ready'
    $script:fakeState = @{ Status = 'Running'; ProcessId = 43210; StartTimeUtc = '2026-10-05T00:00:00.0000000Z'; LastSuccessUtc = $null; ExitCode = $null }
    $script:probe = @{ Kind = 'Missing' }
    $script:denyLock = $false
    $script:denyDisable = $false
    $script:schedulerUnavailable = $false
    $script:extraTasks = @()
    $script:infoUnavailable = $false
    $script:legacyXml = $null
    $script:targets.Clear()
    $script:denyRegister = $false
    $script:badRegistration = $false
    $script:infoByKey = @{}
    try { & $Action }
    catch { throw "$Name failed: $($_.Exception.Message)`n$($_.ScriptStackTrace)" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Assert-Disabled {
    Assert ((ConvertTo-NvtXml $script:taskXml).Task.Settings.Enabled -eq 'false') 'Task must remain disabled.'
    Assert ('unregister' -notin $script:calls) 'Must not unregister.'
}
function Reject-Cli([string[]]$Arguments) {
    # These are all pre-operation failures or DryRun. Launch no other commands.
    $output = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'nvt-sched.ps1') @Arguments 2>&1
    Assert ($LASTEXITCODE -ne 0) 'CLI must fail.'
}

function Get-FixtureSnapshot {
    # Ignore access times: readers may update them; detect file/dir creation and writes.
    @(Get-ChildItem -LiteralPath $testRoot -Recurse -Force | Sort-Object FullName | ForEach-Object {
        if ($_.PSIsContainer) { $_.FullName }
        else { '{0}|{1}|{2}' -f $_.FullName, $_.LastWriteTimeUtc.Ticks, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }) -join "`n"
}

function Invoke-MockCli([string]$Scenario, [switch]$AsJson) {
    $output = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File $mockCli $Scenario -AsJson:$AsJson 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($output | Out-String).Trim() }
}

try {
    # Exercise the real CLI formatter, JSON and error/exit handling in a child
    # process, with every Scheduler entry point replaced and all IO isolated.
    $mockCli = Join-Path $testRoot 'mock-cli.ps1'
    Set-Content -LiteralPath $mockCli -Value @'
param([string]$Scenario, [switch]$AsJson)
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'nvt-sched.ps1')
function Get-NvtSid { 'S-1-5-21-1234' }
function Get-NvtProcessProbe { param($ProcessId) @{ Kind = 'Missing' } }
function Get-NvtDataRoot { Join-Path $PSScriptRoot 'cli-data\NVT\sched' }
function Get-NvtEntry {
    param($TaskId)
    @{ Script = Join-Path (Split-Path $PSScriptRoot -Parent) 'nvt-sched.ps1'; SuccessExitCodes = @(0, 10); Description = '測試每 {0} 分鐘，腳本 {1}，維護 commander. Mock every {0} minutes, script {1}, maintainer commander.' }
}
function ConvertTo-NvtAccountSid([string]$UserId) {
    if ($UserId -cne '{{ACCOUNT}}') { throw 'Fake unresolved identity.' }
    Get-NvtSid
}
function Get-ScheduledTask {
    [CmdletBinding()]param($TaskPath)
    if ($TaskPath) { throw 'Wrong path.' }
    if ($Scenario -eq 'Failure') { throw 'discard-this-service-error' }
    if ($Scenario -ne 'Missing') {
        [pscustomobject]@{ TaskName = 'commander-tick'; TaskPath = '\NVT\'; State = 'Ready' }
    }
    if ($Scenario -eq 'Unmanaged') {
        [pscustomobject]@{ TaskName = 'old-tick'; TaskPath = '\NVT\'; State = 'Disabled' }
    }
}
function Get-ScheduledTaskInfo {
    [CmdletBinding()]param($TaskName, $TaskPath)
    if ($Scenario -eq 'InfoFailure') { throw 'discard-this-info-error' }
    [pscustomobject]@{ LastTaskResult = 10; NextRunTime = [datetime]'2026-10-05T12:20:00' }
}
function Deny-Mutation {
    Set-Content -LiteralPath (Join-Path $PSScriptRoot 'forbidden-mutation') -Value forbidden
    throw 'FORBIDDEN: query attempted a mutation'
}
function Write-NvtError { Deny-Mutation }
function Write-NvtJson { Deny-Mutation }
function Open-NvtRunLock { Deny-Mutation }
function Export-ScheduledTask {
    [CmdletBinding()]param($TaskName, $TaskPath)
    if ($TaskPath -cne '\NVT\' -or $TaskName -cne (Get-NvtName 'commander-tick')) { throw 'Wrong export target.' }
    if ($Scenario -eq 'ExportFailure') { throw 'discard-this-export-error' }
    $expected = ConvertTo-NvtXml (New-NvtDefinition 'commander-tick').Xml
    $registered = ConvertTo-NvtXml (Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'registered-task.fixture.xml') -Raw)
    $registered.Task.Principals.Principal.UserId = Get-NvtSid
    foreach ($node in $expected.Task.RegistrationInfo.ChildNodes) {
        $null = $registered.Task.RegistrationInfo.AppendChild($registered.ImportNode($node, $true))
    }
    foreach ($field in 'Command', 'Arguments', 'WorkingDirectory') {
        $registered.Task.Actions.Exec.$field = $expected.Task.Actions.Exec.$field
    }
    if ($Scenario -eq 'Mismatch') { $registered.Task.Actions.Exec.Command = 'cmd.exe' }
    $registered.OuterXml
}
function Register-ScheduledTask { Deny-Mutation }
function Disable-ScheduledTask { Deny-Mutation }
function Unregister-ScheduledTask { Deny-Mutation }
function Start-ScheduledTask { Deny-Mutation }
function Set-ScheduledTask { Deny-Mutation }
function Enable-ScheduledTask { Deny-Mutation }
function Stop-ScheduledTask { Deny-Mutation }
Invoke-NvtCli -Command list -Json:$AsJson
'@
    $script:baseline = (New-NvtDefinition 'commander-tick').Xml
    $registered = ConvertTo-NvtXml (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'registered-task.fixture.xml') -Raw)
    $expected = ConvertTo-NvtXml $script:baseline
    # Upgrade only registration metadata/action paths; retain execution defaults/account omissions.
    foreach ($node in $expected.Task.RegistrationInfo.ChildNodes) {
        $null = $registered.Task.RegistrationInfo.AppendChild($registered.ImportNode($node, $true))
    }
    foreach ($field in 'Command', 'Arguments', 'WorkingDirectory') {
        $registered.Task.Actions.Exec.$field = $expected.Task.Actions.Exec.$field
    }
    $script:registered = $registered.OuterXml
    Test 'name uses injected SID and exact allowlisted ID' {
        Assert ((Get-NvtName 'commander-tick') -ceq 'commander-tick')
        Assert ((Get-NvtName 'commander-tick' -Legacy) -ceq 'NVT-S-1-5-21-100-200-300-1001-commander-tick')
        Reject { Get-NvtName 'other' } 2
        Reject { Get-NvtName 'Commander-Tick' } 2
    }
    Test 'allowlist contains only the fixed shadow tick and success codes' {
        $entry = Get-NvtEntry 'commander-tick'
        Assert ($entry.Script -ceq (Join-Path $commanderRoot 'tick-shadow.ps1'))
        Assert (($entry.SuccessExitCodes -join ',') -ceq '0,10')
        Reject { Get-NvtEntry '..\commander-tick' } 2
    }
    Test 'commander folder is required and explicit CLI argument overrides environment' {
        $saved = $script:CommanderDir
        $savedEnvironment = $env:COMMANDER_DIR
        try {
            $script:CommanderDir = ''
            $env:COMMANDER_DIR = ''
            Reject { Get-NvtEntry 'commander-tick' } 2
            $output = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'nvt-sched.ps1') add -DryRun 2>&1
            Assert ($LASTEXITCODE -eq 2 -and ($output | Out-String) -match 'COMMANDER_DIR')
            $output = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'nvt-sched-runner.ps1') -Id commander-tick 2>&1
            Assert ($LASTEXITCODE -eq 2 -and ($output | Out-String) -match 'COMMANDER_DIR')
            $env:COMMANDER_DIR = Join-Path $testRoot 'not-configured'
            $output = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'nvt-sched.ps1') add -DryRun -CommanderDir $commanderRoot 2>&1
            Assert ($LASTEXITCODE -eq 0)
            $text = $output | Out-String
            $doc = ConvertTo-NvtXml $text.Substring($text.IndexOf('<?xml'))
            Assert ($doc.Task.Actions.Exec.WorkingDirectory -ceq $commanderRoot)
            Assert ($doc.Task.Actions.Exec.Arguments.EndsWith('-CommanderDir "' + $commanderRoot + '"'))
            # Exercise Windows argument decoding without launching the real runner.
            $script:CommanderDir = $commanderRoot + '\'
            $definition = ConvertTo-NvtXml (New-NvtDefinition 'commander-tick').Xml
            $probeScript = Join-Path $testRoot 'parameter-probe.ps1'
            Set-Content -LiteralPath $probeScript -Value 'param([string]$CommanderDir) [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Write($CommanderDir)'
            $arguments = [string]$definition.Task.Actions.Exec.Arguments
            $info = [Diagnostics.ProcessStartInfo]::new((Get-NvtHostPath))
            $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true
            $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
            $info.Arguments = '-NoLogo -NoProfile -NonInteractive -File "' + $probeScript + '"' + $arguments.Substring($arguments.IndexOf(' -CommanderDir '))
            $process = [Diagnostics.Process]::Start($info)
            try {
                $decoded = $process.StandardOutput.ReadToEnd()
                $process.WaitForExit()
                Assert ($process.ExitCode -eq 0 -and $decoded -ceq $script:CommanderDir) 'Runner argument must survive Windows decoding.'
            } finally { $process.Dispose() }
        } finally {
            $script:CommanderDir = $saved
            $env:COMMANDER_DIR = $savedEnvironment
        }
        Assert ($script:calls.Count -eq 0)
    }
    Test 'CLI rejects unknown ID' { Reject-Cli @('add', '-Id', 'other', '-DryRun') }
    Test 'CLI rejects unknown command' { Reject-Cli @('execute', '-DryRun') }
    Test 'CLI rejects unknown parameter' { Reject-Cli @('add', '-CommandLine', 'whoami', '-DryRun') }
    Test 'CLI rejects extra positional argument' { Reject-Cli @('add', 'whoami', '-DryRun') }
    Test 'CLI rejects invalid repetition intervals' {
        foreach ($value in '0', '-1', '44641', 'oops') { Reject-Cli @('add', '-EveryMinutes', $value, '-DryRun') }
    }
    Test 'CLI rejects add-only options on other commands' {
        Reject-Cli @('list', '-EveryMinutes', '2', '-DryRun')
        Reject-Cli @('remove', '-DryRun')
    }
    Test 'CLI rejects Json on commands other than list' {
        Reject-Cli @('add', '-Json', '-DryRun')
        Reject { Invoke-NvtCommand run 'commander-tick' -Json } 2
        Assert ($script:calls.Count -eq 0)
    }
    Test 'list reports NotInstalled without creating data or validating executable' {
        $script:taskXml = $null
        function Get-NvtEntry { throw 'Must not validate runtime for a query.' }
        function Get-NvtDataRoot { Join-Path $testRoot 'missing\NVT\sched' }
        function Read-NvtState { param($TaskId) & $realReadState $TaskId }
        $before = Get-FixtureSnapshot
        $result = @(Invoke-NvtCommand list 'commander-tick')
        Assert ($result.Count -eq 1 -and $result[0].Status -ceq 'NotInstalled')
        Assert ($null -eq $result[0].State -and $result[0].History.Count -eq 0)
        Assert (($script:calls -join ',') -ceq 'get')
        Assert ((Get-FixtureSnapshot) -ceq $before) 'List must not create state directories.'
    }
    Test 'list includes root current-SID leftovers and never manages them' {
        $name = Get-NvtName 'commander-tick'
        $script:extraTasks = @(
            [pscustomobject]@{ TaskName = "NVT-$(Get-NvtSid)-old-tick"; TaskPath = '\'; State = 'Disabled' },
            [pscustomobject]@{ TaskName = 'NVT-S-1-5-21-999-other'; TaskPath = '\'; State = 'Ready' },
            [pscustomobject]@{ TaskName = "$name-nested"; TaskPath = '\nested\'; State = 'Ready' },
            [pscustomobject]@{ TaskName = 'Unrelated'; TaskPath = '\'; State = 'Ready' }
        )
        $before = Get-FixtureSnapshot
        $result = @(Invoke-NvtCommand list 'commander-tick')
        Assert ($result.Count -eq 2)
        Assert ($result[0].Status -ceq 'Installed' -and $result[1].Status -ceq 'Unmanaged')
        Assert ($result[1].State -ceq 'Disabled' -and $result[1].History.Count -eq 0 -and $null -eq $result[1].Worker)
        Assert (($script:calls -join ',') -ceq 'get,info,export,info') 'Only the managed definition is exported; no mutations.'
        Assert ((Get-FixtureSnapshot) -ceq $before)
    }
    Test 'NotInstalled and Unmanaged can coexist; Id does not filter list' {
        $script:taskXml = $null
        $script:extraTasks = @([pscustomobject]@{ TaskName = "NVT-$(Get-NvtSid)-leftover"; TaskPath = '\'; State = 'Ready' })
        $result = @(Invoke-NvtCommand list 'commander-tick')
        Assert ($result.Count -eq 2 -and $result[0].Status -ceq 'NotInstalled' -and $result[1].Status -ceq 'Unmanaged')
    }
    Test 'list remains readable for a modified allowlisted task definition' {
        $script:taskXml = $script:baseline.Replace('nvt-sched-runner.ps1', 'other.ps1')
        $before = Get-FixtureSnapshot
        $result = Invoke-NvtCommand list 'commander-tick'
        Assert ($result.Status -ceq 'DefinitionMismatch' -and $result.LastTaskResult -eq 10)
        Assert (($script:calls -join ',') -ceq 'get,info,export')
        Assert ((Get-FixtureSnapshot) -ceq $before)
    }
    Test 'mock CLI missing and installed queries exit zero and preserve array JSON' {
        $before = Get-FixtureSnapshot
        foreach ($scenario in 'Missing', 'Installed', 'Unmanaged', 'Mismatch') {
            $plain = Invoke-MockCli $scenario
            $jsonResult = Invoke-MockCli $scenario -AsJson
            Assert ($plain.Code -eq 0 -and $jsonResult.Code -eq 0)
            $parsed = ConvertFrom-Json -InputObject $jsonResult.Text -NoEnumerate
            Assert ($parsed -is [array]) 'JSON must always be an array, even with one result.'
            Assert ($parsed[0].History -is [array] -and $parsed[0].History.Count -eq 0)
            $expected = if ($scenario -eq 'Missing') { 'NotInstalled' } elseif ($scenario -eq 'Mismatch') { 'DefinitionMismatch' } else { 'Installed' }
            Assert ($parsed[0].Status -ceq $expected -and $plain.Text.Contains($expected))
            if ($scenario -eq 'Unmanaged') { Assert ($parsed.Count -eq 2 -and $parsed[1].Status -ceq 'Unmanaged') }
            if ($ShowExamples -and $scenario -in 'Missing', 'Mismatch') {
                Write-Output "MOCK list ($expected, exit $($plain.Code)):`n$($plain.Text)"
                Write-Output "MOCK list -Json ($expected, exit $($jsonResult.Code)):`n$($jsonResult.Text)"
            }
        }
        Assert ((Get-FixtureSnapshot) -ceq $before) 'CLI list must not write anything.'
    }
    Test 'mock CLI query failures return nonzero without recording error state or raw errors' {
        $before = Get-FixtureSnapshot
        foreach ($scenario in 'Failure', 'InfoFailure', 'ExportFailure') {
            foreach ($asJson in $false, $true) {
                $result = Invoke-MockCli $scenario -AsJson:$asJson
                Assert ($result.Code -eq 9)
                Assert ($result.Text -notmatch 'discard-this|Error status could not be saved')
            }
        }
        Assert ((Get-FixtureSnapshot) -ceq $before) 'Failed list must also be read-only.'
    }
    Test 'DryRun is a complete definition and performs no Scheduler or state IO' {
        $before = @(Get-ChildItem -LiteralPath $testRoot -Recurse -Force).Count
        $output = Invoke-NvtCommand add 'commander-tick' -Preview
        Assert ($output.StartsWith("TaskPath: \NVT\`nTaskName: commander-tick"))
        $doc = ConvertTo-NvtXml $output.Substring($output.IndexOf('<?xml'))
        Assert ($doc.Task.Triggers.TimeTrigger.Repetition.Interval -eq 'PT20M')
        Assert ($null -eq $doc.Task.Triggers.TimeTrigger.Repetition.Duration)
        Assert ($doc.Task.Triggers.LogonTrigger.UserId -eq (Get-NvtSid))
        Assert ($doc.Task.Principals.Principal.UserId -eq (Get-NvtSid))
        Assert ($doc.Task.Principals.Principal.LogonType -eq 'InteractiveToken')
        Assert ($doc.Task.Principals.Principal.RunLevel -eq 'LeastPrivilege')
        Assert ($doc.Task.Settings.StartWhenAvailable -eq 'true')
        Assert ($doc.Task.Settings.MultipleInstancesPolicy -eq 'IgnoreNew')
        Assert ($doc.Task.Settings.ExecutionTimeLimit -eq 'PT5M')
        Assert ($doc.Task.Settings.AllowHardTerminate -eq 'true')
        Assert ($doc.Task.Settings.WakeToRun -eq 'false')
        Assert ($doc.Task.Settings.DisallowStartIfOnBatteries -eq 'false')
        Assert ($doc.Task.Actions.Exec.Command -eq (Get-NvtHostPath))
        Assert ($doc.Task.Actions.Exec.Arguments -match '-NoProfile -NonInteractive -WindowStyle Hidden -File "')
        Assert ($doc.Task.Actions.Exec.Arguments -notmatch 'Password|EncodedCommand|ExecutionPolicy')
        Assert ($doc.Task.Actions.Exec.Arguments.EndsWith('-CommanderDir "' + $commanderRoot + '"'))
        Assert ($script:calls.Count -eq 0)
        Assert (@(Get-ChildItem -LiteralPath $testRoot -Recurse -Force).Count -eq $before)
    }
    Test 'custom allowed interval is reflected in XML' {
        Assert ((ConvertTo-NvtXml (New-NvtDefinition 'commander-tick' 45).Xml).Task.Triggers.TimeTrigger.Repetition.Interval -eq 'PT45M')
    }
    Test 'literal data directory is accepted; outside and traversal are rejected' {
        $null = New-NvtDefinition 'commander-tick' 20 $commanderRoot
        Reject { New-NvtDefinition 'commander-tick' 20 $testRoot } 3
        Reject { Assert-NvtPath "$testRoot\..\nvt-sched.ps1" } 3
        Reject { Assert-NvtPath '\\localhost\C$\Windows' -Directory } 3
        Reject { Assert-NvtPath (Join-Path $testRoot 'file.ps1:stream') } 3
        Reject { Assert-NvtPath ($testRoot + '.\child') -Directory } 3
    }
    Test 'Chinese and spaces in a literal path are supported' {
        $folder = Join-Path $testRoot '中文 space'
        $null = New-Item -ItemType Directory -Path $folder
        Assert ((Assert-NvtPath $folder -Directory) -eq $folder)
    }
    Test 'junction directory and paths through it are rejected' {
        $target = Join-Path $testRoot 'physical'
        $link = Join-Path $testRoot 'linked'
        $null = New-Item -ItemType Directory -Path $target
        Set-Content -LiteralPath (Join-Path $target 'test.ps1') -Value '# fixture'
        $null = New-Item -ItemType Junction -Path $link -Target $target
        try {
            Reject { Assert-NvtPath $link -Directory } 3
            Reject { Assert-NvtPath (Join-Path $link 'test.ps1') } 3
        } finally { Remove-Item -LiteralPath $link -Force }
    }
    Test 'add registers only once and initializes bounded status' {
        $script:taskXml = $null
        $null = Invoke-NvtCommand add 'commander-tick'
        Assert (@($script:calls | Where-Object { $_ -eq 'register' }).Count -eq 1)
        $state = & $realReadState 'commander-tick'
        Assert ($state.Status -eq 'NeverRun')
        $null = Invoke-NvtCommand add 'commander-tick'
        Assert (@($script:calls | Where-Object { $_ -eq 'register' }).Count -eq 1)
    }
    Test 'same settings do not register again' {
        $null = Invoke-NvtCommand add 'commander-tick'
        Assert ('register' -notin $script:calls)
    }
    Test 'different interval is not overwritten' {
        Reject { Invoke-NvtCommand add 'commander-tick' 30 } 4
        Assert ('register' -notin $script:calls)
    }
    Test 'service failure cannot be mistaken for absent task' {
        $script:schedulerUnavailable = $true
        Reject { Invoke-NvtCommand add 'commander-tick' }
        Assert ('register' -notin $script:calls)
    }
    Test 'foreign action, principal, settings and extra actions are rejected' {
        foreach ($pair in @(
            @('nvt-sched-runner.ps1', 'other.ps1'),
            @('LeastPrivilege', 'HighestAvailable'),
            @('InteractiveToken', 'Password'),
            @('<ExecutionTimeLimit>PT5M', '<ExecutionTimeLimit>PT6M'),
            @('</Actions>', '<Exec><Command>cmd.exe</Command></Exec></Actions>')
        )) {
            $script:taskXml = $script:baseline.Replace($pair[0], $pair[1])
            Reject { Invoke-NvtCommand run 'commander-tick' } 4
            Assert ('start' -notin $script:calls)
        }
    }
    Test 'original and registered export definitions normalize in both directions' {
        foreach ($xml in $script:baseline, $script:registered) {
            $doc = Assert-NvtDefinition 'commander-tick' $xml $script:baseline
            Assert ($doc.Task.Settings.Enabled -ceq 'true') 'Return defaults for runner/run checks.'
            Assert ($doc.Task.Principals.Principal.RunLevel -ceq 'LeastPrivilege')
            Assert ($doc.Task.Triggers.TimeTrigger.Enabled -ceq 'true' -and $doc.Task.Triggers.LogonTrigger.Enabled -ceq 'true')
            Assert ($doc.Task.Triggers.TimeTrigger.Repetition.StopAtDurationEnd -ceq 'false')
            Assert ($doc.Task.Triggers.LogonTrigger.UserId -ceq (Get-NvtSid))
            $null = Assert-NvtDefinition 'commander-tick' $xml
            $null = Assert-NvtDefinition 'commander-tick' $xml $script:registered
        }
    }
    Test 'named UserIds on either side translate to the same SID' {
        $doc = ConvertTo-NvtXml $script:registered
        $doc.Task.Principals.Principal.UserId = '{{ACCOUNT}}'
        $null = Assert-NvtDefinition 'commander-tick' $doc.OuterXml $script:baseline
        $null = Assert-NvtDefinition 'commander-tick' $script:baseline $doc.OuterXml
    }
    Test 'unresolvable, foreign and malformed UserIds are rejected' {
        foreach ($userId in '{{UNRESOLVED_ACCOUNT}}', 'S-1-5-21-100-200-300-1002', 'S-1-invalid') {
            $doc = ConvertTo-NvtXml $script:registered
            $doc.Task.Triggers.LogonTrigger.UserId = $userId
            Reject { Assert-NvtDefinition 'commander-tick' $doc.OuterXml $script:baseline } 4
            Reject { Assert-NvtDefinition 'commander-tick' $script:baseline $doc.OuterXml } 4
        }
    }
    Test 'identity normalization cannot erase unexpected UserId elements or attributes' {
        foreach ($kind in 'Element', 'Attribute') {
            $doc = ConvertTo-NvtXml $script:baseline
            $node = $doc.Task.Triggers.LogonTrigger.SelectSingleNode('*[local-name()="UserId"]')
            if ($kind -eq 'Element') { $node.InnerXml = '<Unexpected>' + (Get-NvtSid) + '</Unexpected>' }
            else { $node.SetAttribute('Unexpected', 'true') }
            Reject { Assert-NvtDefinition 'commander-tick' $doc.OuterXml $script:baseline } 4
        }
    }
    Test 'registered export still rejects command, arguments, working directory and extra actions' {
        foreach ($field in 'Command', 'Arguments', 'WorkingDirectory', 'ExtraAction') {
            $doc = ConvertTo-NvtXml $script:registered
            switch ($field) {
                'Command' { $doc.Task.Actions.Exec.Command = 'cmd.exe' }
                'Arguments' { $doc.Task.Actions.Exec.Arguments += ' -NoExit' }
                'WorkingDirectory' { $doc.Task.Actions.Exec.WorkingDirectory = (Join-Path $testRoot 'other') }
                'ExtraAction' { $null = $doc.Task.Actions.AppendChild($doc.Task.Actions.Exec.CloneNode($true)) }
            }
            $script:taskXml = $doc.OuterXml
            Reject { Assert-NvtDefinition 'commander-tick' $script:taskXml $script:baseline } 4
            Reject { Invoke-NvtCommand run 'commander-tick' } 4
            Assert ('start' -notin $script:calls)
        }
    }
    Test 'normalization never overwrites non-default permissions or execution settings' {
        foreach ($pair in @(
            @('Task/Principals/Principal/RunLevel', 'Highest'),
            @('Task/Principals/Principal/LogonType', 'Password'),
            @('Task/Settings/ExecutionTimeLimit', 'PT6M'),
            @('Task/Settings/MultipleInstancesPolicy', 'Parallel'),
            @('Task/Settings/AllowHardTerminate', 'false'),
            @('Task/Settings/AllowStartOnDemand', 'false'),
            @('Task/Settings/Enabled', 'false'),
            @('Task/Settings/Hidden', 'true'),
            @('Task/Settings/Priority', '8'),
            @('Task/Settings/RunOnlyIfIdle', 'true'),
            @('Task/Settings/RunOnlyIfNetworkAvailable', 'true'),
            @('Task/Settings/WakeToRun', 'true'),
            @('Task/Triggers/TimeTrigger/Enabled', 'false'),
            @('Task/Triggers/LogonTrigger/Enabled', 'false'),
            @('Task/Triggers/TimeTrigger/Repetition/StopAtDurationEnd', 'true')
        )) {
            $doc = ConvertTo-NvtNormalizedXml (ConvertTo-NvtXml $script:registered)
            $ns = [Xml.XmlNamespaceManager]::new($doc.NameTable)
            $ns.AddNamespace('t', $doc.DocumentElement.NamespaceURI)
            $path = '/t:' + $pair[0].Replace('/', '/t:')
            $doc.SelectSingleNode($path, $ns).InnerText = $pair[1]
            Reject { Assert-NvtDefinition 'commander-tick' $doc.OuterXml $script:baseline } 4
        }
    }
    Test 'registered defaults allow run and repeated add without registration' {
        $script:taskXml = $script:registered
        $null = Invoke-NvtCommand add 'commander-tick'
        $null = Invoke-NvtCommand run 'commander-tick'
        Assert ('register' -notin $script:calls -and 'start' -in $script:calls)
        Assert ((Invoke-NvtCommand list 'commander-tick').Status -ceq 'Installed')
    }
    Test 'XML DTD is rejected without resolving external content' {
        Reject { ConvertTo-NvtXml '<!DOCTYPE Task SYSTEM "file:///does-not-exist"><Task />' }
    }
    Test 'Scheduler default serialization and equivalent durations compare equal' {
        $xml = $script:baseline.Replace('<IdleSettings>', '<IdleSettings><Duration>PT10M</Duration><WaitTimeout>PT1H</WaitTimeout>')
        $xml = $xml.Replace('<Settings>', '<Settings><UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine><Volatile>false</Volatile>')
        $xml = $xml.Replace('PT20M', 'PT1200S')
        $null = Assert-NvtDefinition 'commander-tick' $xml $script:baseline
    }
    Test 'run only submits; list reports completion evidence' {
        $null = Invoke-NvtCommand run 'commander-tick'
        Assert ('start' -in $script:calls)
        $script:fakeState.ExitCode = 10
        $script:fakeState.LastSuccessUtc = '2026-10-05T00:00:00Z'
        $result = Invoke-NvtCommand list 'commander-tick'
        Assert ($result.ExitCode -eq 10 -and $result.LastTaskResult -eq 10)
        Assert ($result.LastSuccessUtc -eq '2026-10-05T00:00:00Z')
        Assert ($result.NextRunTime -eq [datetime]'2026-10-05T12:20:00')
    }
    Test 'run refuses disabled task' {
        Disable-ScheduledTask
        Reject { Invoke-NvtCommand run 'commander-tick' } 5
        Assert ('start' -notin $script:calls)
    }
    Test 'remove disables first, then unregisters only after missing PID' {
        $null = Invoke-NvtCommand remove 'commander-tick'
        Assert ($script:calls.IndexOf('disable') -lt $script:calls.IndexOf('lock'))
        Assert ($script:calls.IndexOf('lock') -lt $script:calls.IndexOf('unregister'))
        Assert (Test-Path -LiteralPath (Get-NvtStatePath 'commander-tick')) 'Retain status.'
    }
    Test 'matching PID and start time stays disabled, even after success status' {
        $script:probe = @{ Kind = 'Found'; StartTimeUtc = $script:fakeState.StartTimeUtc }
        $script:fakeState.Status = 'Succeeded'
        Reject { Invoke-NvtCommand remove 'commander-tick' } 6
        Assert-Disabled
    }
    Test 'reused PID with different start time permits removal' {
        $script:probe = @{ Kind = 'Found'; StartTimeUtc = '2026-10-05T01:00:00.0000000Z' }
        $null = Invoke-NvtCommand remove 'commander-tick'
        Assert ('unregister' -in $script:calls)
    }
    Test 'inaccessible PID stays disabled' {
        $script:probe = @{ Kind = 'Unknown' }
        Reject { Invoke-NvtCommand remove 'commander-tick' } 5
        Assert-Disabled
    }
    Test 'missing status stays disabled' {
        $script:fakeState = $null
        Reject { Invoke-NvtCommand remove 'commander-tick' } 5
        Assert-Disabled
    }
    Test 'invalid PID and timestamp stay disabled' {
        foreach ($bad in @(
            @{ Status = 'Running'; ProcessId = 0; StartTimeUtc = '2026-10-05T00:00:00.0000000Z' },
            @{ Status = 'Running'; ProcessId = 43210; StartTimeUtc = 'bad' },
            @{ Status = 'Running'; ProcessId = '43210'; StartTimeUtc = '2026-10-05T00:00:00.0000000Z' }
        )) {
            $script:fakeState = $bad
            Reject { Invoke-NvtCommand remove 'commander-tick' } 5
            Assert-Disabled
        }
    }
    Test 'runner startup lock prevents removal' {
        $script:denyLock = $true
        Reject { Invoke-NvtCommand remove 'commander-tick' } 7
        Assert-Disabled
    }
    Test 'active or unknown Scheduler state prevents removal' {
        foreach ($state in 'Running', 'Queued', 'Unknown') {
            $script:taskState = $state
            Reject { Invoke-NvtCommand remove 'commander-tick' } 5
            Assert-Disabled
        }
    }
    Test 'never-run task can be removed' {
        $script:fakeState = @{ Status = 'NeverRun'; ProcessId = 0; StartTimeUtc = $null }
        $null = Invoke-NvtCommand remove 'commander-tick'
        Assert ('unregister' -in $script:calls)
    }
    Test 'disable failure must not unregister' {
        $script:denyDisable = $true
        Reject { Invoke-NvtCommand remove 'commander-tick' }
        Assert ('unregister' -notin $script:calls)
    }
    Test 'foreign task is not disabled or removed' {
        $script:taskXml = $script:baseline.Replace('nvt-sched-runner.ps1', 'other.ps1')
        Reject { Invoke-NvtCommand remove 'commander-tick' } 4
        Assert ('disable' -notin $script:calls -and 'unregister' -notin $script:calls)
    }
    Test 'bounded JSON status and error records contain no raw log' {
        Write-NvtError 'commander-tick' 'remove' 5
        $path = Get-NvtStatePath 'commander-tick' 'error.json'
        $errorState = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        Assert ($errorState.Count -eq 3 -and $errorState.Code -eq 5)
        $path = Get-NvtStatePath 'commander-tick'
        Set-Content -LiteralPath $path -Value '{broken' -NoNewline
        Assert ($null -eq (& $realReadState 'commander-tick'))
        Set-Content -LiteralPath $path -Value ('x' * 16385) -NoNewline
        Assert ($null -eq (& $realReadState 'commander-tick'))
    }
    Test 'real per-run file lock excludes a second opener' {
        $handle = & $realLock 'commander-tick'
        try { Reject { & $realLock 'commander-tick' } 7 }
        finally { $handle.Dispose() }
    }
    Test 'real process probe identifies this test process without launching tick' {
        $actual = & $realProbe $PID
        Assert ($actual.Kind -eq 'Found')
        Assert ([DateTimeOffset]::Parse($actual.StartTimeUtc).UtcTicks -eq (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)
    }
    Test 'runner records actual PID, preserves exit codes and discards all tick output' {
        # Override only the approved fixture and the definition check in this scope.
        # The production runner, state writer and exit handling still execute.
        $fixture = Join-Path $testRoot 'fixture-tick.ps1'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        function Get-NvtOwnedDefinition { param($TaskId) ConvertTo-NvtXml $script:taskXml }
        foreach ($code in 0, 10, 1) {
            Set-Content -LiteralPath $fixture -Value ('Write-Output "discard-this-fixture-output"; Write-Warning "discard-this-warning"; exit ' + $code)
            $results = @(Invoke-NvtRunner 'commander-tick')
            Assert ($results.Count -eq 1 -and $results[0] -eq $code) "Expected runner exit $code; got $($results -join ',')."
            $state = & $realReadState 'commander-tick'
            Assert ($state.ProcessId -eq $PID)
            Assert ([DateTimeOffset]::Parse($state.StartTimeUtc).UtcTicks -eq (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks)
            $script:probe = @{ Kind = 'Found'; StartTimeUtc = $state.StartTimeUtc }
            Assert ((Get-NvtLiveness $state) -eq 'Running') 'Round-trip state must still identify the live process.'
            $script:probe = @{ Kind = 'Missing' }
            Assert ($state.ExitCode -eq $code) "Expected recorded exit $code; got $($state.ExitCode)."
            if ($code -in 0, 10) { Assert ($state.Status -eq 'Succeeded' -and $null -ne $state.LastSuccessUtc) }
            else { Assert ($state.Status -eq 'Failed') }
            Assert ((Get-Content -LiteralPath (Get-NvtStatePath 'commander-tick') -Raw) -notmatch 'discard-this')
            $history = Read-NvtHistory 'commander-tick'
            Assert ($history[0].ExitCode -eq $code)
            Assert ([DateTimeOffset]::Parse($history[0].StartTimeUtc) -le [DateTimeOffset]::Parse($history[0].EndTimeUtc))
            Assert ((Get-Content -LiteralPath (Get-NvtStatePath 'commander-tick' 'history.jsonl') -Raw) -notmatch 'discard-this')
        }
    }
    Test 'runner exception stores only an error code and preserves last success' {
        $fixture = Join-Path $testRoot 'fixture-tick.ps1'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        function Get-NvtOwnedDefinition { param($TaskId) ConvertTo-NvtXml $script:taskXml }
        Set-Content -LiteralPath $fixture -Value 'throw "do-not-record-raw-exception"'
        $script:fakeState.LastSuccessUtc = '2026-10-04T00:00:00.0000000Z'
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 1)
        $state = & $realReadState 'commander-tick'
        Assert ($state.Status -eq 'Failed' -and $state.LastSuccessUtc -eq $script:fakeState.LastSuccessUtc)
        Assert ((Get-Content -LiteralPath (Get-NvtStatePath 'commander-tick' 'error.json') -Raw) -notmatch 'do-not-record')
        Assert ((Read-NvtHistory 'commander-tick')[0].ExitCode -eq 1)
        Assert ((Get-Content -LiteralPath (Get-NvtStatePath 'commander-tick' 'history.jsonl') -Raw) -notmatch 'do-not-record')
    }
    Test 'runner accepts registered defaults and executes only a synthetic tick' {
        $fixture = Join-Path $testRoot 'fixture-tick.ps1'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        function Get-NvtOwnedDefinition {
            param($TaskId)
            Assert-NvtDefinition $TaskId $script:taskXml $script:baseline
        }
        Set-Content -LiteralPath $fixture -Value 'exit 10'
        $script:taskXml = $script:registered
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 10)
        $state = & $realReadState 'commander-tick'
        Assert ($state.Status -ceq 'Succeeded' -and $state.ExitCode -eq 10)
    }
    Test 'late runner after disable or unregister never starts tick' {
        $marker = Join-Path $testRoot 'must-not-exist'
        $fixture = Join-Path $testRoot 'fixture-tick.ps1'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        Set-Content -LiteralPath $fixture -Value ('Set-Content -LiteralPath ''' + $marker.Replace("'", "''") + ''' -Value unsafe')
        function Get-NvtOwnedDefinition { param($TaskId) ConvertTo-NvtXml $script:taskXml }
        Disable-ScheduledTask
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 1)
        Assert (-not (Test-Path -LiteralPath $marker))
        function Get-NvtOwnedDefinition { param($TaskId) throw 'Task no longer exists.' }
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 1)
        Assert (-not (Test-Path -LiteralPath $marker))
        Assert ((Read-NvtHistory 'commander-tick')[0].ExitCode -eq 1)
    }
    Test 'runner retains exactly the latest 50 history lines and list shows newest five' {
        function Get-NvtDataRoot { Join-Path $testRoot 'history-limit\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'history-limit')
        $fixture = Join-Path $testRoot 'fixture-tick.ps1'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        function Get-NvtOwnedDefinition { param($TaskId) ConvertTo-NvtXml $script:taskXml }
        Set-Content -LiteralPath $fixture -Value 'exit 10'
        # Seed 49 distinguishable entries, then exercise the real runner append/trim twice.
        $start = [DateTimeOffset]::Parse('2026-10-01T00:00:00Z')
        foreach ($index in 1..49) {
            Add-NvtHistory 'commander-tick' ($start.AddMinutes($index).ToString('o')) ($start.AddMinutes($index).AddSeconds(1).ToString('o')) $index
        }
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 10)
        Assert ((Invoke-NvtRunner 'commander-tick') -eq 10)
        $path = Get-NvtStatePath 'commander-tick' 'history.jsonl'
        $lines = @(Get-Content -LiteralPath $path)
        Assert ($lines.Count -eq 50)
        $records = @($lines | ForEach-Object { ConvertFrom-Json -InputObject $_ -AsHashtable })
        Assert ($records[0].ExitCode -eq 2 -and $records[-1].ExitCode -eq 10 -and $records[-2].ExitCode -eq 10)
        foreach ($record in $records) { Assert ($record.Count -eq 3 -and $record.ContainsKey('StartTimeUtc') -and $record.ContainsKey('EndTimeUtc') -and $record.ContainsKey('ExitCode')) }
        $before = Get-FixtureSnapshot
        $result = Invoke-NvtCommand list 'commander-tick'
        Assert ($result.History.Count -eq 5)
        Assert (($result.History.ExitCode -join ',') -ceq '10,10,49,48,47')
        $parsed = ConvertFrom-Json -InputObject (Invoke-NvtCommand list 'commander-tick' -Json) -NoEnumerate
        Assert ($parsed -is [array] -and $parsed[0].History -is [array] -and $parsed[0].History.Count -eq 5)
        $script:taskXml = $null
        $removed = Invoke-NvtCommand list 'commander-tick'
        Assert ($removed.Status -ceq 'NotInstalled' -and $removed.History.Count -eq 5) 'Retained history remains queryable after removal.'
        Assert ((Get-FixtureSnapshot) -ceq $before)
    }
    Test 'unreadable or invalid history is unknown and query never repairs it' {
        $path = Get-NvtStatePath 'commander-tick' 'history.jsonl' -Create
        foreach ($bad in '{broken', ('x' * 16385), '{"StartTimeUtc":"bad","EndTimeUtc":"bad","ExitCode":0}', '{"StartTimeUtc":"bad","EndTimeUtc":"bad","ExitCode":0,"RawLog":"discard-this"}') {
            Set-Content -LiteralPath $path -Value $bad
            $before = Get-FixtureSnapshot
            $result = Invoke-NvtCommand list 'commander-tick' -Json
            $parsed = ConvertFrom-Json -InputObject $result -NoEnumerate
            Assert ($null -eq $parsed[0].History -and $result -notmatch 'discard-this')
            Assert ((Get-FixtureSnapshot) -ceq $before)
        }
        $handle = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
        try { Assert ($null -eq (Read-NvtHistory 'commander-tick')) }
        finally { $handle.Dispose() }
    }
    Test 'mock CLI exposes history columns and stable JSON including a single history item' {
        function Get-NvtDataRoot { Join-Path $testRoot 'cli-data\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'cli-data')
        $start = [DateTimeOffset]::Parse('2026-10-05T04:00:00Z')
        Add-NvtHistory 'commander-tick' ($start.ToString('o')) ($start.AddSeconds(2).ToString('o')) 10
        $statePath = Get-NvtStatePath 'commander-tick' -Create
        Write-NvtJson $statePath @{ Status = 'Succeeded'; ProcessId = 43210; StartTimeUtc = $start.ToString('o'); UpdatedAtUtc = $start.AddSeconds(2).ToString('o'); ExitCode = 10; LastSuccessUtc = $start.AddSeconds(2).ToString('o') }
        $before = Get-FixtureSnapshot
        $plain = Invoke-MockCli 'Unmanaged'
        $jsonResult = Invoke-MockCli 'Unmanaged' -AsJson
        Assert ($plain.Code -eq 0 -and $jsonResult.Code -eq 0)
        Assert ($plain.Text -match 'History \(latest 5, newest first\)' -and $plain.Text -match 'StartTimeUtc\s+EndTimeUtc\s+ExitCode')
        $parsed = ConvertFrom-Json -InputObject $jsonResult.Text -NoEnumerate
        Assert ($parsed.Count -eq 2 -and $parsed[0].History -is [array] -and $parsed[0].History.Count -eq 1)
        Assert ($parsed[0].History[0].ExitCode -eq 10 -and $parsed[0].ExitCode -eq 10)
        Assert ($parsed[1].History -is [array] -and $parsed[1].History.Count -eq 0)
        Assert ((Get-FixtureSnapshot) -ceq $before)
        if ($ShowExamples) {
            Write-Output "MOCK list (Installed + Unmanaged, exit $($plain.Code)):`n$($plain.Text)"
            Write-Output "MOCK list -Json (Installed + Unmanaged, exit $($jsonResult.Code)):`n$($jsonResult.Text)"
        }
    }
    Test 'new folder definition includes bilingual description, interval, SID and runner path' {
        $definition = New-NvtDefinition 'commander-tick' 45
        $doc = ConvertTo-NvtXml $definition.Xml
        Assert ($definition.TaskPath -ceq '\NVT\' -and $definition.TaskName -ceq 'commander-tick')
        Assert ($doc.Task.RegistrationInfo.Author -ceq 'commander')
        Assert ($doc.Task.RegistrationInfo.Description -match '每 45 分鐘' -and $doc.Task.RegistrationInfo.Description -match 'every 45 minutes')
        Assert ($doc.Task.RegistrationInfo.Description.Contains('tick-shadow.ps1') -and $doc.Task.RegistrationInfo.Description.Contains('維護：commander'))
        Assert ($doc.Task.Actions.Exec.Arguments.Contains(' -TaskPath \NVT\ -CommanderDir "'))
        Assert ($doc.Task.Actions.Exec.Arguments.EndsWith('-CommanderDir "' + $commanderRoot + '"'))
        Assert ($doc.Task.Principals.Principal.UserId -ceq (Get-NvtSid))
        foreach ($field in 'Author', 'Description') {
            $changed = ConvertTo-NvtXml $definition.Xml
            $changed.Task.RegistrationInfo.$field = 'changed'
            Reject { Assert-NvtDefinition 'commander-tick' $changed.OuterXml $definition.Xml } 4
        }
    }
    Test 'legacy definition reproduces the source action without TaskPath or CommanderDir' {
        $legacy = New-NvtDefinition 'commander-tick' -Legacy
        $frozen = Get-FrozenLegacyXml
        Assert ($legacy.TaskPath -ceq '\' -and $legacy.TaskName -ceq (Get-NvtName 'commander-tick' -Legacy))
        Assert ((Get-NvtXmlShape (ConvertTo-NvtNormalizedXml (ConvertTo-NvtXml $legacy.Xml))) -ceq
            (Get-NvtXmlShape (ConvertTo-NvtNormalizedXml (ConvertTo-NvtXml $frozen))))
        $arguments = (ConvertTo-NvtXml $legacy.Xml).Task.Actions.Exec.Arguments
        Assert ($arguments.EndsWith(' -Id commander-tick') -and -not $arguments.Contains('-TaskPath') -and -not $arguments.Contains('-CommanderDir'))
        $null = Assert-NvtDefinition 'commander-tick' $frozen -Legacy
        $withDir = $frozen.Replace(' -Id commander-tick<', ' -Id commander-tick -CommanderDir &quot;X&quot;<')
        Reject { Assert-NvtDefinition 'commander-tick' $withDir -Legacy } 4
    }
    Test 'install exports and validates the new task before removing legacy and retains all records' {
        $script:taskXml = $null
        $script:legacyXml = (Get-FrozenLegacyXml)
        $statePath = Get-NvtStatePath 'commander-tick' -Create
        $stateBefore = [IO.File]::ReadAllText($statePath)
        $historyPath = Get-NvtStatePath 'commander-tick' 'history.jsonl'
        $historyBefore = [IO.File]::ReadAllText($historyPath)
        $message = Invoke-NvtCommand install 'commander-tick'
        Assert ($message -match 'verified; legacy removed; history retained')
        Assert ($script:taskXml -and -not $script:legacyXml)
        $newTarget = '\NVT\commander-tick'
        $oldTarget = '\' + (Get-NvtName 'commander-tick' -Legacy)
        Assert ($script:targets.IndexOf("register|$newTarget") -lt $script:targets.IndexOf("export|$newTarget"))
        Assert ($script:targets.IndexOf("export|$newTarget") -lt $script:targets.IndexOf("disable|$oldTarget"))
        Assert ($script:targets.IndexOf("disable|$oldTarget") -lt $script:targets.IndexOf("unregister|$oldTarget"))
        Assert ([IO.File]::ReadAllText($statePath) -ceq $stateBefore)
        Assert ([IO.File]::ReadAllText($historyPath) -ceq $historyBefore)
        $null = Invoke-NvtCommand install 'commander-tick'
        Assert (@($script:calls | Where-Object { $_ -eq 'register' }).Count -eq 1)
    }
    Test 'registration failure leaves legacy definition and enabled state untouched' {
        $script:taskXml = $null
        $script:legacyXml = (Get-FrozenLegacyXml)
        $original = $script:legacyXml
        $script:denyRegister = $true
        Reject { Invoke-NvtCommand install 'commander-tick' }
        Assert ($script:legacyXml -ceq $original -and -not $script:taskXml)
        Assert ('disable' -notin $script:calls -and 'unregister' -notin $script:calls)
    }
    Test 'post-registration metadata mismatch preserves legacy and returns definition error' {
        $script:taskXml = $null
        $script:legacyXml = (Get-FrozenLegacyXml)
        $original = $script:legacyXml
        $script:badRegistration = $true
        Reject { Invoke-NvtCommand install 'commander-tick' } 4
        Assert ($script:legacyXml -ceq $original)
        Assert ('disable' -notin $script:calls -and 'unregister' -notin $script:calls)
    }
    Test 'legacy cleanup with a busy lock retains verified new and disabled legacy for retry' {
        $script:legacyXml = (Get-FrozenLegacyXml)
        $script:denyLock = $true
        Reject { Invoke-NvtCommand install 'commander-tick' } 7
        Assert ($script:taskXml -and $script:legacyXml)
        Assert ((ConvertTo-NvtXml $script:legacyXml).Task.Settings.Enabled -ceq 'false')
        Assert ('unregister' -notin $script:calls)
        $script:denyLock = $false
        $null = Invoke-NvtCommand install 'commander-tick'
        Assert (-not $script:legacyXml -and $script:taskXml)
    }
    Test 'foreign legacy principal is never disabled or removed' {
        $script:legacyXml = (Get-FrozenLegacyXml).Replace((Get-NvtSid), 'S-1-5-21-100-200-300-1002')
        Reject { Invoke-NvtCommand install 'commander-tick' } 4
        Assert ('disable' -notin $script:calls -and 'unregister' -notin $script:calls)
    }
    Test 'list flags both allowlisted and unknown legacy names while status queries the new folder' {
        $script:legacyXml = (Get-FrozenLegacyXml)
        $script:extraTasks = @([pscustomobject]@{ TaskName = "NVT-$(Get-NvtSid)-old-tick"; TaskPath = '\'; State = 'Disabled' })
        $before = Get-FixtureSnapshot
        $tasks = @(Invoke-NvtCommand list 'commander-tick')
        Assert ($tasks.Count -eq 3 -and -not $tasks[0].Legacy -and $tasks[0].TaskPath -ceq '\NVT\')
        Assert ($tasks[1].Legacy -and $tasks[2].Legacy -and $tasks[1].Status -ceq 'Unmanaged')
        $status = ConvertFrom-Json -InputObject (Invoke-NvtCommand status 'commander-tick' -Json) -NoEnumerate
        Assert ($status.Count -eq 3 -and $status[0].Status -ceq 'Installed')
        Assert ((Get-FixtureSnapshot) -ceq $before)
    }
    Test 'legacy runner verifies its root definition and refuses a disabled legacy task' {
        $script:legacyXml = (Get-FrozenLegacyXml)
        $null = Get-NvtOwnedDefinition 'commander-tick' -Legacy
        $fixture = Join-Path $testRoot 'legacy-tick.ps1'
        Set-Content -LiteralPath $fixture -Value 'exit 10'
        function Get-NvtEntry { param($TaskId) @{ Script = $fixture; SuccessExitCodes = @(0, 10) } }
        # Preserve the real legacy validation above, and use the synthetic action below.
        function Get-NvtOwnedDefinition { param($TaskId, [switch]$Legacy) Assert $Legacy; ConvertTo-NvtXml $script:legacyXml }
        Assert ((Invoke-NvtRunner 'commander-tick' -Legacy) -eq 10)
        Disable-ScheduledTask -TaskName (Get-NvtName 'commander-tick' -Legacy) -TaskPath '\'
        Assert ((Invoke-NvtRunner 'commander-tick' -Legacy) -eq 1)
    }
    Test 'audit excludes Microsoft and classifies failures, never-run, disabled, author and ours' {
        function Get-NvtDataRoot { Join-Path $testRoot 'audit-classify\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'audit-classify')
        $script:extraTasks = @(
            [pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'fail'; State = 'Ready'; Author = 'Vendor'; Description = 'failed updater' },
            [pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'never'; State = 'Ready'; Author = ''; Description = '<b>[未使用](https://invalid)</b>' },
            [pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'off'; State = 'Disabled'; Author = 'Vendor'; Description = 'disabled' },
            [pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'running'; State = 'Running'; Author = 'Vendor'; Description = 'running' },
            [pscustomobject]@{ TaskPath = '\Microsoft\Windows\'; TaskName = 'excluded'; State = 'Ready' },
            [pscustomobject]@{ TaskPath = '\MicrosoftVendor\'; TaskName = 'included'; State = 'Ready'; Author = 'Vendor'; Description = 'vendor' },
            [pscustomobject]@{ TaskPath = '\NVT\child\'; TaskName = 'ours'; State = 'Ready'; Author = 'commander'; Description = 'ours' }
        )
        foreach ($task in $script:extraTasks) {
            $code = switch ($task.TaskName) { 'fail' { 1 }; 'never' { 267011 }; 'running' { 267009 }; default { 0 } }
            $script:infoByKey[$task.TaskPath + $task.TaskName] = [pscustomobject]@{
                LastTaskResult = $code; LastRunTime = [datetime]'2026-10-05T08:00:00'; NextRunTime = [datetime]'2026-10-06T08:00:00'
            }
        }
        $result = Invoke-NvtAudit ([DateTimeOffset]'2026-10-05T08:30:00+08:00')
        $snapshot = Get-Content -LiteralPath (Join-Path (Split-Path $result.Path -Parent) 'latest.json') -Raw | ConvertFrom-Json -AsHashtable
        Assert ($snapshot.Tasks.Count -eq 7 -and 'excluded' -notin $snapshot.Tasks.TaskName -and 'included' -in $snapshot.Tasks.TaskName)
        foreach ($pair in @(@('fail', '失敗'), @('never', '從沒跑過'), @('never', '沒有作者'), @('off', '已停用'), @('ours', '屬於我們的'))) {
            $task = $snapshot.Tasks | Where-Object TaskName -eq $pair[0]
            Assert ($pair[1] -in @(Get-NvtAuditFlags $task))
        }
        Assert ('失敗' -notin @(Get-NvtAuditFlags ($snapshot.Tasks | Where-Object TaskName -eq 'running')))
        $report = Get-Content -LiteralPath $result.Path -Raw
        Assert ($report.StartsWith('本次共 7 個排程') -and $report -match '## 需要注意' -and $report -match '## 我們的' -and $report -match '## 廠商的')
        Assert ($report -match '新增 0、刪除 0')
        Assert ($report -match '建議可停用' -and $report -match '從沒跑過且沒有作者' -and $report -notmatch '<b>')
        Assert ($report -match '結果 10' -and $report -match 'runner 視為成功')
        Assert ($snapshot.Count -eq 2 -and $snapshot.Tasks.Count -eq 7)
        Assert ($snapshot.Tasks[0].Count -eq 8)
        Assert ('register' -notin $script:calls -and 'disable' -notin $script:calls -and 'unregister' -notin $script:calls)
    }
    Test 'audit differences use full path and name and retain dated reports' {
        function Get-NvtDataRoot { Join-Path $testRoot 'audit-diff\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'audit-diff')
        $script:taskXml = $null
        $script:extraTasks = @([pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'same'; State = 'Ready'; Author = 'Vendor'; Description = 'before' })
        $first = Invoke-NvtAudit ([DateTimeOffset]'2026-10-05T08:30:00+08:00')
        $script:extraTasks = @([pscustomobject]@{ TaskPath = '\Other\'; TaskName = 'same'; State = 'Ready'; Author = 'Vendor'; Description = 'after' })
        $second = Invoke-NvtAudit ([DateTimeOffset]'2026-10-12T08:30:00+08:00')
        $secondLines = @(Get-Content -LiteralPath $second.Path)
        Assert ($secondLines[0].Contains('新增 1、刪除 1'))
        Assert (@($secondLines | Where-Object { $_.StartsWith('- 新增：') -and $_.Contains('Other') -and $_.Contains('same') }).Count -eq 1)
        Assert (@($secondLines | Where-Object { $_.StartsWith('- 刪除：') -and $_.Contains('Vendor') -and $_.Contains('same') }).Count -eq 1)
        Assert ((Test-Path -LiteralPath $first.Path) -and (Test-Path -LiteralPath $second.Path))
        $third = Invoke-NvtAudit ([DateTimeOffset]'2026-10-12T09:00:00+08:00')
        Assert ((Get-Content -LiteralPath $third.Path -Raw).Contains('新增 0、刪除 0'))
        $script:extraTasks = @()
        $empty = Invoke-NvtAudit ([DateTimeOffset]'2026-10-19T08:30:00+08:00')
        $emptyReport = Get-Content -LiteralPath $empty.Path -Raw
        Assert ($emptyReport.StartsWith('本次共 0 個排程') -and $emptyReport.Contains('新增 0、刪除 1'))
        Assert ('disable' -notin $script:calls)
    }
    Test 'audit failure preserves previous comparison evidence and never records job errors' {
        function Get-NvtDataRoot { Join-Path $testRoot 'audit-fail\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'audit-fail')
        $first = Invoke-NvtAudit ([DateTimeOffset]'2026-10-05T08:30:00+08:00')
        $before = Get-FixtureSnapshot
        $script:infoUnavailable = $true
        Reject { Invoke-NvtCommand audit 'commander-tick' }
        Assert ((Get-FixtureSnapshot) -ceq $before)
        $script:infoUnavailable = $false
        $latest = Join-Path (Split-Path $first.Path -Parent) 'latest.json'
        Set-Content -LiteralPath $latest -Value '{broken'
        $before = Get-FixtureSnapshot
        Reject { Invoke-NvtAudit ([DateTimeOffset]'2026-10-12T08:30:00+08:00') }
        Assert ((Get-FixtureSnapshot) -ceq $before)
        Assert ('register' -notin $script:calls -and 'disable' -notin $script:calls)
    }
    Test 'weekly audit waits for Monday 08:30, executes once and retries only notification' {
        function Get-NvtDataRoot { Join-Path $testRoot 'audit-weekly\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'audit-weekly')
        Assert ($null -eq (Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-04T12:00:00+08:00') '2026-W40'))
        Assert ($null -eq (Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-05T08:29:59+08:00')))
        Assert ($script:calls.Count -eq 0)
        $first = Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-05T08:30:00+08:00')
        Assert ($first.Week -ceq '2026-W41' -and (Test-Path -LiteralPath $first.Path))
        $calls = $script:calls.Count
        $before = Get-FixtureSnapshot
        Assert ($null -eq (Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-05T08:50:00+08:00') $first.Week))
        $retry = Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-06T09:00:00+08:00')
        Assert ($retry.Path -ceq $first.Path -and $retry.Week -ceq $first.Week)
        Assert ($script:calls.Count -eq $calls -and (Get-FixtureSnapshot) -ceq $before)
        $next = Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-10-13T09:00:00+08:00') $first.Week
        Assert ($next.Week -ceq '2026-W42' -and $next.Path -ne $first.Path)
    }
    Test 'manual audit is reused in the same ISO week and year boundaries use ISO year' {
        function Get-NvtDataRoot { Join-Path $testRoot 'audit-manual\NVT\sched' }
        $null = New-Item -ItemType Directory -Path (Join-Path $testRoot 'audit-manual')
        $manual = Invoke-NvtAudit ([DateTimeOffset]'2025-12-29T08:00:00+08:00')
        $calls = $script:calls.Count
        $weekly = Invoke-NvtWeeklyAudit ([DateTimeOffset]'2026-01-01T08:45:00+08:00')
        Assert ($weekly.Week -ceq '2026-W01' -and $weekly.Path -ceq $manual.Path)
        Assert ($script:calls.Count -eq $calls)
        Assert ((Get-NvtIsoWeek ([DateTimeOffset]'2027-01-01T08:00:00+08:00')) -ceq '2026-W53')
    }
    Test 'isolated tick logs once, returns 10 and retries a failed notification without another audit' {
        $tickRoot = Join-Path $testRoot 'isolated-tick'
        $wrapperRoot = Join-Path $tickRoot 'nvt-core-tools\scheduler'
        $null = New-Item -ItemType Directory -Path $wrapperRoot -Force
        # Synthetic caller: verify audit reuse and acknowledgement after successful logging.
        # The deployed external tick is not part of this import.
        $tickSource = @'
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nvt-core-tools\scheduler\nvt-sched.ps1')
$now = [DateTimeOffset]'2026-10-05T08:30:00+08:00'
$snapshotPath = Join-Path $PSScriptRoot 'snapshot.json'
$logPath = Join-Path $PSScriptRoot 'snapshot-changes.log'
$snapshot = if (Test-Path -LiteralPath $snapshotPath) {
    Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json -AsHashtable
} else { @{ weekly_task_audit_week = $null } }
try {
    $audit = Invoke-NvtWeeklyAudit $now $snapshot.weekly_task_audit_week
    if (-not $audit) { exit 0 }
    Add-Content -LiteralPath $logPath -Value ('weekly task audit ready: ' + $audit.Path) -ErrorAction Stop
    $snapshot.weekly_task_audit_week = $audit.Week
    Write-NvtJson $snapshotPath $snapshot
    exit 10
} catch { exit 1 }
'@
        $isolatedTick = Join-Path $tickRoot 'tick-shadow.ps1'
        Set-Content -LiteralPath $isolatedTick -Value $tickSource
        Set-Content -LiteralPath (Join-Path $tickRoot 'quota-history.csv') -Value @(
            'sample_time,used_percent,resets_at', '2026-10-05 08:29:00,39,2026-10-06 07:00'
        )
        $wrapper = @'
. '__SCHED__'
function Get-NvtSid { 'S-1-5-21-1234' }
function Get-NvtDataRoot { '__ROOT__\NVT\sched' }
function Get-ScheduledTask {
    [CmdletBinding()]param($TaskPath)
    Add-Content -LiteralPath '__ROOT__\audit-scans.log' -Value scan
    [pscustomobject]@{ TaskPath = '\Vendor\'; TaskName = 'mock'; State = 'Ready'; Author = 'Vendor'; Description = 'isolated test' }
}
function Get-ScheduledTaskInfo {
    [CmdletBinding()]param($TaskName, $TaskPath)
    [pscustomobject]@{ LastRunTime = [datetime]'2026-10-05T08:00:00'; LastTaskResult = 0; NextRunTime = [datetime]'2026-10-05T09:00:00' }
}
function Register-ScheduledTask { throw 'FORBIDDEN' }
function Export-ScheduledTask { throw 'FORBIDDEN' }
function Disable-ScheduledTask { throw 'FORBIDDEN' }
function Enable-ScheduledTask { throw 'FORBIDDEN' }
function Unregister-ScheduledTask { throw 'FORBIDDEN' }
function Set-ScheduledTask { throw 'FORBIDDEN' }
function Start-ScheduledTask { throw 'FORBIDDEN' }
function Stop-ScheduledTask { throw 'FORBIDDEN' }
'@
        $wrapper = $wrapper.Replace('__SCHED__', (Join-Path $PSScriptRoot 'nvt-sched.ps1').Replace("'", "''")).Replace('__ROOT__', $tickRoot.Replace("'", "''"))
        Set-Content -LiteralPath (Join-Path $wrapperRoot 'nvt-sched.ps1') -Value $wrapper
        $harness = Join-Path $tickRoot 'harness.ps1'
        Set-Content -LiteralPath $harness -Value @'
$ErrorActionPreference = 'Stop'
function Get-ChildItem { [CmdletBinding()]param($LiteralPath, $Filter, [switch]$File, [switch]$Force) @() }
function Get-PSDrive { [CmdletBinding()]param($Name, $PSProvider) [pscustomobject]@{ Free = 500GB } }
& (Join-Path $PSScriptRoot 'tick-shadow.ps1')
exit $LASTEXITCODE
'@
        $firstOutput = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File $harness 2>&1
        Assert ($LASTEXITCODE -eq 10) "First isolated tick must return 10: $firstOutput"
        $snapshotPath = Join-Path $tickRoot 'snapshot.json'
        $logPath = Join-Path $tickRoot 'snapshot-changes.log'
        $scanPath = Join-Path $tickRoot 'audit-scans.log'
        $snapshot = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json -AsHashtable
        Assert ($snapshot.weekly_task_audit_week -ceq '2026-W41')
        $lines = @(Get-Content -LiteralPath $logPath)
        Assert ($lines.Count -eq 1 -and $lines[0].Contains('weekly task audit ready: ' + (Join-Path $tickRoot 'NVT\sched\audit\audit-20261005.md')))
        $null = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File $harness 2>&1
        Assert ($LASTEXITCODE -eq 0 -and @(Get-Content -LiteralPath $scanPath).Count -eq 1)
        Assert (@(Get-Content -LiteralPath $logPath).Count -eq 1)
        $snapshot.weekly_task_audit_week = $null
        Write-NvtJson $snapshotPath $snapshot
        $blocked = [IO.File]::Open($logPath, 'Open', 'ReadWrite', 'None')
        try {
            $null = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File $harness 2>&1
            Assert ($LASTEXITCODE -eq 1)
            $unacknowledged = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json -AsHashtable
            Assert ($null -eq $unacknowledged.weekly_task_audit_week)
        } finally { $blocked.Dispose() }
        $null = & (Get-NvtHostPath) -NoLogo -NoProfile -NonInteractive -File $harness 2>&1
        Assert ($LASTEXITCODE -eq 10 -and @(Get-Content -LiteralPath $scanPath).Count -eq 1)
        Assert (@(Get-Content -LiteralPath $logPath).Count -eq 2)
    }
    Write-Output "RESULT: $script:passed passed, 0 failed; Scheduler mocked; synthetic tick uses isolated inputs/outputs."
} finally {
    $env:COMMANDER_DIR = $previousCommanderDir
    # Validate the absolute target, remove junctions non-recursively, then fixtures.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $expectedParent = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or [IO.Path]::GetFileName($resolved) -notmatch '^\.test-[a-f0-9]{32}$') {
        throw 'Unsafe fixture cleanup target.'
    }
    foreach ($item in Get-ChildItem -LiteralPath $resolved -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { Remove-Item -LiteralPath $item.FullName -Force }
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
