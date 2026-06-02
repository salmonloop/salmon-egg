#if WINDOWS
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsAudioInputSignalDiagnosticsService : IAudioInputSignalDiagnosticsService, IDisposable
{
    private const int ClsCtxAll = 23;
    private const float NonSilentThreshold = 0.02f;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly object _sync = new();
    private readonly ILogger<WindowsAudioInputSignalDiagnosticsService> _logger;
    private AudioInputSignalDiagnosticsSnapshot _snapshot = new(
        IsSupported: true,
        IsMonitoring: false,
        ObservedSampleCount: 0,
        ObservedNonSilentSampleCount: 0,
        MaxPeakLevel: 0,
        FirstNonSilentSampleObservedAt: null,
        LastNonSilentSampleObservedAt: null,
        FailureMessage: null);
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioMeterInformation? _audioMeter;
    private bool _disposed;

    public WindowsAudioInputSignalDiagnosticsService(ILogger<WindowsAudioInputSignalDiagnosticsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AudioInputSignalDiagnosticsSnapshot GetCurrentSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopMonitoringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            InitializeAudioMeter();
            lock (_sync)
            {
                _snapshot = new AudioInputSignalDiagnosticsSnapshot(
                    IsSupported: true,
                    IsMonitoring: true,
                    ObservedSampleCount: 0,
                    ObservedNonSilentSampleCount: 0,
                    MaxPeakLevel: 0,
                    FirstNonSilentSampleObservedAt: null,
                    LastNonSilentSampleObservedAt: null,
                    FailureMessage: null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio input signal diagnostics monitoring failed to initialize.");
            lock (_sync)
            {
                _snapshot = _snapshot with
                {
                    IsMonitoring = false,
                    FailureMessage = ex.Message
                };
            }
            DisposeAudioMeter();
            return;
        }

        var monitoringCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringCancellationTokenSource = monitoringCancellationTokenSource;
        _monitoringTask = Task.Run(() => MonitorAsync(monitoringCancellationTokenSource.Token), CancellationToken.None);
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var monitoringCancellationTokenSource = _monitoringCancellationTokenSource;
        var monitoringTask = _monitoringTask;
        _monitoringCancellationTokenSource = null;
        _monitoringTask = null;

        try
        {
            monitoringCancellationTokenSource?.Cancel();
        }
        catch
        {
        }

        try
        {
            if (monitoringTask is not null)
            {
                await monitoringTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (monitoringCancellationTokenSource?.IsCancellationRequested == true)
        {
        }
        finally
        {
            monitoringCancellationTokenSource?.Dispose();
        }

        DisposeAudioMeter();
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                IsMonitoring = false
            };
        }
    }

    private void InitializeAudioMeter()
    {
        DisposeAudioMeter();

        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eMultimedia, out var device));
        var interfaceId = typeof(IAudioMeterInformation).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, ClsCtxAll, IntPtr.Zero, out var audioMeterObject));

        _deviceEnumerator = enumerator;
        _device = device;
        _audioMeter = (IAudioMeterInformation)audioMeterObject;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_audioMeter is null)
                {
                    return;
                }

                Marshal.ThrowExceptionForHR(_audioMeter.GetPeakValue(out var peakValue));
                var now = DateTimeOffset.Now;
                lock (_sync)
                {
                    var observedSampleCount = _snapshot.ObservedSampleCount + 1;
                    var observedNonSilentSampleCount = _snapshot.ObservedNonSilentSampleCount;
                    var firstNonSilentSampleObservedAt = _snapshot.FirstNonSilentSampleObservedAt;
                    var lastNonSilentSampleObservedAt = _snapshot.LastNonSilentSampleObservedAt;
                    var maxPeakLevel = Math.Max(_snapshot.MaxPeakLevel, peakValue);
                    if (peakValue >= NonSilentThreshold)
                    {
                        observedNonSilentSampleCount++;
                        firstNonSilentSampleObservedAt ??= now;
                        lastNonSilentSampleObservedAt = now;
                    }

                    _snapshot = _snapshot with
                    {
                        IsMonitoring = true,
                        ObservedSampleCount = observedSampleCount,
                        ObservedNonSilentSampleCount = observedNonSilentSampleCount,
                        MaxPeakLevel = maxPeakLevel,
                        FirstNonSilentSampleObservedAt = firstNonSilentSampleObservedAt,
                        LastNonSilentSampleObservedAt = lastNonSilentSampleObservedAt,
                        FailureMessage = null
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audio input signal diagnostics monitoring failed while polling.");
                lock (_sync)
                {
                    _snapshot = _snapshot with
                    {
                        IsMonitoring = false,
                        FailureMessage = ex.Message
                    };
                }
                return;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private void DisposeAudioMeter()
    {
        ReleaseComObject(ref _audioMeter);
        ReleaseComObject(ref _device);
        ReleaseComObject(ref _deviceEnumerator);
    }

    private static void ReleaseComObject<T>(ref T? comObject)
        where T : class
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch
        {
        }
        finally
        {
            comObject = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            _monitoringCancellationTokenSource?.Cancel();
        }
        catch
        {
        }

        _monitoringCancellationTokenSource?.Dispose();
        _monitoringCancellationTokenSource = null;
        _monitoringTask = null;
        DisposeAudioMeter();
    }

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid interfaceId, int clsContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peakValue);
        int GetMeteringChannelCount(out int channelCount);
        int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);
        int QueryHardwareSupport(out int hardwareSupportMask);
    }
}
#endif
