param(
    [string] $BaseValue,
    [string] $Mode = 'acp',
    [string] $LoginValue
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($Mode -eq 'acp') {
    while ($null -ne ($line = [Console]::ReadLine())) {
        $request = $line | ConvertFrom-Json
        if ($request.method -eq 'initialize') {
            $response = @{
                jsonrpc = '2.0'
                id = $request.id
                result = @{
                    protocolVersion = 1
                    agentInfo = @{ name = 'terminal-auth-fixture'; version = '1.0' }
                    agentCapabilities = @{}
                    authMethods = @(@{
                        id = 'fixture-login'
                        name = 'Fixture login'
                        type = 'terminal'
                        args = @('-Mode', $env:SALMONEGG_AUTH_FIXTURE_MODE, '-LoginValue', 'login argument & literal')
                        env = @{ SALMONEGG_AUTH_FIXTURE_VALUE = 'method override' }
                    })
                }
            }
            [Console]::WriteLine(($response | ConvertTo-Json -Depth 10 -Compress))
        }
    }
    exit 0
}

# This must be a real console. Redirected pipes cannot satisfy terminal authentication.
if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
    exit 91
}

$descendantId = $null
if ($Mode -eq 'cancel') {
    $childStart = [Diagnostics.ProcessStartInfo]::new()
    $childStart.FileName = (Get-Process -Id $PID).Path
    $childStart.Arguments = '-NoLogo -NoProfile -Command "while ($true) { Start-Sleep -Seconds 1 }"'
    $childStart.UseShellExecute = $false
    $child = [Diagnostics.Process]::Start($childStart)
    $descendantId = $child.Id
    $child.Dispose()
}

$observation = @{
    processId = $PID
    descendantId = $descendantId
    baseValue = $BaseValue
    loginValue = $LoginValue
    environmentValue = $env:SALMONEGG_AUTH_FIXTURE_VALUE
    workingDirectory = [Environment]::CurrentDirectory
    inputRedirected = [Console]::IsInputRedirected
    outputRedirected = [Console]::IsOutputRedirected
}
[IO.File]::WriteAllText($env:SALMONEGG_AUTH_FIXTURE_OBSERVATION, ($observation | ConvertTo-Json -Compress))
[Console]::WriteLine('TERMINAL_AUTH_READY')

if ($Mode -eq 'cancel') {
    # Keep producing output while teardown closes ConPTY. The client must drain it to avoid
    # ClosePseudoConsole deadlocking, and closing the job must reclaim the descendant above.
    while ($true) {
        [Console]::WriteLine(('output while cancellation drains ' * 30))
        Start-Sleep -Milliseconds 20
    }
}

if ([Console]::ReadLine() -ne 'user confirmed') {
    exit 92
}
[Console]::WriteLine('TERMINAL_AUTH_ACCEPTED')
if ($Mode -eq 'failure') {
    exit 23
}
exit 0
