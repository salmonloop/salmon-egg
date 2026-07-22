using System.Diagnostics;
using System.Text;

namespace SalmonEgg.GuiTests.Windows;

internal sealed class NativeDeviceGamepadTestInput : IGamepadTestInput
{
    private const string BridgePathEnvVar = "SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE";
    private const string BridgeTimeoutMsEnvVar = "SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE_TIMEOUT_MS";
    private const string HoldMsEnvVar = "SALMONEGG_GUI_GAMEPAD_NATIVE_HOLD_MS";
    private static readonly TimeSpan DefaultHold = TimeSpan.FromMilliseconds(500);

    private readonly Process _bridgeProcess;
    private readonly int _timeoutMs;
    private readonly TimeSpan _holdDuration;
    private readonly object _releaseSync = new();
    private readonly object _commandSync = new();
    private CancellationTokenSource? _autoReleaseCts;
    private bool _disposed;

    public NativeDeviceGamepadTestInput()
    {
        var bridgePath = GetRequiredBridgePath();

        _timeoutMs = TryParseTimeoutMs(Environment.GetEnvironmentVariable(BridgeTimeoutMsEnvVar));
        _holdDuration = TryParseHold(Environment.GetEnvironmentVariable(HoldMsEnvVar));
        _bridgeProcess = StartBridgeProcess(bridgePath);
        SendCommand("create");
    }

    internal static bool IsBridgeConfigured(out string failureReason)
    {
        var bridgePath = Environment.GetEnvironmentVariable(BridgePathEnvVar);
        if (string.IsNullOrWhiteSpace(bridgePath))
        {
            failureReason = $"Set {BridgePathEnvVar} to the native-device bridge executable to run this smoke.";
            return false;
        }

        if (!File.Exists(bridgePath))
        {
            failureReason = $"The native-device bridge executable was not found: {bridgePath}";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    // Face presses use app-semantic bridge commands so DualSense / Switch Pro HIDMaestro
    // profiles map physical face buttons correctly (not Xbox-only A/B/X/Y field keys).
    public void PressUp() => HoldThenAutoRelease("dpad-up");

    public void PressDown() => HoldThenAutoRelease("dpad-down");

    public void PressLeft() => HoldThenAutoRelease("dpad-left");

    public void PressRight() => HoldThenAutoRelease("dpad-right");

    public void PressActivate() => HoldThenAutoRelease("activate");

    public void PressBack() => HoldThenAutoRelease("back");

    public void PressWestFaceButton() => HoldThenAutoRelease("west");

    public void PressShortcutVoiceToggle() => HoldThenAutoRelease("voice");

    public void PressLeftTrigger() => HoldThenAutoRelease("lt");

    public void PressRightTrigger() => HoldThenAutoRelease("rt");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAutoRelease();

        try
        {
            SendCommand("dispose");
        }
        catch
        {
        }

        try
        {
            if (!_bridgeProcess.HasExited)
            {
                _bridgeProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            _bridgeProcess.Dispose();
        }
    }

    private void HoldThenAutoRelease(string input)
    {
        CancelAutoRelease();
        SendCommand("press " + input);

        var cts = new CancellationTokenSource();
        lock (_releaseSync)
        {
            _autoReleaseCts = cts;
        }

        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_holdDuration, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                SendCommand("press release");
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Best-effort release; dispose/next press will clear state.
            }
        }, token);
    }

    private void CancelAutoRelease()
    {
        CancellationTokenSource? cts;
        lock (_releaseSync)
        {
            cts = _autoReleaseCts;
            _autoReleaseCts = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private Process StartBridgeProcess(string bridgePath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = "serve",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };

        process.Start();
        return process;
    }

    private void SendCommand(string command)
    {
        ThrowIfDisposed();

        lock (_commandSync)
        {
            if (_bridgeProcess.HasExited)
            {
                var stderr = _bridgeProcess.StandardError.ReadToEnd();
                throw new InvalidOperationException(
                    $"The native-device gamepad bridge exited before handling '{command}'."
                    + $"{Environment.NewLine}stderr: {stderr}");
            }

            _bridgeProcess.StandardInput.WriteLine(command);
            _bridgeProcess.StandardInput.Flush();

            while (true)
            {
                var lineTask = _bridgeProcess.StandardOutput.ReadLineAsync();
                if (!lineTask.Wait(_timeoutMs))
                {
                    throw new TimeoutException(
                        $"The native-device gamepad bridge timed out after {_timeoutMs} ms for '{command}'.");
                }

                var line = lineTask.Result;
                if (line is null)
                {
                    var stderr = _bridgeProcess.StandardError.ReadToEnd();
                    throw new InvalidOperationException(
                        $"The native-device gamepad bridge closed its output before acknowledging '{command}'."
                        + $"{Environment.NewLine}stderr: {stderr}");
                }

                if (string.Equals(line, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The native-device gamepad bridge rejected '{command}': {line}");
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static int TryParseTimeoutMs(string? rawValue)
    {
        return int.TryParse(rawValue, out var parsed) && parsed > 0
            ? parsed
            : 5_000;
    }

    private static TimeSpan TryParseHold(string? rawValue)
    {
        if (int.TryParse(rawValue, out var parsed) && parsed > 0)
        {
            return TimeSpan.FromMilliseconds(parsed);
        }

        return DefaultHold;
    }

    private static string GetRequiredBridgePath()
    {
        var bridgePath = Environment.GetEnvironmentVariable(BridgePathEnvVar);
        if (string.IsNullOrWhiteSpace(bridgePath))
        {
            throw new InvalidOperationException(
                $"Set {BridgePathEnvVar} to the native-device bridge executable to use the native-device gamepad backend.");
        }

        if (!File.Exists(bridgePath))
        {
            throw new FileNotFoundException(
                $"The native-device bridge executable was not found: {bridgePath}",
                bridgePath);
        }

        return bridgePath;
    }
}
