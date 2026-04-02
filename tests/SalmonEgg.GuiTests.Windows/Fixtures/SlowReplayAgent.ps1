$ErrorActionPreference = 'Stop'

[Console]::InputEncoding = [System.Text.UTF8Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::UTF8

$sessionId = if ([string]::IsNullOrWhiteSpace($env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID))
{
    'gui-remote-session-01'
}
else
{
    $env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_SESSION_ID
}

$messageCount = 60
if ([int]::TryParse($env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT, [ref]$messageCount) -and $messageCount -gt 0)
{
}
else
{
    $messageCount = 60
}

$replayStartDelayMs = 120
$chunkDelayMs = 12

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

while (($line = [Console]::In.ReadLine()) -ne $null)
{
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

        default
        {
            if ($null -ne $message.id)
            {
                Send-Error $message.id -32601 "Method '$method' is not supported by the GUI smoke agent."
            }
        }
    }
}
