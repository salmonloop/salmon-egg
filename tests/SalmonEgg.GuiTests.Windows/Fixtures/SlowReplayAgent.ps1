param(
    [string]$SessionId,
    [int]$MessageCount,
    [int]$ListDelayMs,
    [int]$ReplayStartDelayMs,
    [int]$ChunkDelayMs = 12
)

$ErrorActionPreference = 'Stop'

[Console]::InputEncoding = [System.Text.UTF8Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::UTF8

$sessionId = if (-not [string]::IsNullOrWhiteSpace($SessionId))
{
    $SessionId
}
elseif ([string]::IsNullOrWhiteSpace($env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID))
{
    'gui-remote-session-01'
}
else
{
    $env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID
}

$messageCount = if ($MessageCount -gt 0) { $MessageCount } else { 60 }
if ($MessageCount -gt 0)
{
}
elseif ([int]::TryParse($env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT, [ref]$messageCount) -and $messageCount -gt 0)
{
}
else
{
    $messageCount = 60
}

$replayStartDelayMs = if ($PSBoundParameters.ContainsKey('ReplayStartDelayMs')) { $ReplayStartDelayMs } else { 120 }
$chunkDelayMs = if ($PSBoundParameters.ContainsKey('ChunkDelayMs')) { $ChunkDelayMs } else { 12 }
$listDelayMs = if ($PSBoundParameters.ContainsKey('ListDelayMs')) { $ListDelayMs } else { 0 }
$controlFilePath = $env:SALMONEGG_GUI_CONTROL_FILE
$promptAckMode = $env:SALMONEGG_GUI_PROMPT_ACK_MODE
$promptLateUserMessageId = $env:SALMONEGG_GUI_LATE_USER_MESSAGE_ID
$promptLateUserMessageText = $env:SALMONEGG_GUI_LATE_USER_MESSAGE_TEXT
$promptLateUserMessageDelayMs = 0
if (-not [int]::TryParse($env:SALMONEGG_GUI_LATE_USER_MESSAGE_DELAY_MS, [ref]$promptLateUserMessageDelayMs))
{
    $promptLateUserMessageDelayMs = 0
}
$nextOutboundRequestId = 9000

Add-Type -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

public sealed class SalmonEggGuiLinePump : IDisposable
{
    private readonly BlockingCollection<string> _lines = new BlockingCollection<string>();
    private readonly Thread _readerThread;

    public SalmonEggGuiLinePump(TextReader reader)
    {
        if (reader == null)
        {
            throw new ArgumentNullException("reader");
        }

        _readerThread = new Thread(() =>
        {
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    _lines.Add(line);
                }
            }
            finally
            {
                _lines.CompleteAdding();
            }
        });

        _readerThread.IsBackground = true;
        _readerThread.Start();
    }

    public bool TryTake(out string line, int millisecondsTimeout)
    {
        return _lines.TryTake(out line, millisecondsTimeout);
    }

    public bool IsCompleted
    {
        get { return _lines.IsCompleted; }
    }

    public void Dispose()
    {
        _lines.CompleteAdding();
    }
}
"@

