#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$ExecutablePath,

    [string]$OutputPath,

    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8,

    [ValidateRange(5, 120)]
    [int]$ProcessTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AnsiPattern = [regex]::new(
    ([string][char]27 + '\[[0-?]*[ -/]*[@-~]'),
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
)

$ShortcutTargets = [ordered]@{
    modules = 'xbdm modules'
    module = 'xbdm modules'
    mem = 'xbdm mem'
    xex = 'xbdm xex'
    fs = 'xbdm fs'
    threads = 'xbdm threads'
    debug = 'xbdm debug'
}

function Normalize-HelpText {
    param([AllowEmptyString()][string]$Text)

    $normalized = $AnsiPattern.Replace($Text, '').Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = foreach ($line in $normalized.Split("`n")) {
        $line.TrimEnd([char[]]@(' ', "`t"))
    }

    return ($lines -join "`n").Trim([char[]]@("`n"))
}

function Get-PathKey {
    param([string[]]$PathParts)

    return $PathParts -join ' '
}

function Get-DisplayPath {
    param([string[]]$PathParts)

    if ($PathParts.Count -eq 0) {
        return 'rgh'
    }

    return 'rgh ' + (Get-PathKey -PathParts $PathParts)
}

function Start-HelpInvocation {
    param(
        [int]$Index,
        [string[]]$PathParts,
        [string]$Executable,
        [string]$TemporaryHome,
        [int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $TemporaryHome
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.Environment['XECLI_HOME'] = $TemporaryHome
    $startInfo.Environment['XECLI_LANG'] = 'en'
    $startInfo.Environment['NO_COLOR'] = '1'
    $startInfo.Environment['TERM'] = 'dumb'
    $startInfo.Environment['DOTNET_CLI_UI_LANGUAGE'] = 'en-US'

    $startInfo.ArgumentList.Add('--language')
    $startInfo.ArgumentList.Add('en')
    foreach ($part in $PathParts) {
        $startInfo.ArgumentList.Add($part)
    }
    $startInfo.ArgumentList.Add('--help')

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        [void]$process.Start()
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $exitTask = $process.WaitForExitAsync()

        return [pscustomobject]@{
            Index = $Index
            PathParts = [string[]]@($PathParts)
            Process = $process
            StandardOutputTask = $standardOutputTask
            StandardErrorTask = $standardErrorTask
            ExitTask = $exitTask
            TimeoutTask = [System.Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds($TimeoutSeconds))
        }
    }
    catch {
        $process.Dispose()
        throw
    }
}

function Complete-HelpInvocation {
    param([pscustomobject]$Invocation)

    [void]$Invocation.ExitTask.GetAwaiter().GetResult()
    $standardOutput = $Invocation.StandardOutputTask.GetAwaiter().GetResult()
    $standardError = $Invocation.StandardErrorTask.GetAwaiter().GetResult()
    $exitCode = $Invocation.Process.ExitCode
    $displayPath = Get-DisplayPath -PathParts $Invocation.PathParts
    $Invocation.Process.Dispose()

    $normalizedOutput = Normalize-HelpText -Text $standardOutput
    $normalizedError = Normalize-HelpText -Text $standardError

    if ($exitCode -ne 0) {
        $details = if ([string]::IsNullOrWhiteSpace($normalizedError)) { 'No error output was returned.' } else { $normalizedError }
        throw "$displayPath --help exited with code $exitCode. $details"
    }

    if (-not [string]::IsNullOrWhiteSpace($normalizedError)) {
        throw "$displayPath --help wrote unexpected error output: $normalizedError"
    }

    if ([string]::IsNullOrWhiteSpace($normalizedOutput)) {
        throw "$displayPath --help returned no output."
    }

    return [pscustomobject]@{
        Index = $Invocation.Index
        PathParts = [string[]]@($Invocation.PathParts)
        Key = Get-PathKey -PathParts $Invocation.PathParts
        Text = $normalizedOutput
    }
}

function Invoke-HelpBatch {
    param(
        [object[]]$Requests,
        [string]$Executable,
        [string]$TemporaryHome,
        [int]$MaximumConcurrency,
        [int]$TimeoutSeconds
    )

    $pending = [System.Collections.Generic.Queue[object]]::new()
    foreach ($request in $Requests) {
        $pending.Enqueue($request)
    }

    $running = [System.Collections.Generic.List[object]]::new()
    $results = [System.Collections.Generic.List[object]]::new()

    try {
        while ($pending.Count -gt 0 -or $running.Count -gt 0) {
            while ($pending.Count -gt 0 -and $running.Count -lt $MaximumConcurrency) {
                $request = $pending.Dequeue()
                $invocation = Start-HelpInvocation `
                    -Index $request.Index `
                    -PathParts $request.PathParts `
                    -Executable $Executable `
                    -TemporaryHome $TemporaryHome `
                    -TimeoutSeconds $TimeoutSeconds
                $running.Add($invocation)
            }

            if ($running.Count -eq 0) {
                continue
            }

            $waitTasks = [System.Collections.Generic.List[System.Threading.Tasks.Task]]::new()
            foreach ($invocation in $running) {
                $waitTasks.Add($invocation.ExitTask)
                $waitTasks.Add($invocation.TimeoutTask)
            }

            [void][System.Threading.Tasks.Task]::WhenAny($waitTasks.ToArray()).GetAwaiter().GetResult()

            $timedOut = @($running | Where-Object { $_.TimeoutTask.IsCompleted -and -not $_.ExitTask.IsCompleted })
            if ($timedOut.Count -gt 0) {
                $displayPath = Get-DisplayPath -PathParts $timedOut[0].PathParts
                try {
                    $timedOut[0].Process.Kill($true)
                }
                catch {
                }
                throw "$displayPath --help exceeded the $TimeoutSeconds-second timeout."
            }

            $completed = @($running | Where-Object { $_.ExitTask.IsCompleted })
            foreach ($invocation in $completed) {
                $result = Complete-HelpInvocation -Invocation $invocation
                $results.Add($result)
                [void]$running.Remove($invocation)
            }
        }
    }
    finally {
        foreach ($invocation in @($running)) {
            try {
                if (-not $invocation.Process.HasExited) {
                    $invocation.Process.Kill($true)
                }
            }
            catch {
            }
            finally {
                $invocation.Process.Dispose()
            }
        }
    }

    return @($results | Sort-Object Index)
}

function Get-ChildCommands {
    param([string]$HelpText)

    $commands = [System.Collections.Generic.List[object]]::new()
    $seenNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $inCommands = $false
    $current = $null

    foreach ($line in $HelpText.Split("`n")) {
        if (-not $inCommands) {
            if ($line -eq 'COMMANDS:') {
                $inCommands = $true
            }
            continue
        }

        if ($line -match '^[A-Z][A-Z ]+:$') {
            break
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line -match '^ {4}(?<name>[^\s<\[]+)(?:\s+(?:<[^>]+>|\[[^\]]+\]))*\s{2,}(?<description>\S.*)$') {
            $name = $Matches.name
            if (-not $seenNames.Add($name)) {
                throw "Duplicate command '$name' was found in one help table."
            }

            $current = [pscustomobject]@{
                Name = $name
                Description = $Matches.description.Trim()
            }
            $commands.Add($current)
            continue
        }

        if ($null -ne $current -and $line -match '^\s{8,}(?<continuation>\S.*)$') {
            $current.Description = ($current.Description + ' ' + $Matches.continuation.Trim()).Trim()
            continue
        }

        throw "Unable to parse command help row: $line"
    }

    return $commands.ToArray()
}

function Escape-MarkdownTableCell {
    param([string]$Value)

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-GeneratedHelpSections {
    param([string]$Markdown)

    $sections = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $lines = $Markdown.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n")

    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -notmatch '^#{3,6} `(?<path>rgh .+)`$') {
            continue
        }

        $path = $Matches.path
        if ($index + 2 -ge $lines.Length -or $lines[$index + 1] -ne '' -or $lines[$index + 2] -ne '```text') {
            throw "Generated section '$path' does not begin with a text code fence."
        }

        $bodyLines = [System.Collections.Generic.List[string]]::new()
        $cursor = $index + 3
        while ($cursor -lt $lines.Length -and $lines[$cursor] -ne '```') {
            $bodyLines.Add($lines[$cursor])
            $cursor++
        }

        if ($cursor -ge $lines.Length) {
            throw "Generated section '$path' has no closing code fence."
        }

        if (-not $sections.TryAdd($path, ($bodyLines -join "`n"))) {
            throw "Generated section '$path' appears more than once."
        }

        $index = $cursor
    }

    return $sections
}

function Assert-DocumentQuality {
    param([string]$Markdown)

    if ($AnsiPattern.IsMatch($Markdown)) {
        throw 'Generated Markdown contains ANSI control sequences.'
    }

    $lines = $Markdown.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n")
    foreach ($line in $lines) {
        if ($line -match '[ \t]+$') {
            throw 'Generated Markdown contains trailing whitespace.'
        }

        if ($line -match '(?i)\brgh\s+xtaf\s+format\b' -and
            $line -match '(?i)(--disk\b|PHYSICALDRIVE)' -and
            $line -match '(?i)--auto-confirm\b') {
            throw 'Generated Markdown contains an unsafe auto-confirmed physical format example.'
        }
    }

    $insideFence = $false
    foreach ($line in $lines) {
        if ($line -match '^```') {
            $insideFence = -not $insideFence
        }
    }
    if ($insideFence) {
        throw 'Generated Markdown contains an unbalanced code fence.'
    }

    if ([regex]::IsMatch($Markdown, '--path\s+(?!")[A-Za-z]:\\Program Files\\', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        throw 'Generated Markdown contains an unquoted IDA installation path.'
    }

    $disallowedTerms = [ordered]@{
        'attribution marker' = [string]::Concat('Co', 'dex')
        'vendor attribution marker' = [string]::Concat('Open', 'A', 'I')
        'generic attribution marker' = [string]::Concat('A', 'I')
        'technology attribution phrase' = [string]::Concat('artificial', ' intelligence')
        'internal verification phrase' = [string]::Concat('local', ' test')
        'machine-role wording' = [string]::Concat('work', 'station')
    }

    foreach ($entry in $disallowedTerms.GetEnumerator()) {
        $suffix = if ($entry.Key -eq 'internal verification phrase') { 's?' } else { '' }
        $pattern = '(?i)\b' + [regex]::Escape($entry.Value) + $suffix + '\b'
        if ([regex]::IsMatch($Markdown, $pattern)) {
            throw "Generated Markdown contains $($entry.Key)."
        }
    }

    $forbiddenPatterns = [ordered]@{
        'machine-specific user path' = '(?i)\b[A-Z]:\\Users\\[^\\\s]+\\'
        'candidate or validation path' = '(?i)\b[A-Z]:\\XeCLI-(?:public-candidate|validation|repo)\b'
    }

    foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
        if ([regex]::IsMatch($Markdown, $entry.Value)) {
            throw "Generated Markdown contains $($entry.Key)."
        }
    }
}

$executableItem = Get-Item -LiteralPath $ExecutablePath
if ($executableItem.PSIsContainer) {
    throw "ExecutablePath must identify a file: $ExecutablePath"
}
$resolvedExecutable = $executableItem.FullName

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot '..\wiki\CLI-Help.md'
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "The output directory does not exist: $outputDirectory"
}

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
)
$temporaryHome = Join-Path $temporaryBase ('xecli-cli-help-' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($temporaryHome)

try {
    $rootResult = Invoke-HelpBatch `
        -Requests @([pscustomobject]@{ Index = 0; PathParts = [string[]]@() }) `
        -Executable $resolvedExecutable `
        -TemporaryHome $temporaryHome `
        -MaximumConcurrency 1 `
        -TimeoutSeconds $ProcessTimeoutSeconds
    $rootHelp = $rootResult[0].Text
    $rootCommands = @(Get-ChildCommands -HelpText $rootHelp)
    if ($rootCommands.Count -eq 0) {
        throw 'Root help did not expose any commands.'
    }

    foreach ($shortcut in $ShortcutTargets.Keys) {
        if ($shortcut -notin $rootCommands.Name) {
            throw "Expected shortcut root '$shortcut' is missing from root help."
        }
    }

    $helpByPath = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $childrenByPath = [System.Collections.Generic.Dictionary[string, object[]]]::new([System.StringComparer]::Ordinal)
    $knownPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $frontier = [System.Collections.Generic.List[object]]::new()
    foreach ($rootCommand in $rootCommands) {
        if ($ShortcutTargets.Contains($rootCommand.Name)) {
            continue
        }

        $parts = [string[]]@($rootCommand.Name)
        $key = Get-PathKey -PathParts $parts
        [void]$knownPaths.Add($key)
        $frontier.Add([pscustomobject]@{ PathParts = $parts })
    }

    while ($frontier.Count -gt 0) {
        $requests = [System.Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $frontier.Count; $index++) {
            $requests.Add([pscustomobject]@{
                Index = $index
                PathParts = [string[]]@($frontier[$index].PathParts)
            })
        }

        $batch = Invoke-HelpBatch `
            -Requests $requests.ToArray() `
            -Executable $resolvedExecutable `
            -TemporaryHome $temporaryHome `
            -MaximumConcurrency $ThrottleLimit `
            -TimeoutSeconds $ProcessTimeoutSeconds

        $nextFrontier = [System.Collections.Generic.List[object]]::new()
        foreach ($result in $batch) {
            if (-not $helpByPath.TryAdd($result.Key, $result.Text)) {
                throw "Help path '$($result.Key)' was discovered more than once."
            }

            $children = @(Get-ChildCommands -HelpText $result.Text)
            $childrenByPath.Add($result.Key, [object[]]$children)

            foreach ($child in $children) {
                $childParts = [string[]](@($result.PathParts) + $child.Name)
                $childKey = Get-PathKey -PathParts $childParts
                if (-not $knownPaths.Add($childKey)) {
                    throw "Command path '$childKey' was discovered more than once."
                }

                $nextFrontier.Add([pscustomobject]@{ PathParts = $childParts })
            }
        }

        if ($knownPaths.Count -gt 1000) {
            throw 'Command discovery exceeded the 1,000-path safety limit.'
        }

        $frontier = $nextFrontier
    }

    $referenceOrder = [System.Collections.Generic.List[object]]::new()
    function Add-CommandTree {
        param([string[]]$PathParts)

        $key = Get-PathKey -PathParts $PathParts
        $referenceOrder.Add([pscustomobject]@{
            Key = $key
            PathParts = [string[]]@($PathParts)
            Text = $helpByPath[$key]
        })

        foreach ($child in $childrenByPath[$key]) {
            Add-CommandTree -PathParts ([string[]](@($PathParts) + $child.Name))
        }
    }

    foreach ($rootCommand in $rootCommands) {
        if ($ShortcutTargets.Contains($rootCommand.Name)) {
            continue
        }
        Add-CommandTree -PathParts ([string[]]@($rootCommand.Name))
    }

    if ($referenceOrder.Count -ne $helpByPath.Count) {
        throw 'Canonical command ordering did not cover every discovered help path.'
    }

    $builder = [System.Text.StringBuilder]::new()
    function Add-MarkdownLine {
        param([AllowEmptyString()][string]$Line = '')

        [void]$builder.Append($Line).Append("`n")
    }

    Add-MarkdownLine '# CLI Help'
    Add-MarkdownLine
    Add-MarkdownLine 'XeCLI v2.0.0 uses the `rgh` command. This page is generated from the Release executable and lists every canonical command path without duplicating shortcut trees.'
    Add-MarkdownLine
    Add-MarkdownLine '## Usage'
    Add-MarkdownLine
    Add-MarkdownLine 'Use `--help` at any level of the command tree:'
    Add-MarkdownLine
    Add-MarkdownLine '```powershell'
    Add-MarkdownLine 'rgh --help'
    Add-MarkdownLine 'rgh <command> --help'
    Add-MarkdownLine 'rgh <group> <command> --help'
    Add-MarkdownLine '```'
    Add-MarkdownLine
    Add-MarkdownLine 'Commands that connect to a console use the current target. Start with `rgh start`, `rgh connect`, or `rgh target --help` when a target has not been configured.'
    Add-MarkdownLine
    Add-MarkdownLine '## Root commands'
    Add-MarkdownLine
    Add-MarkdownLine '| Command | Description |'
    Add-MarkdownLine '| --- | --- |'
    foreach ($rootCommand in $rootCommands) {
        $description = Escape-MarkdownTableCell -Value $rootCommand.Description
        Add-MarkdownLine "| ``rgh $($rootCommand.Name)`` | $description |"
    }
    Add-MarkdownLine
    Add-MarkdownLine '## Shortcut map'
    Add-MarkdownLine
    Add-MarkdownLine 'These roots remain supported. Their command trees are documented once under the canonical XBDM paths.'
    Add-MarkdownLine
    Add-MarkdownLine '| Shortcut | Canonical path |'
    Add-MarkdownLine '| --- | --- |'
    foreach ($entry in $ShortcutTargets.GetEnumerator()) {
        Add-MarkdownLine "| ``rgh $($entry.Key)`` | ``rgh $($entry.Value)`` |"
    }
    Add-MarkdownLine
    Add-MarkdownLine '## Canonical command reference'
    Add-MarkdownLine
    Add-MarkdownLine 'Each section below is captured from the matching `--help` invocation with terminal color removed.'
    Add-MarkdownLine

    foreach ($record in $referenceOrder) {
        $headingLevel = [Math]::Min(6, $record.PathParts.Count + 2)
        $heading = '#' * $headingLevel
        Add-MarkdownLine "$heading ``rgh $($record.Key)``"
        Add-MarkdownLine
        Add-MarkdownLine '```text'
        foreach ($line in $record.Text.Split("`n")) {
            Add-MarkdownLine $line
        }
        Add-MarkdownLine '```'
        Add-MarkdownLine
    }

    $markdown = $builder.ToString().TrimEnd([char[]]@("`r", "`n")) + "`n"
    Assert-DocumentQuality -Markdown $markdown

    $generatedSections = Get-GeneratedHelpSections -Markdown $markdown
    if ($generatedSections.Count -ne $referenceOrder.Count) {
        throw "Generated Markdown has $($generatedSections.Count) help sections; expected $($referenceOrder.Count)."
    }

    foreach ($record in $referenceOrder) {
        $displayPath = "rgh $($record.Key)"
        if (-not $generatedSections.ContainsKey($displayPath)) {
            throw "Generated Markdown is missing section '$displayPath'."
        }
        if ($generatedSections[$displayPath] -cne $record.Text) {
            throw "Generated Markdown does not preserve the captured help for '$displayPath'."
        }
    }

    $validationRequests = [System.Collections.Generic.List[object]]::new()
    $validationRequests.Add([pscustomobject]@{ Index = 0; PathParts = [string[]]@() })
    for ($index = 0; $index -lt $referenceOrder.Count; $index++) {
        $validationRequests.Add([pscustomobject]@{
            Index = $index + 1
            PathParts = [string[]]@($referenceOrder[$index].PathParts)
        })
    }

    $validationResults = Invoke-HelpBatch `
        -Requests $validationRequests.ToArray() `
        -Executable $resolvedExecutable `
        -TemporaryHome $temporaryHome `
        -MaximumConcurrency $ThrottleLimit `
        -TimeoutSeconds $ProcessTimeoutSeconds

    if ($validationResults[0].Text -cne $rootHelp) {
        throw 'Root help changed between discovery and validation.'
    }

    $validatedRootCommands = @(Get-ChildCommands -HelpText $validationResults[0].Text)
    if (($validatedRootCommands | ConvertTo-Json -Compress) -cne ($rootCommands | ConvertTo-Json -Compress)) {
        throw 'The generated root overview does not match the validated root help.'
    }

    for ($index = 1; $index -lt $validationResults.Count; $index++) {
        $validated = $validationResults[$index]
        $displayPath = "rgh $($validated.Key)"
        if (-not $generatedSections.ContainsKey($displayPath)) {
            throw "Validated help has no generated section for '$displayPath'."
        }
        if ($generatedSections[$displayPath] -cne $validated.Text) {
            throw "Generated section '$displayPath' does not match its live --help output."
        }
    }

    [System.IO.File]::WriteAllText($resolvedOutput, $markdown, [System.Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        Output = $resolvedOutput
        RootCommands = $rootCommands.Count
        CanonicalSections = $referenceOrder.Count
        DiscoveryInvocations = 1 + $referenceOrder.Count
        ValidationInvocations = $validationResults.Count
        TotalInvocations = 1 + $referenceOrder.Count + $validationResults.Count
        Bytes = [System.Text.Encoding]::UTF8.GetByteCount($markdown)
    }
}
finally {
    $resolvedTemporaryHome = [System.IO.Path]::GetFullPath($temporaryHome)
    $expectedPrefix = $temporaryBase + [System.IO.Path]::DirectorySeparatorChar
    $safeName = [System.IO.Path]::GetFileName($resolvedTemporaryHome).StartsWith('xecli-cli-help-', [System.StringComparison]::Ordinal)
    $insideTemporaryBase = $resolvedTemporaryHome.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)

    if ($insideTemporaryBase -and $safeName -and [System.IO.Directory]::Exists($resolvedTemporaryHome)) {
        [System.IO.Directory]::Delete($resolvedTemporaryHome, $true)
    }
}
