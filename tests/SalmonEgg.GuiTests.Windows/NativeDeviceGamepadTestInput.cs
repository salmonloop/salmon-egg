using System.Diagnostics;
using System.Text;

namespace SalmonEgg.GuiTests.Windows;

internal sealed class NativeDeviceGamepadTestInput : IGamepadTestInput
{
    private const string BridgePathEnvVar = "SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE";
    private const string BridgeTimeoutMsEnvVar = "SALMONEGG_GUI_GAMEPAD_NATIVE_BRIDGE_TIMEOUT_MS";

    private readonly Process _bridgeProcess;
    private readonly int _timeoutMs;
    private bool _disposed;

    public NativeDeviceGamepadTestInput()
    {
        var bridgePath = GetRequiredBridgePath();

        _timeoutMs = TryParseTimeoutMs(Environment.GetEnvironmentVariable(BridgeTimeoutMsEnvVar));
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

    public void PressUp()
    {
        SendCommand("press dpad-up");
    }

    public void PressDown()
    {
        SendCommand("press dpad-down");
    }

    public void PressLeft()
    {
        SendCommand("press dpad-left");
    }

    public void PressRight()
    {
        SendCommand("press dpad-right");
    }

    public void PressActivate()
    {
        SendCommand("press a");
    }

    public void PressBack()
    {
        SendCommand("press b");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static int TryParseTimeoutMs(string? rawValue)
    {
        return int.TryParse(rawValue, out var parsed) && parsed > 0
            ? parsed
            : 5000;
    }

    private static string GetRequiredBridgePath()
    {
        var bridgePath = Environment.GetEnvironmentVariable(BridgePathEnvVar)
            ?? throw new InvalidOperationException(
                $"The native-device gamepad backend requires {BridgePathEnvVar} to point to a bridge executable.");

        if (!File.Exists(bridgePath))
        {
            throw new FileNotFoundException(
                $"The native-device gamepad bridge executable was not found: {bridgePath}",
                bridgePath);
        }

        return bridgePath;
    }
}