function Write-JsonLine([hashtable]$payload)
{
    $json = $payload | ConvertTo-Json -Compress -Depth 12
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function New-TextContent([string]$text)
{
    return @{
        type = 'text'
        text = $text
    }
}

function Send-Response($id, [hashtable]$result)
{
    Write-JsonLine @{
        jsonrpc = '2.0'
        id = $id
        result = $result
    }
}

function Send-Error($id, [int]$code, [string]$message)
{
    Write-JsonLine @{
        jsonrpc = '2.0'
        id = $id
        error = @{
            code = $code
            message = $message
        }
    }
}

function Send-SessionUpdate([string]$targetSessionId, [hashtable]$update)
{
    Write-JsonLine @{
        jsonrpc = '2.0'
        method = 'session/update'
        params = @{
            sessionId = $targetSessionId
            update = $update
        }
    }
}

function Resolve-SessionSuffix([string]$targetSessionId)
{
    if ([string]::IsNullOrWhiteSpace($targetSessionId))
    {
        return '01'
    }

    $match = [System.Text.RegularExpressions.Regex]::Match($targetSessionId, '(\d{2})$')
    if ($match.Success)
    {
        return $match.Groups[1].Value
    }

    return '01'
}

function New-LoadResult([string]$sessionSuffix)
{
    return @{
        modes = @{
            currentModeId = 'planner'
            availableModes = @(
                @{
                    id = 'agent'
                    name = "Agent $sessionSuffix"
                    description = 'General conversation mode'
                },
                @{
                    id = 'planner'
                    name = "Planner $sessionSuffix"
                    description = 'Structured planning mode'
                }
            )
        }
        configOptions = @(
            @{
                id = 'mode'
                name = 'Mode'
                description = 'Conversation mode'
                type = 'select'
                currentValue = 'planner'
                options = @(
                    @{
                        value = 'agent'
                        name = "Agent $sessionSuffix"
                    },
                    @{
                        value = 'planner'
                        name = "Planner $sessionSuffix"
                    }
                )
            }
        )
    }
}

function Send-AgentRequest([string]$method, [hashtable]$params)
{
    $requestId = $script:nextOutboundRequestId
    $script:nextOutboundRequestId++

    Write-JsonLine @{
        jsonrpc = '2.0'
        id = $requestId
        method = $method
        params = $params
    }

    return $requestId
}

function Wait-AgentResponse([int]$requestId, [int]$timeoutMs = 8000)
{
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline)
    {
        $line = $null
        if (-not $linePump.TryTake([ref]$line, 50))
        {
            if ($linePump.IsCompleted)
            {
                break
            }

            continue
        }

        if ([string]::IsNullOrWhiteSpace($line))
        {
            continue
        }

        $message = $line | ConvertFrom-Json
        if ($null -eq $message.id)
        {
            continue
        }

        if ([string]$message.id -ne [string]$requestId)
        {
            continue
        }

        if ($null -ne $message.error)
        {
            throw "Agent request '$requestId' failed: $($message.error.message)"
        }

        return $message.result
    }

    throw "Timed out waiting for client response '$requestId'."
}

function New-SessionResult([string]$targetSessionId)
{
    $sessionSuffix = Resolve-SessionSuffix $targetSessionId
    $loadResult = New-LoadResult $sessionSuffix

    return @{
        sessionId = $targetSessionId
        modes = $loadResult.modes
        configOptions = $loadResult.configOptions
    }
}

function Resolve-PromptText($requestParams)
{
    if ($null -eq $requestParams -or $null -eq $requestParams.prompt)
    {
        return $promptLateUserMessageText
    }

    foreach ($block in @($requestParams.prompt))
    {
        $text = [string]$block.text
        if (-not [string]::IsNullOrWhiteSpace($text))
        {
            return $text
        }
    }

    return $promptLateUserMessageText
}

function Send-ControlledBackgroundUpdate
{
    if ([string]::IsNullOrWhiteSpace($controlFilePath) -or -not (Test-Path $controlFilePath))
    {
        return
    }

    $raw = Get-Content -Path $controlFilePath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($raw))
    {
        return
    }

    try
    {
        $command = $raw | ConvertFrom-Json
    }
    catch
    {
        return
    }

    $emitted = $false

    if ($command.kind -eq 'background-agent-message' -and -not [string]::IsNullOrWhiteSpace([string]$command.sessionId))
    {
        Send-SessionUpdate ([string]$command.sessionId) @{
            sessionUpdate = 'agent_message_chunk'
            content = (New-TextContent ([string]$command.text))
        }

        $emitted = $true
    }
    if ($emitted)
    {
        Remove-Item -Path $controlFilePath -Force -ErrorAction SilentlyContinue
    }
}

if (-not [Console]::IsInputRedirected)
{
    throw "SlowReplayAgent.ps1 requires redirected stdin."
}

$linePump = [SalmonEggGuiLinePump]::new([Console]::In)

