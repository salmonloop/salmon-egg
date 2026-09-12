param(
    [Parameter(Mandatory = $true)] [string] $StateDirectory,
    [ValidateSet('acp', 'login')] [string] $Mode = 'acp'
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$requestLog = Join-Path $StateDirectory 'requests.jsonl'
$signedIn = Join-Path $StateDirectory 'signed-in'
$scenario = Join-Path $StateDirectory 'scenario.txt'

function Write-Observation($value) {
    [IO.File]::AppendAllText($requestLog, ($value | ConvertTo-Json -Depth 10 -Compress) + [Environment]::NewLine)
}

if ($Mode -eq 'login') {
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { exit 91 }
    $behavior = [IO.File]::ReadAllText($scenario).Trim()
    $child = $null
    if ($behavior -eq 'cancel') {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = (Get-Process -Id $PID).Path
        $start.Arguments = '-NoLogo -NoProfile -Command "while ($true) { Start-Sleep -Seconds 1 }"'
        $start.UseShellExecute = $false
        $child = [Diagnostics.Process]::Start($start)
    }
    $observed = @{ processId = $PID; descendantId = $(if ($child) { $child.Id } else { $null });
        inputRedirected = [Console]::IsInputRedirected; outputRedirected = [Console]::IsOutputRedirected;
        methodEnvironment = $env:SALMONEGG_TERMINAL_GUI_METHOD }
    [IO.File]::WriteAllText((Join-Path $StateDirectory 'login.json'), ($observed | ConvertTo-Json -Compress))
    [Console]::WriteLine('PACKAGED_TERMINAL_READY')
    if ($behavior -eq 'cancel') {
        while ($true) { [Console]::WriteLine('terminal cancellation output'); Start-Sleep -Milliseconds 100 }
    }
    if ([Console]::ReadLine() -ne 'user confirmed') { exit 92 }
    [Console]::WriteLine('PACKAGED_TERMINAL_CONFIRMED')
    if ($behavior -eq 'failure') { exit 23 }
    [IO.File]::WriteAllText($signedIn, 'normal-zero-exit')
    exit 0
}

while ($null -ne ($line = [Console]::ReadLine())) {
    $request = $line | ConvertFrom-Json
    $method = [string]$request.method
    Write-Observation @{ method = $method; processId = $PID; authenticated = (Test-Path $signedIn) }
    $response = @{ jsonrpc = '2.0'; id = $request.id }
    switch ($method) {
        'initialize' {
            Write-Observation @{ terminalAdvertised = ($request.params.clientCapabilities.auth.terminal -eq $true); processId = $PID }
            $response.result = @{ protocolVersion = 1; agentInfo = @{ name = 'packaged-terminal-fixture'; version = '1' };
                agentCapabilities = @{ loadSession = $true; sessionCapabilities = @{ list = @{} } };
                authMethods = @(@{ id = 'packaged-login'; type = 'terminal'; name = 'Native sign-in';
                    args = @('-Mode', 'login'); env = @{ SALMONEGG_TERMINAL_GUI_METHOD = 'method-overlay' } }) }
        }
        'session/list' { $response.result = @{ sessions = @() } }
        'session/load' { $response.result = @{ sessionId = 'packaged-terminal-session' } }
        'session/new' { $response.result = @{ sessionId = 'packaged-terminal-session' } }
        'session/prompt' {
            if (-not (Test-Path $signedIn)) {
                $response.error = @{ code = -32000; message = 'Sign in before sending a prompt.' }
            }
            else {
                $text = 'PACKAGED_AUTH_REPLY'
                $notification = @{ jsonrpc = '2.0'; method = 'session/update'; params = @{
                    sessionId = 'packaged-terminal-session'; update = @{ sessionUpdate = 'agent_message_chunk'; content = @{ type = 'text'; text = $text } } } }
                [Console]::WriteLine(($notification | ConvertTo-Json -Depth 12 -Compress))
                $response.result = @{ stopReason = 'end_turn' }
            }
        }
        'authenticate' { $response.error = @{ code = -32602; message = 'Terminal methods must never be sent to authenticate.' } }
        default {
            if ($null -eq $request.id) { continue }
            $response.error = @{ code = -32601; message = 'Unsupported fixture request.' }
        }
    }
    [Console]::WriteLine(($response | ConvertTo-Json -Depth 12 -Compress))
}
