# Copyright (c) 2026 Dennis Liu. All rights reserved.
#requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)][ValidateSet('install', 'add', 'list', 'status', 'audit', 'run', 'remove')][string]$Command,
    [ValidateSet('commander-tick')][string]$Id = 'commander-tick',
    [ValidateRange(1, 44640)][int]$EveryMinutes = 20,
    [string]$DataDir,
    [string]$CommanderDir = $env:COMMANDER_DIR,
    [switch]$DryRun,
    [switch]$Json
)

# Also dot-sourced by the fixed runner and the offline tests; no work on import.
function Stop-Nvt([int]$Code, [string]$Message, [switch]$Outdated) {
    $errorObject = [InvalidOperationException]::new($Message)
    $errorObject.Data['NvtCode'] = $Code
    if ($Outdated) { $errorObject.Data['NvtDefinitionOutdated'] = $true }
    throw $errorObject
}

function Assert-NvtPath([string]$Path, [switch]$Directory) {
    # Reject ambiguous Win32 spellings, traversal, UNC/device paths and ADS.
    if ($Path -notmatch '^[A-Za-z]:\\' -or $Path.Substring(2) -match '[:/"*?<>|\x00-\x1f]' -or
        @($Path.Substring(3).Split('\') | Where-Object { $_ -in '.', '..' -or $_ -match '[ .]$' }).Count) {
        Stop-Nvt 3 'Only literal local absolute paths without traversal are allowed.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    $current = $full
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Stop-Nvt 3 'Linked paths are not allowed (including parent directories).'
        }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($parent) { $parent.FullName } else { $null }
    }
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if ([bool]$item.PSIsContainer -ne [bool]$Directory) { Stop-Nvt 3 'Unexpected path type.' }
    return $full
}

function Get-NvtSid { [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
function Get-NvtName([string]$TaskId, [switch]$Legacy) {
    if ($TaskId -cne 'commander-tick') { Stop-Nvt 2 'Id is not allowlisted.' }
    if ($Legacy) { return "NVT-$(Get-NvtSid)-$TaskId" }
    return $TaskId
}

function Get-NvtEntry([string]$TaskId) {
    $null = Get-NvtName $TaskId
    $path = Assert-NvtPath (Join-Path $PSScriptRoot 'allowlist.psd1')
    $entries = Import-PowerShellDataFile -LiteralPath $path -ErrorAction Stop
    if ($entries.Count -ne 1 -or -not $entries.ContainsKey($TaskId)) { Stop-Nvt 2 'Invalid allowlist.' }
    $entry = $entries[$TaskId]
    if ($entry.Count -ne 3 -or -not $entry.ContainsKey('Script') -or
        -not $entry.ContainsKey('Description') -or [string]::IsNullOrWhiteSpace($entry.Description) -or
        -not $entry.ContainsKey('SuccessExitCodes') -or
        ($entry.SuccessExitCodes -join ',') -cne '0,10') { Stop-Nvt 2 'Invalid allowlist entry.' }
    if (-not $script:CommanderDir) { Stop-Nvt 2 'Set COMMANDER_DIR or pass -CommanderDir (commander folder).' }
    $commander = Assert-NvtPath $script:CommanderDir -Directory
    $entry.Script = Join-Path $commander $entry.Script
    $null = Assert-NvtPath $entry.Script
    if ([IO.Path]::GetExtension($entry.Script) -ine '.ps1') { Stop-Nvt 3 'Only the allowlisted PowerShell script is supported.' }
    return $entry
}

function Get-NvtHostPath {
    # Do not resolve pwsh from PATH or accept a caller-supplied executable.
    Assert-NvtPath (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PowerShell\7\pwsh.exe')
}

function Get-NvtConsolePath {
    # Resolve the console from Windows, never PATH or a caller-supplied executable.
    Assert-NvtPath (Join-Path ([Environment]::SystemDirectory) 'conhost.exe')
}

function Get-NvtDataRoot { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NVT\sched' }

function Get-NvtStatePath([string]$TaskId, [string]$Suffix = 'json', [switch]$Create) {
    $null = Get-NvtName $TaskId
    $root = Get-NvtDataRoot
    foreach ($directory in @((Split-Path $root -Parent), $root)) {
        if (-not (Test-Path -LiteralPath $directory)) {
            if (-not $Create) { return $null }
            $null = Assert-NvtPath (Split-Path $directory -Parent) -Directory
            $null = New-Item -ItemType Directory -Path $directory -ErrorAction Stop
        }
        $null = Assert-NvtPath $directory -Directory
    }
    $path = Join-Path $root "$TaskId.$Suffix"
    if (Test-Path -LiteralPath $path) { $null = Assert-NvtPath $path }
    return $path
}

function Write-NvtJson([string]$Path, $Value) {
    # Readers also take an exclusive handle: no partially written JSON is trusted.
    $stream = [IO.File]::Open($Path, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 5 -Compress))
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.SetLength($bytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}

function Read-NvtState([string]$TaskId) {
    try {
        $path = Get-NvtStatePath $TaskId
        if (-not $path -or -not (Test-Path -LiteralPath $path)) { return $null }
        $stream = [IO.File]::Open($path, 'Open', 'Read', 'None')
        try {
            if ($stream.Length -gt 16384) { return $null }
            $reader = [IO.StreamReader]::new($stream)
            try {
                $state = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable -ErrorAction Stop
                if ($state -isnot [Collections.IDictionary]) { return $null }
                # PS 7 converts ISO JSON strings to DateTime automatically.
                # Preserve round-trip precision without requiring PS 7.5 DateKind.
                foreach ($field in 'StartTimeUtc', 'UpdatedAtUtc', 'LastSuccessUtc') {
                    if ($state[$field] -is [datetime] -or $state[$field] -is [DateTimeOffset]) {
                        $state[$field] = $state[$field].ToUniversalTime().ToString('o')
                    }
                }
                return $state
            }
            finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch { return $null }
}

function Add-NvtHistory([string]$TaskId, [string]$Started, [string]$Ended, [int]$Code) {
    $path = Get-NvtStatePath $TaskId 'history.jsonl' -Create
    # Separate exclusive handle also serializes rejected/overlapping runner attempts.
    $stream = [IO.File]::Open($path, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        if ($stream.Length -gt 16384) { Stop-Nvt 9 'History exceeds its size limit.' }
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 1024, $true)
        try { $lines = @($reader.ReadToEnd() -split '\r?\n' | Where-Object { $_.Length -gt 0 } | Select-Object -Last 49) }
        finally { $reader.Dispose() }
        $lines += ([ordered]@{ StartTimeUtc = $Started; EndTimeUtc = $Ended; ExitCode = $Code } | ConvertTo-Json -Compress)
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($lines -join "`n") + "`n")
        $stream.Position = 0
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.SetLength($bytes.Length)
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}

function Read-NvtHistory([string]$TaskId) {
    try {
        $path = Get-NvtStatePath $TaskId 'history.jsonl'
        if (-not $path -or -not (Test-Path -LiteralPath $path)) { return ,@() }
        $stream = [IO.File]::Open($path, 'Open', 'Read', 'None')
        try {
            if ($stream.Length -gt 16384) { return $null }
            $reader = [IO.StreamReader]::new($stream)
            try { $lines = @($reader.ReadToEnd() -split '\r?\n' | Where-Object { $_.Length -gt 0 } | Select-Object -Last 5) }
            finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
        $history = @(
            foreach ($line in $lines) {
                $record = $line | ConvertFrom-Json -AsHashtable -ErrorAction Stop
                if ($record -isnot [Collections.IDictionary] -or $record.Count -ne 3 -or
                    -not $record.ContainsKey('StartTimeUtc') -or -not $record.ContainsKey('EndTimeUtc') -or
                    ($record.ExitCode -isnot [int] -and $record.ExitCode -isnot [long])) { return $null }
                foreach ($field in 'StartTimeUtc', 'EndTimeUtc') {
                    if ($record[$field] -is [datetime] -or $record[$field] -is [DateTimeOffset]) {
                        $record[$field] = $record[$field].ToUniversalTime().ToString('o')
                    }
                    $record[$field] = [DateTimeOffset]::ParseExact($record[$field], 'o', [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime().ToString('o')
                }
                [pscustomobject][ordered]@{ StartTimeUtc = $record.StartTimeUtc; EndTimeUtc = $record.EndTimeUtc; ExitCode = $record.ExitCode }
            }
        )
        [array]::Reverse($history)
        return ,$history
    } catch { return $null }
}

function Write-NvtError([string]$TaskId, [string]$Operation, [int]$Code) {
    Write-NvtJson (Get-NvtStatePath $TaskId 'error.json' -Create) ([ordered]@{
        AtUtc = [DateTimeOffset]::UtcNow.ToString('o'); Operation = $Operation; Code = $Code
    })
}

function Open-NvtRunLock([string]$TaskId) {
    $path = Get-NvtStatePath $TaskId 'lock' -Create
    try { return [IO.File]::Open($path, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Stop-Nvt 7 'Runner is active or its lock cannot be read; keep the task disabled.' }
}

function Get-NvtProcessProbe([int]$ProcessId) {
    try {
        $process = [Diagnostics.Process]::GetProcessById($ProcessId)
        try { return @{ Kind = 'Found'; StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o') } }
        finally { $process.Dispose() }
    } catch [ArgumentException] { return @{ Kind = 'Missing' } }
    catch { return @{ Kind = 'Unknown' } }
}

function Get-NvtLiveness($State) {
    if ($null -eq $State) { return 'Unknown' }
    if ($State.Status -eq 'NeverRun' -and $State.ProcessId -eq 0 -and $null -eq $State.StartTimeUtc) { return 'Exited' }
    if ($State.Status -notin 'Running', 'Succeeded', 'Failed' -or
        $State.ProcessId -isnot [long] -and $State.ProcessId -isnot [int] -or $State.ProcessId -le 0) { return 'Unknown' }
    try { $started = [DateTimeOffset]::ParseExact($State.StartTimeUtc, 'o', [Globalization.CultureInfo]::InvariantCulture) }
    catch { return 'Unknown' }
    $probe = Get-NvtProcessProbe $State.ProcessId
    if ($probe.Kind -eq 'Missing') { return 'Exited' }
    if ($probe.Kind -ne 'Found') { return 'Unknown' }
    try { $actual = [DateTimeOffset]::ParseExact($probe.StartTimeUtc, 'o', [Globalization.CultureInfo]::InvariantCulture) }
    catch { return 'Unknown' }
    if ($started.UtcTicks -eq $actual.UtcTicks) { return 'Running' }
    # The PID was reused: the recorded worker, specifically, has exited.
    return 'Exited'
}

function New-NvtDefinition([string]$TaskId, [int]$Minutes = 20, [string]$Directory, [bool]$Enabled = $true, [switch]$Legacy) {
    if ($Minutes -lt 1 -or $Minutes -gt 44640) { Stop-Nvt 2 'EveryMinutes must be 1..44640.' }
    $entry = Get-NvtEntry $TaskId
    $scriptDirectory = Split-Path $entry.Script -Parent
    if (-not $Directory) { $Directory = $scriptDirectory }
    $Directory = Assert-NvtPath $Directory -Directory
    # Current tick reads/writes beside itself; DataDir cannot redirect code or data.
    if ($Directory -ine $scriptDirectory) { Stop-Nvt 3 'DataDir must be the allowlisted tick directory.' }
    $hostPath = Get-NvtHostPath
    $consolePath = Get-NvtConsolePath
    $runner = Assert-NvtPath (Join-Path $PSScriptRoot 'nvt-sched-runner.ps1')
    $sid = Get-NvtSid
    $arguments = '--headless "' + $hostPath + '" -NoLogo -NoProfile -NonInteractive -File "' + $runner + '" -Id ' + $TaskId
    # Legacy definitions are only matched and omit the routing arguments.
    if (-not $Legacy) {
        $arguments += ' -TaskPath \NVT\'
        $arguments += ' -CommanderDir "' + ($script:CommanderDir -replace '(\\+)$', '$1$1') + '"'
    }
    $escape = { param($value) [Security.SecurityElement]::Escape([string]$value) }
    $registration = if ($Legacy) { '' } else {
        '<RegistrationInfo><Author>commander</Author><Description>' +
            (& $escape ($entry.Description -f $Minutes, $entry.Script)) + '</Description></RegistrationInfo>'
    }
    $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  $registration
  <Triggers>
    <TimeTrigger>
      <Repetition><Interval>PT${Minutes}M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
      <StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled>
    </TimeTrigger>
    <LogonTrigger><Enabled>true</Enabled><UserId>$sid</UserId></LogonTrigger>
  </Triggers>
  <Principals><Principal id="CurrentUser"><UserId>$sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand><Enabled>$($Enabled.ToString().ToLowerInvariant())</Enabled><Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><Priority>7</Priority>
  </Settings>
  <Actions Context="CurrentUser"><Exec><Command>$(& $escape $consolePath)</Command><Arguments>$(& $escape $arguments)</Arguments><WorkingDirectory>$(& $escape $Directory)</WorkingDirectory></Exec></Actions>
</Task>
"@
    return [pscustomobject]@{ TaskPath = if ($Legacy) { '\' } else { '\NVT\' }; TaskName = Get-NvtName $TaskId -Legacy:$Legacy; Xml = $xml }
}

function ConvertTo-NvtPreviousAction([xml]$Definition) {
    # Match the previous action exactly; retain every other allowlisted setting.
    $definition = [xml]$Definition.CloneNode($true)
    $hostPath = Get-NvtHostPath
    $prefix = '--headless "' + $hostPath + '" -NoLogo -NoProfile -NonInteractive '
    $arguments = [string]$definition.Task.Actions.Exec.Arguments
    if ([string]$definition.Task.Actions.Exec.Command -cne (Get-NvtConsolePath) -or
        -not $arguments.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Expected the headless action.' }
    $definition.Task.Actions.Exec.Command = $hostPath
    $definition.Task.Actions.Exec.Arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden ' + $arguments.Substring($prefix.Length)
    return ,$definition
}

function ConvertTo-NvtXml([string]$Text) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($Text), $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    } finally { $reader.Dispose() }
}

function ConvertTo-NvtAccountSid([string]$UserId) {
    ([Security.Principal.NTAccount]::new($UserId).Translate([Security.Principal.SecurityIdentifier])).Value
}

function ConvertTo-NvtNormalizedXml([xml]$Document) {
    $document = [xml]$Document.CloneNode($true)
    $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    # Only these observed omissions are filled. Missing containers, unknown
    # nodes, attributes and every non-default value still affect comparison.
    $defaults = [ordered]@{
        '/t:Task/t:Principals/t:Principal' = @{ RunLevel = 'LeastPrivilege' }
        '/t:Task/t:Settings' = [ordered]@{
            AllowHardTerminate = 'true'; AllowStartOnDemand = 'true'; Enabled = 'true'
            Hidden = 'false'; Priority = '7'; RunOnlyIfIdle = 'false'
            RunOnlyIfNetworkAvailable = 'false'; WakeToRun = 'false'
        }
        '/t:Task/t:Triggers/t:TimeTrigger | /t:Task/t:Triggers/t:LogonTrigger' = @{ Enabled = 'true' }
        '/t:Task/t:Triggers/*/t:Repetition' = @{ StopAtDurationEnd = 'false' }
    }
    foreach ($path in $defaults.Keys) {
        foreach ($parent in $document.SelectNodes($path, $namespace)) {
            foreach ($name in $defaults[$path].Keys) {
                if (-not $parent.SelectSingleNode("t:$name", $namespace)) {
                    $child = $document.CreateElement($name, $namespace.LookupNamespace('t'))
                    $child.InnerText = $defaults[$path][$name]
                    $null = $parent.AppendChild($child)
                }
            }
        }
    }
    foreach ($node in @($document.SelectNodes('/t:Task/t:Settings/t:UseUnifiedSchedulingEngine', $namespace))) {
        $null = $node.ParentNode.RemoveChild($node)
    }
    foreach ($node in $document.SelectNodes('/t:Task/t:Principals/t:Principal/t:UserId | /t:Task/t:Triggers/t:LogonTrigger/t:UserId', $namespace)) {
        # Literal SIDs are already identities; NTAccount.Translate resolves names.
        # Canonicalize both, and let any invalid/unresolvable identity fail closed.
        if ($node.SelectNodes('*').Count -ne 0) { throw 'UserId must be an identity string.' }
        $sid = if ($node.InnerText -match '^S-\d+-') { $node.InnerText } else { ConvertTo-NvtAccountSid $node.InnerText }
        $node.InnerText = [Security.Principal.SecurityIdentifier]::new($sid).Value
    }
    return ,$document
}

function Get-NvtXmlShape([xml]$Document) {
    # Compare all execution-bearing XML, including unexpected nodes/attributes.
    # Scheduler may materialize these harmless defaults when exporting XML.
    $defaults = @{
        'Task/Settings/IdleSettings/Duration' = 'PT10M'
        'Task/Settings/IdleSettings/WaitTimeout' = 'PT1H'
        'Task/Settings/DisallowStartOnRemoteAppSession' = 'false'
        'Task/Settings/Volatile' = 'false'
        'Task/Principals/Principal/ProcessTokenSidType' = 'Default'
    }
    $rows = [Collections.Generic.List[string]]::new()
    function Visit-NvtXml($node, [string]$path) {
        if ($path -eq 'Task/RegistrationInfo') { return }
        if ($defaults.ContainsKey($path) -and $node.InnerText -ceq $defaults[$path] -and $node.Attributes.Count -eq 0) { return }
        $rows.Add("$path|namespace=$($node.NamespaceURI)")
        foreach ($attribute in $node.Attributes) {
            if ($attribute.Name -eq 'xmlns' -or ($path -eq 'Task' -and $attribute.Name -eq 'version')) { continue }
            $rows.Add("$path|@$($attribute.Name)=$($attribute.Value)")
        }
        $children = @($node.ChildNodes | Where-Object NodeType -eq Element)
        if ($children.Count -eq 0) {
            $value = $node.InnerText
            if ($node.LocalName -in 'Interval', 'ExecutionTimeLimit') {
                $value = [Xml.XmlConvert]::ToTimeSpan($value).Ticks.ToString()
            }
            $rows.Add("$path|value=$value")
        }
        foreach ($child in $children) { Visit-NvtXml $child "$path/$($child.LocalName)" }
    }
    Visit-NvtXml $Document.DocumentElement 'Task'
    return (($rows | Sort-Object -CaseSensitive) -join "`n")
}

function Assert-NvtDefinition([string]$TaskId, [string]$Xml, [string]$ExpectedXml, [switch]$Legacy, [switch]$PreviousAction) {
    try {
        $document = ConvertTo-NvtNormalizedXml (ConvertTo-NvtXml $Xml)
        if (-not $ExpectedXml) {
            $minutes = [Xml.XmlConvert]::ToTimeSpan([string]$document.Task.Triggers.TimeTrigger.Repetition.Interval).TotalMinutes
            if ($minutes -ne [math]::Truncate($minutes) -or $minutes -lt 1 -or $minutes -gt 44640) { throw 'interval' }
            $enabled = [string]$document.Task.Settings.Enabled
            if ($enabled -cnotin 'true', 'false') { throw 'enabled' }
            $ExpectedXml = (New-NvtDefinition $TaskId ([int]$minutes) -Enabled ($enabled -ceq 'true') -Legacy:$Legacy).Xml
        }
        $expected = ConvertTo-NvtNormalizedXml (ConvertTo-NvtXml $ExpectedXml)
        if ($PreviousAction) { $expected = ConvertTo-NvtPreviousAction $expected }
        if (-not $Legacy) {
            foreach ($field in 'Author', 'Description') {
                if ([string]::IsNullOrWhiteSpace([string]$document.Task.RegistrationInfo.$field) -or
                    [string]$document.Task.RegistrationInfo.$field -cne [string]$expected.Task.RegistrationInfo.$field) { throw 'metadata' }
            }
        }
        if ((Get-NvtXmlShape $document) -cne (Get-NvtXmlShape $expected)) { throw 'different' }
        return $document
    } catch {
        if (-not $PreviousAction) {
            $outdated = $false
            try {
                $null = Assert-NvtDefinition $TaskId $Xml $ExpectedXml -Legacy:$Legacy -PreviousAction
                $outdated = $true
            } catch { }
            if ($outdated) {
                Stop-Nvt 4 'Task definition is outdated; use install or add to register the headless action.' -Outdated
            }
        }
        Stop-Nvt 4 'Task definition differs from the allowlisted definition; refusing to overwrite or execute it.'
    }
}

function Get-NvtTask([string]$TaskId, [switch]$Legacy) {
    $name = Get-NvtName $TaskId -Legacy:$Legacy
    $path = if ($Legacy) { '\' } else { '\NVT\' }
    # Enumerate then filter: an absent NVT folder must mean NotInstalled, not API failure.
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object { $_.TaskPath -eq $path -and $_.TaskName -eq $name })
    if ($tasks.Count -gt 1) { Stop-Nvt 4 'Ambiguous task name.' }
    if ($tasks.Count -eq 1) { return $tasks[0] }
    return $null
}

function Get-NvtOwnedDefinition([string]$TaskId, [switch]$Legacy) {
    $path = if ($Legacy) { '\' } else { '\NVT\' }
    $xml = Export-ScheduledTask -TaskName (Get-NvtName $TaskId -Legacy:$Legacy) -TaskPath $path -ErrorAction Stop
    try { Assert-NvtDefinition $TaskId $xml -Legacy:$Legacy }
    catch {
        # Explicit legacy migration/runner calls still recognize the original root action.
        if (-not $Legacy -or -not $_.Exception.Data['NvtDefinitionOutdated']) { throw }
        Assert-NvtDefinition $TaskId $xml -Legacy -PreviousAction
    }
}

function Test-NvtHeadlessTask($Task) {
    # Only the allowlisted task in its current folder has runner state to resolve.
    if ($Task.TaskPath -ine '\NVT\' -or $Task.TaskName -ine 'commander-tick') { return $false }
    $actions = @($Task.Actions)
    return ($actions.Count -eq 1 -and
        $actions[0].Execute -ieq (Join-Path ([Environment]::SystemDirectory) 'conhost.exe') -and
        $actions[0].Arguments -match '^--headless(?:\s|$)')
}

function Get-NvtRunResult($Task, $Info, $State, $Current = $null) {
    $unknown = [pscustomobject]@{ Result = 'Unknown'; ResultSource = 'Unknown' }
    if ($null -eq $Info) { return $unknown }
    # These are Scheduler facts, not child exit codes. They override retained state.
    if ($Info.LastTaskResult -in 267009, 267011 -or -not (Test-NvtHeadlessTask $Task)) {
        return [pscustomobject]@{ Result = $Info.LastTaskResult; ResultSource = 'Scheduler' }
    }
    # The action looks headless. Trust runner state only when the whole definition is the allowlisted one.
    # Callers that already compared the definition pass the outcome in Current to avoid a second export.
    if ($null -ne $Current) { if (-not $Current) { return $unknown } }
    else { try { $null = Get-NvtOwnedDefinition 'commander-tick' } catch { return $unknown } }
    if ($State -isnot [Collections.IDictionary] -or $State.Status -notin 'Succeeded', 'Failed' -or
        ($State.ExitCode -isnot [int] -and $State.ExitCode -isnot [long]) -or -not $Info.LastRunTime) { return $unknown }
    try {
        # Scheduler supplies local DateTime; runner timestamps carry their UTC offset.
        $lastRunUtc = ([datetime]$Info.LastRunTime).ToUniversalTime()
        if ($lastRunUtc.Year -le 1900) { return $unknown }
        $updated = if ($State.UpdatedAtUtc -is [datetime] -or $State.UpdatedAtUtc -is [DateTimeOffset]) {
            [DateTimeOffset]$State.UpdatedAtUtc
        } else {
            [DateTimeOffset]::ParseExact($State.UpdatedAtUtc, 'o', [Globalization.CultureInfo]::InvariantCulture)
        }
        # Five seconds allow for timestamp precision and small clock differences.
        if ($updated.UtcDateTime -lt $lastRunUtc.AddSeconds(-5)) { return $unknown }
    } catch { return $unknown }
    return [pscustomobject]@{ Result = $State.ExitCode; ResultSource = 'RunnerState' }
}

function Get-NvtList {
    $prefix = "NVT-$(Get-NvtSid)-"
    # The only supported allowlist ID is fixed by Get-NvtName/Get-NvtEntry.
    # Querying must still work if its script or task definition has changed.
    $managedName = Get-NvtName 'commander-tick'
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
        $_.TaskPath -eq '\NVT\' -or ($_.TaskPath -eq '\' -and $_.TaskName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))
    } | Sort-Object @{ Expression = { $_.TaskPath -eq '\' } }, TaskName)
    if (-not @($tasks | Where-Object { $_.TaskPath -eq '\NVT\' -and $_.TaskName -ieq $managedName }).Count) {
        $tasks = @([pscustomobject]@{ TaskPath = '\NVT\'; TaskName = $managedName; State = $null }) + $tasks
    }
    foreach ($task in $tasks) {
        $legacy = $task.TaskPath -eq '\'
        $managed = -not $legacy -and $task.TaskName -ieq $managedName
        $installed = $null -ne $task.State
        $info = $null
        $state = $null
        $history = @()
        $status = if (-not $managed) { 'Unmanaged' } elseif ($installed) { 'Installed' } else { 'NotInstalled' }
        if ($installed) { $info = Get-ScheduledTaskInfo -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop }
        if ($managed) {
            if ($installed) {
                try { $null = Get-NvtOwnedDefinition 'commander-tick' }
                catch {
                    if ($_.Exception.Data['NvtCode'] -ne 4) { throw }
                    $status = if ($_.Exception.Data['NvtDefinitionOutdated']) { 'DefinitionOutdated' } else { 'DefinitionMismatch' }
                }
            }
            $state = Read-NvtState 'commander-tick'
            $history = Read-NvtHistory 'commander-tick'
        }
        $runResult = Get-NvtRunResult $task $info $state ($managed -and $installed -and $status -notin 'DefinitionMismatch', 'DefinitionOutdated')
        [pscustomobject][ordered]@{
            TaskPath = $task.TaskPath
            TaskName = $task.TaskName
            Legacy = $legacy
            Status = $status
            State = if ($installed) { [string]$task.State } else { $null }
            Worker = if ($managed) { Get-NvtLiveness $state } else { $null }
            LastSuccessUtc = $state.LastSuccessUtc; ExitCode = $state.ExitCode
            LastTaskResult = $info.LastTaskResult; NextRunTime = $info.NextRunTime
            Result = $runResult.Result; ResultSource = $runResult.ResultSource
            History = $history
        }
    }
}

function Format-NvtList([object[]]$Tasks) {
    foreach ($task in $Tasks) {
        # Explicit formatting keeps nested history visible in terminals and pipes.
        ($task | Select-Object TaskPath, TaskName, Legacy, Status, State, Worker, LastSuccessUtc, ExitCode, Result, ResultSource, LastTaskResult, NextRunTime |
            Format-List | Out-String).TrimEnd()
        if ($null -eq $task.History) { 'History: Unknown (unreadable, invalid or busy)' }
        elseif ($task.History.Count -eq 0) { 'History: (none)' }
        else {
            'History (latest 5, newest first):'
            ($task.History | Format-Table StartTimeUtc, EndTimeUtc, ExitCode -AutoSize | Out-String -Width 160).TrimEnd()
        }
    }
}

function Remove-NvtTask([string]$TaskId, [switch]$Legacy) {
    $name = Get-NvtName $TaskId -Legacy:$Legacy
    $path = if ($Legacy) { '\' } else { '\NVT\' }
    $null = Get-NvtOwnedDefinition $TaskId -Legacy:$Legacy
    $null = Disable-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction Stop
    # The same lock closes the gap between task launch and recording its PID.
    # A late runner must check Enabled under this lock before executing tick.
    $handle = Open-NvtRunLock $TaskId
    try {
        $live = Get-NvtLiveness (Read-NvtState $TaskId)
        if ($live -eq 'Running') { Stop-Nvt 6 'Recorded worker is still running; task remains disabled.' }
        if ($live -ne 'Exited') { Stop-Nvt 5 'Worker state is unknown; task remains disabled.' }
        $task = Get-NvtTask $TaskId -Legacy:$Legacy
        if (-not $task -or [string]$task.State -notin 'Disabled', 'Ready') { Stop-Nvt 5 'Scheduler state is active or unknown; task remains disabled.' }
        $definition = Get-NvtOwnedDefinition $TaskId -Legacy:$Legacy
        if ([string]$definition.Task.Settings.Enabled -cne 'false') { Stop-Nvt 5 'Task disable could not be confirmed.' }
        Unregister-ScheduledTask -TaskName $name -TaskPath $path -Confirm:$false -ErrorAction Stop
    } finally { $handle.Dispose() }
    return "$path$name removed; state retained."
}

function Invoke-NvtCommand([string]$Operation, [string]$TaskId, [int]$Minutes = 20, [string]$Directory, [switch]$Preview, [switch]$Json) {
    if ($Operation -eq 'install') { $Operation = 'add' }
    if ($Operation -eq 'status') { $Operation = 'list' }
    if ($Operation -notin 'add', 'list', 'audit', 'run', 'remove') { Stop-Nvt 2 'Expected install, add, list, status, audit, run or remove.' }
    if ($Preview -and $Operation -ne 'add') { Stop-Nvt 2 'DryRun is supported only with install/add.' }
    if ($Json -and $Operation -ne 'list') { Stop-Nvt 2 'Json is supported only with list/status.' }
    if ($Operation -eq 'audit') { return (Invoke-NvtAudit).Path }
    if ($Operation -eq 'list') {
        $null = Get-NvtName $TaskId
        $result = @(Get-NvtList)
        if ($Json) { return ConvertTo-Json -InputObject $result -Depth 5 }
        return $result
    }
    $null = Get-NvtEntry $TaskId
    $name = Get-NvtName $TaskId
    if ($Operation -eq 'add') {
        $definition = New-NvtDefinition $TaskId $Minutes $Directory
        if ($Preview) { return "TaskPath: \NVT\`nTaskName: $name`n$($definition.Xml)" }
        $existing = Get-NvtTask $TaskId
        if ($existing) {
            try {
                $null = Assert-NvtDefinition $TaskId (Export-ScheduledTask -TaskName $name -TaskPath '\NVT\' -ErrorAction Stop) $definition.Xml
            } catch {
                if (-not $_.Exception.Data['NvtDefinitionOutdated']) { throw }
                $handle = Open-NvtRunLock $TaskId
                try {
                    # Read Scheduler state again under the lock; the earlier read can be stale.
                    $current = Get-NvtTask $TaskId
                    if ((Get-NvtLiveness (Read-NvtState $TaskId)) -ne 'Exited' -or -not $current -or [string]$current.State -cne 'Ready') {
                        Stop-Nvt 5 'Previous worker or Scheduler state is active or unknown; refusing to update.'
                    }
                    # Recheck under the runner lock before replacing this exact previous definition.
                    $null = Assert-NvtDefinition $TaskId (Export-ScheduledTask -TaskName $name -TaskPath '\NVT\' -ErrorAction Stop) $definition.Xml -PreviousAction
                    $null = Register-ScheduledTask -TaskName $name -TaskPath '\NVT\' -Xml $definition.Xml -Force -ErrorAction Stop
                } finally { $handle.Dispose() }
            }
        } else {
            # Preserve previous evidence and refuse to lose track of a still-live worker.
            $old = Read-NvtState $TaskId
            $statePath = Get-NvtStatePath $TaskId -Create
            if ((Test-Path -LiteralPath $statePath) -and (Get-NvtLiveness $old) -ne 'Exited') {
                Stop-Nvt 5 'Previous worker is running or unknown; refusing to register.'
            }
            if (-not (Test-Path -LiteralPath $statePath)) {
                Write-NvtJson $statePath ([ordered]@{ Status = 'NeverRun'; ProcessId = 0; StartTimeUtc = $null; UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); ExitCode = $null; LastSuccessUtc = $null })
            }
            $null = Register-ScheduledTask -TaskName $name -TaskPath '\NVT\' -Xml $definition.Xml -ErrorAction Stop
        }
        # Never touch legacy until the new task is visible and its exported definition matches.
        if (-not (Get-NvtTask $TaskId)) { Stop-Nvt 4 'New task could not be verified; legacy task retained.' }
        $null = Assert-NvtDefinition $TaskId (Export-ScheduledTask -TaskName $name -TaskPath '\NVT\' -ErrorAction Stop) $definition.Xml
        if (Get-NvtTask $TaskId -Legacy) {
            $null = Remove-NvtTask $TaskId -Legacy
            return "\NVT\$name verified; legacy removed; history retained."
        }
        return "\NVT\$name verified; installed with the same settings; history retained."
    }
    $task = Get-NvtTask $TaskId
    if (-not $task) { Stop-Nvt 8 'Task is not installed for the current SID.' }
    $definition = Get-NvtOwnedDefinition $TaskId
    switch ($Operation) {
        'run' {
            if ([string]$definition.Task.Settings.Enabled -cne 'true') { Stop-Nvt 5 'Task is disabled.' }
            Start-ScheduledTask -TaskName $name -TaskPath '\NVT\' -ErrorAction Stop
            return "$name submitted; use list to inspect completion."
        }
        'remove' { return Remove-NvtTask $TaskId }
    }
}

function Get-NvtAuditPaths([DateTimeOffset]$At, [switch]$Create) {
    $statePath = Get-NvtStatePath 'commander-tick' -Create:$Create
    if (-not $statePath) { return $null }
    $directory = Join-Path (Split-Path $statePath -Parent) 'audit'
    if (-not (Test-Path -LiteralPath $directory)) {
        if (-not $Create) { return $null }
        $null = New-Item -ItemType Directory -Path $directory -ErrorAction Stop
    }
    $null = Assert-NvtPath $directory -Directory
    $paths = @{ Latest = Join-Path $directory 'latest.json'; Report = Join-Path $directory ("audit-{0:yyyyMMdd}.md" -f $At) }
    foreach ($path in $paths.Values) {
        if (Test-Path -LiteralPath $path) { $null = Assert-NvtPath $path }
    }
    return $paths
}

function Read-NvtAuditSnapshot($Stream) {
    if ($Stream.Length -eq 0) { return $null }
    if ($Stream.Length -gt 4MB) { Stop-Nvt 9 'Audit snapshot exceeds its size limit.' }
    $reader = [IO.StreamReader]::new($Stream, [Text.Encoding]::UTF8, $true, 1024, $true)
    try { $value = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable -ErrorAction Stop }
    finally { $reader.Dispose() }
    if ($value -isnot [Collections.IDictionary] -or -not $value.ContainsKey('At') -or
        -not $value.ContainsKey('Tasks') -or $value.Tasks -isnot [array]) { Stop-Nvt 9 'Invalid audit snapshot; preserve it for inspection.' }
    $null = [DateTimeOffset]$value.At
    return $value
}

function Get-NvtAuditFlags($Task) {
    $result = if ($Task.ResultSource) { $Task.Result } else { $Task.LastTaskResult }
    if ($Task.ResultSource -eq 'Unknown' -or
        ($Task.ResultSource -eq 'RunnerState' -and $result -ne 0) -or
        ($null -ne $result -and $result -notin 0, 267009, 267011)) { '失敗' }
    if ($Task.LastTaskResult -eq 267011 -or -not $Task.LastRunTime -or ([datetime]$Task.LastRunTime).Year -le 1900) { '從沒跑過' }
    if ($Task.State -eq 'Disabled') { '已停用' }
    if ([string]::IsNullOrWhiteSpace($Task.Author)) { '沒有作者' }
    if ($Task.TaskPath.StartsWith('\NVT\', [StringComparison]::OrdinalIgnoreCase)) { '屬於我們的' }
}

function ConvertTo-NvtAuditText([string]$Value) {
    # Task metadata is plain text, never Markdown links/HTML or executable actions.
    if ([string]::IsNullOrWhiteSpace($Value)) { return '（無）' }
    return (($Value -replace '[\x00-\x1f\x7f]', ' ') -replace '([\\`*_{}\[\]()<>#!|])', '\$1')
}

function Invoke-NvtAudit([DateTimeOffset]$At = [DateTimeOffset]::Now) {
    $paths = Get-NvtAuditPaths $At -Create
    # latest.json doubles as the audit lock; no extra lock or baseline file.
    $stream = [IO.File]::Open($paths.Latest, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $previous = Read-NvtAuditSnapshot $stream
        $tasks = @(
            foreach ($task in (Get-ScheduledTask -ErrorAction Stop | Sort-Object TaskPath, TaskName)) {
                if ($task.TaskPath -eq '\Microsoft\' -or $task.TaskPath.StartsWith('\Microsoft\', [StringComparison]::OrdinalIgnoreCase)) { continue }
                $info = Get-ScheduledTaskInfo -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop
                $state = if (Test-NvtHeadlessTask $task) { Read-NvtState 'commander-tick' } else { $null }
                $runResult = Get-NvtRunResult $task $info $state
                [pscustomobject][ordered]@{
                    TaskPath = [string]$task.TaskPath; TaskName = [string]$task.TaskName; State = [string]$task.State
                    LastRunTime = if ($null -ne $info.LastRunTime) { ([datetime]$info.LastRunTime).ToString('o') } else { $null }
                    LastTaskResult = if ($null -ne $info.LastTaskResult) { [long]$info.LastTaskResult } else { $null }
                    Result = $runResult.Result; ResultSource = $runResult.ResultSource
                    NextRunTime = if ($null -ne $info.NextRunTime) { ([datetime]$info.NextRunTime).ToString('o') } else { $null }
                    Author = [string]$task.Author; Description = [string]$task.Description
                }
            }
        )
        $before = @($previous.Tasks | ForEach-Object { $_.TaskPath + $_.TaskName })
        $after = @($tasks | ForEach-Object { $_.TaskPath + $_.TaskName })
        # The first report establishes a baseline rather than claiming all tasks are new.
        $added = @(if ($previous) { $after | Where-Object { $_ -notin $before } })
        $removed = @(if ($previous) { $before | Where-Object { $_ -notin $after } })
        $attention = @($tasks | Where-Object { @(Get-NvtAuditFlags $_ | Where-Object { $_ -ne '屬於我們的' }).Count -gt 0 })
        $lines = [Collections.Generic.List[string]]::new()
        $lines.Add("本次共 $($tasks.Count) 個排程，需要注意 $($attention.Count) 個；新增 $($added.Count)、刪除 $($removed.Count)。停用由 owner 決定。")
        $lines.Add('')
        $lines.Add("產生時間：$($At.ToString('yyyy-MM-dd HH:mm zzz'))。只查排程，未停用或修改任何工作。")
        $lines.Add('')
        $lines.Add('## 需要注意')
        $lines.Add('')
        if (-not $previous) { $lines.Add('首次建立快照，下次開始比較新增與刪除。') }
        foreach ($pair in @(@('新增', $added), @('刪除', $removed))) {
            foreach ($name in $pair[1]) { $lines.Add("- $($pair[0])：$(ConvertTo-NvtAuditText $name)") }
        }
        foreach ($task in $attention) {
            $lines.Add("- $(ConvertTo-NvtAuditText ($task.TaskPath + $task.TaskName))：$((Get-NvtAuditFlags $task) -join '、')；結果 $($task.Result)；來源 $($task.ResultSource)。")
        }
        if ($previous -and $attention.Count -eq 0 -and $added.Count -eq 0 -and $removed.Count -eq 0) { $lines.Add('沒有新增、刪除或異常標記。') }
        $lines.Add('')
        $lines.Add('### 建議可停用（僅建議）')
        $lines.Add('')
        $suggested = 0
        foreach ($task in $tasks) {
            $flags = @(Get-NvtAuditFlags $task)
            if ('屬於我們的' -in $flags -or $task.TaskName.StartsWith("NVT-$(Get-NvtSid)-", [StringComparison]::OrdinalIgnoreCase) -or
                $task.State -ne 'Ready' -or '沒有作者' -notin $flags -or '從沒跑過' -notin $flags) { continue }
            $lines.Add("- $(ConvertTo-NvtAuditText ($task.TaskPath + $task.TaskName))：從沒跑過且沒有作者。請先確認用途；若已不使用相關軟體，可由 owner 停用。停用可能影響廠商更新或功能。")
            $suggested++
        }
        if ($suggested -eq 0) { $lines.Add('沒有足夠依據提出停用建議。') }
        foreach ($group in '我們的', '廠商的') {
            $lines.Add(''); $lines.Add("## $group"); $lines.Add('')
            $members = @($tasks | Where-Object { ('屬於我們的' -in @(Get-NvtAuditFlags $_)) -eq ($group -eq '我們的') })
            if ($members.Count -eq 0) { $lines.Add('（無）') }
            foreach ($task in $members) {
                $flags = @(Get-NvtAuditFlags $task)
                $lines.Add("- **$(ConvertTo-NvtAuditText ($task.TaskPath + $task.TaskName))**")
                $lines.Add("  狀態：$(ConvertTo-NvtAuditText $task.State)；標記：$(if ($flags.Count) { $flags -join '、' } else { '無' })。")
                $lines.Add("  上次：$(ConvertTo-NvtAuditText $task.LastRunTime)；結果：$($task.Result)；來源：$($task.ResultSource)；下次：$(ConvertTo-NvtAuditText $task.NextRunTime)。")
                $lines.Add("  作者：$(ConvertTo-NvtAuditText $task.Author)。說明：$(ConvertTo-NvtAuditText $task.Description)")
                if ($group -eq '我們的' -and $task.TaskName -eq 'commander-tick' -and $task.Result -eq 10) {
                    $lines.Add('  注意：依稽核規則，非零結果 10 標記失敗；此 tick 的 10 表示有新變化，runner 視為成功。')
                }
                $lines.Add('')
            }
        }
        [IO.File]::WriteAllText($paths.Report, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(([ordered]@{ At = $At.ToString('o'); Tasks = $tasks } | ConvertTo-Json -Depth 5))
        if ($bytes.Length -gt 4MB) { Stop-Nvt 9 'Audit snapshot exceeds its size limit.' }
        $stream.Position = 0
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.SetLength($bytes.Length)
        $stream.Flush($true)
        return [pscustomobject]@{ Path = $paths.Report }
    } finally { $stream.Dispose() }
}

function Get-NvtIsoWeek([DateTimeOffset]$At) {
    return '{0}-W{1:00}' -f [Globalization.ISOWeek]::GetYear($At.DateTime), [Globalization.ISOWeek]::GetWeekOfYear($At.DateTime)
}

function Invoke-NvtWeeklyAudit([DateTimeOffset]$At = [DateTimeOffset]::Now, [string]$AcknowledgedWeek) {
    $week = Get-NvtIsoWeek $At
    $monday = $At.Date.AddDays(-(([int]$At.DayOfWeek + 6) % 7)).AddHours(8.5)
    if ($At.DateTime -lt $monday -or $AcknowledgedWeek -eq $week) { return $null }
    $paths = Get-NvtAuditPaths $At
    $latest = $null
    if ($paths -and (Test-Path -LiteralPath $paths.Latest)) {
        $stream = [IO.File]::Open($paths.Latest, 'Open', 'Read', 'None')
        try { $latest = Read-NvtAuditSnapshot $stream }
        finally { $stream.Dispose() }
    }
    if ($latest -and (Get-NvtIsoWeek ([DateTimeOffset]$latest.At)) -eq $week) {
        $report = (Get-NvtAuditPaths ([DateTimeOffset]$latest.At)).Report
        if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { $latest = $null }
    } else { $latest = $null }
    if (-not $latest) { $report = (Invoke-NvtAudit $At).Path }
    # The caller logs before acknowledging the week, so a failed log is retried without re-auditing.
    return [pscustomobject]@{ Week = $week; Path = $report }
}

function Invoke-NvtRunner([string]$TaskId, [switch]$Legacy) {
    $handle = $null
    $state = $null
    $code = 1
    $started = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        $entry = Get-NvtEntry $TaskId
        $handle = Open-NvtRunLock $TaskId
        $definition = Get-NvtOwnedDefinition $TaskId -Legacy:$Legacy
        if ([string]$definition.Task.Settings.Enabled -cne 'true') { Stop-Nvt 5 'Task is disabled.' }
        $previous = Read-NvtState $TaskId
        if ((Get-NvtLiveness $previous) -ne 'Exited') { Stop-Nvt 5 'Previous worker is running or unknown.' }
        $state = [ordered]@{
            Status = 'Running'; ProcessId = $PID
            StartTimeUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
            UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            ExitCode = $null; LastSuccessUtc = $previous.LastSuccessUtc
        }
        $statePath = Get-NvtStatePath $TaskId -Create
        Write-NvtJson $statePath $state
        # The approved tick launches no child processes: actual work runs in
        # this PID. Discard every stream; never store source data or raw errors.
        $global:LASTEXITCODE = 0
        & $entry.Script *> $null
        $code = [int]$LASTEXITCODE
        $state.Status = if ($code -in $entry.SuccessExitCodes) { 'Succeeded' } else { 'Failed' }
        if ($state.Status -eq 'Succeeded') { $state.LastSuccessUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    } catch {
        $code = 1
        if ($null -ne $state) { $state.Status = 'Failed' }
        $errorCode = if ($_.Exception.Data.Contains('NvtCode')) { [int]$_.Exception.Data['NvtCode'] } else { 9 }
        try { Write-NvtError $TaskId 'runner' $errorCode } catch { }
    } finally {
        # Settle the history outcome first, so the published state never shows success for a failed run.
        try { Add-NvtHistory $TaskId $started ([DateTimeOffset]::UtcNow.ToString('o')) $code }
        catch {
            $code = 1
            try { Write-NvtError $TaskId 'runner' 9 } catch { }
        }
        if ($null -ne $state) {
            if ($code -ne 0 -and $state.Status -eq 'Succeeded' -and $code -notin $entry.SuccessExitCodes) {
                $state.Status = 'Failed'; $state.LastSuccessUtc = $previous.LastSuccessUtc
            }
            $state.ExitCode = $code
            $state.UpdatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
            # A failed state write leaves older state, which queries report as Unknown.
            try { Write-NvtJson $statePath $state } catch { $code = 1 }
        }
        if ($null -ne $handle) { $handle.Dispose() }
    }
    return $code
}

function Invoke-NvtCli {
    # Keep presentation/exit handling callable with offline mocks as well.
    [CmdletBinding()]
    param([string]$Command, [string]$Id = 'commander-tick', [int]$EveryMinutes = 20,
        [string]$DataDir, [string]$CommanderDir = $script:CommanderDir, [switch]$DryRun, [switch]$Json)
    $ErrorActionPreference = 'Stop'
    try {
        $script:CommanderDir = $CommanderDir
        if (-not $CommanderDir) { Stop-Nvt 2 'Set COMMANDER_DIR or pass -CommanderDir (commander folder).' }
        if ($Command -notin 'add', 'install' -and ($PSBoundParameters.ContainsKey('EveryMinutes') -or $PSBoundParameters.ContainsKey('DataDir'))) {
            Stop-Nvt 2 'EveryMinutes and DataDir are supported only with install/add.'
        }
        $result = Invoke-NvtCommand $Command $Id $EveryMinutes $DataDir -Preview:$DryRun -Json:$Json
        if ($Command -in 'list', 'status' -and -not $Json) { Format-NvtList @($result) }
        else { $result }
        exit 0
    } catch {
        $code = 9
        $message = 'Local scheduler or file access failed; inspect permissions and installation.'
        if ($_.Exception.Data.Contains('NvtCode')) { $code = [int]$_.Exception.Data['NvtCode']; $message = $_.Exception.Message }
        if (-not $DryRun -and $Command -in 'add', 'install', 'run', 'remove') {
            try { Write-NvtError $Id $Command $code } catch { $message += ' Error status could not be saved.' }
        }
        [Console]::Error.WriteLine("nvt-sched: [$code] $message")
        exit $code
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
Invoke-NvtCli @PSBoundParameters