try
{
    while ($true)
    {
        Send-ControlledBackgroundUpdate

        $line = $null
        if (-not $linePump.TryTake([ref]$line, 25))
        {
            if ($linePump.IsCompleted)
            {
                break
            }

            continue
        }

        if ([string]::IsNullOrWhiteSpace($line))
        {
            continue
        }

        $message = $line | ConvertFrom-Json
        $method = [string]$message.method

        switch ($method)
        {
        'initialize'
        {
            Send-Response $message.id @{
                protocolVersion = 1
                agentInfo = @{
                    name = 'gui-slow-replay-agent'
                    title = 'GUI Slow Replay Agent'
                    version = '1.0.0'
                }
                agentCapabilities = @{
                    loadSession = $true
                    sessionCapabilities = @{
                        list = @{}
                    }
                }
            }
            continue
        }

        'session/load'
        {
            $requestedSessionId = [string]$message.params.sessionId
            if ([string]::IsNullOrWhiteSpace($requestedSessionId))
            {
                $requestedSessionId = $sessionId
            }
            $sessionSuffix = Resolve-SessionSuffix $requestedSessionId
            $sessionLabel = "GUI Remote Session $sessionSuffix"

            Start-Sleep -Milliseconds $replayStartDelayMs

            Send-SessionUpdate $requestedSessionId @{
                sessionUpdate = 'session_info_update'
                title = $sessionLabel
                updatedAt = '2026-03-29T12:00:00Z'
            }

            for ($index = 1; $index -le $messageCount; $index++)
            {
                $text = "$sessionLabel replay {0:d3}" -f $index
                $updateType = if (($index % 2) -eq 0) { 'agent_message_chunk' } else { 'user_message_chunk' }

                Send-SessionUpdate $requestedSessionId @{
                    sessionUpdate = $updateType
                    content = (New-TextContent $text)
                }

                if ($chunkDelayMs -gt 0)
                {
                    Start-Sleep -Milliseconds $chunkDelayMs
                }
            }

            Send-Response $message.id (New-LoadResult $sessionSuffix)

            continue
        }

        'session/list'
        {
            if ($listDelayMs -gt 0)
            {
                Start-Sleep -Milliseconds $listDelayMs
            }

            $sessionSuffix = Resolve-SessionSuffix $sessionId
            $sessionLabel = "GUI Remote Session $sessionSuffix"

            Send-Response $message.id @{
                sessions = @(
                    @{
                        sessionId = $sessionId
                        cwd = (Get-Location).Path
                        title = $sessionLabel
                        updatedAt = '2026-03-29T12:00:00Z'
                    }
                )
            }

            continue
        }

        'session/new'
        {
            Send-Response $message.id (New-SessionResult $sessionId)
            continue
        }

        'session/prompt'
        {
            $requestedSessionId = [string]$message.params.sessionId
            if ([string]::IsNullOrWhiteSpace($requestedSessionId))
            {
                $requestedSessionId = $sessionId
            }

            $requestMessageId = [string]$message.params.messageId
            $promptText = Resolve-PromptText $message.params

            if ($promptAckMode -eq 'late-authoritative-update')
            {
                Send-Response $message.id @{
                    stopReason = 'end_turn'
                }

                if ($promptLateUserMessageDelayMs -gt 0)
                {
                    Start-Sleep -Milliseconds $promptLateUserMessageDelayMs
                }

                $lateUserMessageId = if (-not [string]::IsNullOrWhiteSpace($promptLateUserMessageId))
                {
                    $promptLateUserMessageId
                }
                else
                {
                    $requestMessageId
                }

                Send-SessionUpdate $requestedSessionId @{
                    sessionUpdate = 'user_message_chunk'
                    messageId = $lateUserMessageId
                    content = (New-TextContent $promptText)
                }

                continue
            }

            Send-Response $message.id @{
                stopReason = 'end_turn'
                userMessageId = $requestMessageId
            }

            continue
        }

        default
        {
            if ($null -ne $message.id)
            {
                Send-Error $message.id -32601 "Method '$method' is not supported by the GUI smoke agent."
            }
        }
    }
}
}
finally
{
    $linePump.Dispose()
}
