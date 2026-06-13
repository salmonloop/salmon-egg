param(
    [string]$ScenarioJsonPath,
    [string]$ListenUrl,
    [string]$ReadySignalPath
)

$ErrorActionPreference = 'Stop'

[Console]::InputEncoding = [System.Text.UTF8Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::UTF8

$envScenario = $null
if (-not [string]::IsNullOrWhiteSpace($ScenarioJsonPath) -and (Test-Path -LiteralPath $ScenarioJsonPath))
{
    $envScenario = Get-Content -LiteralPath $ScenarioJsonPath -Raw | ConvertFrom-Json
}
else
{
    $envScenario = [pscustomobject]@{}
}

function Get-ScenarioValue([string]$name, $defaultValue = $null)
{
    if ($null -ne $envScenario -and $null -ne $envScenario.PSObject.Properties[$name])
    {
        $value = $envScenario.$name
        if ($null -ne $value)
        {
            return $value
        }
    }

    return $defaultValue
}

function To-LowerInvariant([string]$value)
{
    if ([string]::IsNullOrWhiteSpace($value))
    {
        return [string]::Empty
    }

    return $value.ToLowerInvariant()
}

function Get-IntScenarioValue([string]$name, [int]$defaultValue = 0)
{
    $raw = Get-ScenarioValue $name $null
    if ($null -eq $raw)
    {
        return $defaultValue
    }

    try
    {
        return [int]$raw
    }
    catch
    {
        return $defaultValue
    }
}

Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

public static class SalmonEggGuiWebSocketHelpers
{
    public static string ReceiveText(WebSocket socket)
    {
        if (socket == null)
        {
            throw new ArgumentNullException("socket");
        }

        var buffer = new byte[4096];
        using (var stream = new MemoryStream())
        {
            while (true)
            {
                var result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).GetAwaiter().GetResult();
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public static void SendText(WebSocket socket, string text)
    {
        if (socket == null)
        {
            throw new ArgumentNullException("socket");
        }

        if (text == null)
        {
            text = string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static WebSocket AcceptSocket(HttpListenerContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException("context");
        }

        return context.AcceptWebSocketAsync(null).GetAwaiter().GetResult().WebSocket;
    }
}
"@

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

$script:sessionId = [string](Get-ScenarioValue 'sessionId' 'gui-remote-session-01')
$script:initializeBehavior = To-LowerInvariant ([string](Get-ScenarioValue 'initializeBehavior' 'Success'))
$script:sessionNewBehavior = To-LowerInvariant ([string](Get-ScenarioValue 'sessionNewBehavior' 'Success'))
$script:cwdAcceptancePolicy = To-LowerInvariant ([string](Get-ScenarioValue 'cwdAcceptancePolicy' 'AcceptAny'))
$script:modesVariant = To-LowerInvariant ([string](Get-ScenarioValue 'modesVariant' 'Normal'))
$script:acceptedCwd = [string](Get-ScenarioValue 'acceptedCwd' (Get-Location).Path)
$script:mappedRemoteCwd = [string](Get-ScenarioValue 'mappedRemoteCwd' $script:acceptedCwd)
$script:initializeDelayMs = Get-IntScenarioValue 'initializeDelayMs' 0
$script:sessionNewDelayMs = Get-IntScenarioValue 'sessionNewDelayMs' 0
$script:sessionNewErrorCode = Get-IntScenarioValue 'sessionNewErrorCode' -32602
$script:sessionNewErrorMessage = [string](Get-ScenarioValue 'sessionNewErrorMessage' 'session/new rejected by GUI mock ACP harness.')
$script:replayMessageCount = [int]([string]$env:SALMONEGG_GUI_FAKE_REMOTE_REPLAY_MESSAGE_COUNT)
if ($script:replayMessageCount -le 0)
{
    $script:replayMessageCount = 60
}
$script:replayStartDelayMs = 120
$script:chunkDelayMs = 12
$script:listDelayMs = 0
$script:controlFilePath = $env:SALMONEGG_GUI_CONTROL_FILE
$script:promptAckMode = $env:SALMONEGG_GUI_PROMPT_ACK_MODE
$script:promptLateUserMessageId = $env:SALMONEGG_GUI_LATE_USER_MESSAGE_ID
$script:promptLateUserMessageText = $env:SALMONEGG_GUI_LATE_USER_MESSAGE_TEXT
$script:promptLateUserMessageDelayMs = 0
if (-not [int]::TryParse($env:SALMONEGG_GUI_LATE_USER_MESSAGE_DELAY_MS, [ref]$script:promptLateUserMessageDelayMs))
{
    $script:promptLateUserMessageDelayMs = 0
}
$script:transportWriter = $null

function Write-JsonLine([hashtable]$payload)
{
    $json = $payload | ConvertTo-Json -Compress -Depth 16
    if ($null -ne $script:transportWriter)
    {
        [SalmonEggGuiWebSocketHelpers]::SendText($script:transportWriter, $json)
        return
    }

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

function New-ModesState([string]$sessionSuffix)
{
    if ($script:modesVariant -eq 'empty')
    {
        return @{
            currentModeId = ''
            availableModes = @()
        }
    }

    return @{
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
}

function New-ConfigOptions([string]$sessionSuffix)
{
    if ($script:modesVariant -eq 'empty')
    {
        return ,@()
    }

    return ,@(
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

function New-LoadResult([string]$sessionSuffix)
{
    return @{
        modes = (New-ModesState $sessionSuffix)
        configOptions = (New-ConfigOptions $sessionSuffix)
    }
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
        return $script:promptLateUserMessageText
    }

    foreach ($block in @($requestParams.prompt))
    {
        $text = [string]$block.text
        if (-not [string]::IsNullOrWhiteSpace($text))
        {
            return $text
        }
    }

    return $script:promptLateUserMessageText
}

function Send-ControlledBackgroundUpdate
{
    if ([string]::IsNullOrWhiteSpace($script:controlFilePath) -or -not (Test-Path $script:controlFilePath))
    {
        return
    }

    $raw = Get-Content -Path $script:controlFilePath -Raw -ErrorAction SilentlyContinue
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
        Remove-Item -Path $script:controlFilePath -Force -ErrorAction SilentlyContinue
    }
}

function Test-CwdAccepted([string]$cwd)
{
    switch ($script:cwdAcceptancePolicy)
    {
        'acceptany' { return $true }
        'rejectmissing' { return -not [string]::IsNullOrWhiteSpace($cwd) }
        'rejectnonexistent' { return -not [string]::IsNullOrWhiteSpace($cwd) -and (Test-Path -LiteralPath $cwd) }
        'rejectunmappedremote' { return -not [string]::IsNullOrWhiteSpace($cwd) -and $cwd -eq $script:mappedRemoteCwd }
        default { return $true }
    }
}

function Send-SessionValidationError($requestId, [string]$cwd)
{
    if ([string]::IsNullOrWhiteSpace($cwd))
    {
        Send-Error $requestId -32602 'Missing cwd.'
        return
    }

    switch ($script:cwdAcceptancePolicy)
    {
        'rejectnonexistent' { Send-Error $requestId -32602 "cwd does not exist: $cwd" }
        'rejectunmappedremote' { Send-Error $requestId -32602 "cwd is not mapped to the local project root: $cwd" }
        default { Send-Error $requestId -32602 "cwd is not accepted by the mock ACP harness: $cwd" }
    }
}

function New-InitializeResponse
{
    return @{
        protocolVersion = 1
        agentInfo = @{
            name = 'gui-mock-acp-agent'
            title = 'GUI Mock ACP Harness'
            version = '1.0.0'
        }
        agentCapabilities = @{
            loadSession = $true
            sessionCapabilities = @{
                list = @{}
            }
        }
    }
}

function Handle-AcpMessage([object]$message)
{
    $method = [string]$message.method
    switch ($method)
    {
        'initialize'
        {
            if ($script:initializeDelayMs -gt 0)
            {
                Start-Sleep -Milliseconds $script:initializeDelayMs
            }

            if ($script:initializeBehavior -eq 'noresponse')
            {
                return
            }

            Send-Response $message.id (New-InitializeResponse)
            return
        }

        'session/load'
        {
            $requestedSessionId = [string]$message.params.sessionId
            if ([string]::IsNullOrWhiteSpace($requestedSessionId))
            {
                $requestedSessionId = $script:sessionId
            }

            $requestedCwd = [string]$message.params.cwd
            if (-not (Test-CwdAccepted $requestedCwd))
            {
                Send-SessionValidationError $message.id $requestedCwd
                return
            }

            $sessionSuffix = Resolve-SessionSuffix $requestedSessionId
            $sessionLabel = "GUI Remote Session $sessionSuffix"

            if ($script:replayStartDelayMs -gt 0)
            {
                Start-Sleep -Milliseconds $script:replayStartDelayMs
            }

            Send-SessionUpdate $requestedSessionId @{
                sessionUpdate = 'session_info_update'
                title = $sessionLabel
                updatedAt = '2026-03-29T12:00:00Z'
            }

            for ($index = 1; $index -le $script:replayMessageCount; $index++)
            {
                $text = "$sessionLabel replay {0:d3}" -f $index
                $updateType = if (($index % 2) -eq 0) { 'agent_message_chunk' } else { 'user_message_chunk' }

                Send-SessionUpdate $requestedSessionId @{
                    sessionUpdate = $updateType
                    content = (New-TextContent $text)
                }

                if ($script:chunkDelayMs -gt 0)
                {
                    Start-Sleep -Milliseconds $script:chunkDelayMs
                }
            }

            Send-Response $message.id (New-LoadResult $sessionSuffix)
            return
        }

        'session/list'
        {
            if ($script:listDelayMs -gt 0)
            {
                Start-Sleep -Milliseconds $script:listDelayMs
            }

            $sessionSuffix = Resolve-SessionSuffix $script:sessionId
            $sessionLabel = "GUI Remote Session $sessionSuffix"

            Send-Response $message.id @{
                sessions = @(
                    @{
                        sessionId = $script:sessionId
                        cwd = (Get-Location).Path
                        title = $sessionLabel
                        updatedAt = '2026-03-29T12:00:00Z'
                    }
                )
            }
            return
        }

        'session/new'
        {
            $requestedCwd = [string]$message.params.cwd
            if (-not (Test-CwdAccepted $requestedCwd))
            {
                Send-SessionValidationError $message.id $requestedCwd
                return
            }

            if ($script:sessionNewDelayMs -gt 0)
            {
                Start-Sleep -Milliseconds $script:sessionNewDelayMs
            }

            switch ($script:sessionNewBehavior)
            {
                'error'
                {
                    Send-Error $message.id $script:sessionNewErrorCode $script:sessionNewErrorMessage
                    return
                }
                'noresponse'
                {
                    return
                }
            }

            $sessionSuffix = Resolve-SessionSuffix $script:sessionId
            $loadResult = New-LoadResult $sessionSuffix
            Send-Response $message.id @{
                sessionId = $script:sessionId
                modes = $loadResult.modes
                configOptions = $loadResult.configOptions
            }
            return
        }

        'session/prompt'
        {
            $requestedSessionId = [string]$message.params.sessionId
            if ([string]::IsNullOrWhiteSpace($requestedSessionId))
            {
                $requestedSessionId = $script:sessionId
            }

            $requestMessageId = [string]$message.params.messageId
            $promptText = Resolve-PromptText $message.params

            if ($script:promptAckMode -eq 'late-authoritative-update')
            {
                Send-Response $message.id @{
                    stopReason = 'end_turn'
                }

                if ($script:promptLateUserMessageDelayMs -gt 0)
                {
                    Start-Sleep -Milliseconds $script:promptLateUserMessageDelayMs
                }

                $lateUserMessageId = if (-not [string]::IsNullOrWhiteSpace($script:promptLateUserMessageId))
                {
                    $script:promptLateUserMessageId
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

                return
            }

            Send-Response $message.id @{
                stopReason = 'end_turn'
                userMessageId = $requestMessageId
            }
            return
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

function Process-StdioHarness
{
    if (-not [Console]::IsInputRedirected)
    {
        throw 'MockAcpHarness.ps1 requires redirected stdin for stdio transport.'
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
            Handle-AcpMessage $message
        }
    }
    finally
    {
        $linePump.Dispose()
    }
}

function ConvertTo-HttpPrefix([string]$url)
{
    $uri = [Uri]$url
    $builder = [UriBuilder]::new($uri)
    switch ($builder.Scheme.ToLowerInvariant())
    {
        'ws' { $builder.Scheme = 'http' }
        'wss' { $builder.Scheme = 'https' }
        default { throw "ListenUrl must use ws:// or wss://, got '$url'." }
    }

    if ([string]::IsNullOrWhiteSpace($builder.Path))
    {
        $builder.Path = '/'
    }
    elseif (-not $builder.Path.EndsWith('/'))
    {
        $builder.Path += '/'
    }

    return $builder.Uri.AbsoluteUri
}

function Process-WebSocketHarness([string]$listenUrl)
{
    if ([string]::IsNullOrWhiteSpace($listenUrl))
    {
        throw 'ListenUrl is required for websocket transport.'
    }

    $httpPrefix = ConvertTo-HttpPrefix $listenUrl
    $listener = [System.Net.HttpListener]::new()
    $listener.Prefixes.Add($httpPrefix)
    $listener.Start()

    if (-not [string]::IsNullOrWhiteSpace($ReadySignalPath))
    {
        Set-Content -LiteralPath $ReadySignalPath -Value $listenUrl -Encoding UTF8
    }

    try
    {
        while ($true)
        {
            $context = $listener.GetContext()
            if (-not $context.Request.IsWebSocketRequest)
            {
                $context.Response.StatusCode = 400
                $context.Response.Close()
                continue
            }

            $script:transportWriter = [SalmonEggGuiWebSocketHelpers]::AcceptSocket($context)
            try
            {
                while ($script:transportWriter.State -eq [System.Net.WebSockets.WebSocketState]::Open)
                {
                    Send-ControlledBackgroundUpdate
                    $text = [SalmonEggGuiWebSocketHelpers]::ReceiveText($script:transportWriter)
                    if ($null -eq $text)
                    {
                        break
                    }

                    if ([string]::IsNullOrWhiteSpace($text))
                    {
                        continue
                    }

                    $message = $text | ConvertFrom-Json
                    Handle-AcpMessage $message
                }
            }
            finally
            {
                if ($null -ne $script:transportWriter)
                {
                    $script:transportWriter.Dispose()
                    $script:transportWriter = $null
                }
            }
        }
    }
    finally
    {
        $listener.Stop()
        $listener.Close()
        if (-not [string]::IsNullOrWhiteSpace($ReadySignalPath) -and -not (Test-Path -LiteralPath $ReadySignalPath))
        {
            Set-Content -LiteralPath $ReadySignalPath -Value $listenUrl -Encoding UTF8
        }
    }
}

if ((To-LowerInvariant ([string](Get-ScenarioValue 'transportKind' 'stdio'))) -eq 'websocket')
{
    Process-WebSocketHarness $ListenUrl
}
else
{
    Process-StdioHarness
}
