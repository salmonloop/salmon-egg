using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Input;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class VoiceInputDiagnosticsServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_WhenLatestSessionReadyButNoRecognition_ReportsSilentSession()
    {
        var logText = """
            2026-06-02 00:40:09.136 +08:00 [INF] [Thread:2] Voice input start requested. RequestId=bfa3b5645fa647a49f2506074d59bbac LanguageTag=zh-CN
            2026-06-02 00:40:12.028 +08:00 [INF] [Thread:2] Voice input recognizer ready. RequestId=bfa3b5645fa647a49f2506074d59bbac LanguageTag=zh-CN
            2026-06-02 00:40:19.645 +08:00 [INF] [Thread:2] Voice input stop requested. RequestId=bfa3b5645fa647a49f2506074d59bbac WasListening=true
            2026-06-02 00:40:19.652 +08:00 [INF] [Thread:18] Voice input session ended. RequestId=bfa3b5645fa647a49f2506074d59bbac
            """;
        var sut = CreateService(logText, VoiceInputPermissionResult.Granted());

        var snapshot = await sut.GetSnapshotAsync();

        Assert.True(snapshot.IsSupported);
        Assert.NotNull(snapshot.LatestSession);
        Assert.Equal("bfa3b5645fa647a49f2506074d59bbac", snapshot.LatestSession!.RequestId);
        Assert.Equal("zh-CN", snapshot.LatestSession.LanguageTag);
        Assert.Equal(VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition, snapshot.LatestSession.Outcome);
        Assert.NotNull(snapshot.LatestSession.RecognizerReadyAt);
        Assert.NotNull(snapshot.LatestSession.StartRequestedAt);
        Assert.NotNull(snapshot.LatestSession.StopRequestedAt);
        Assert.NotNull(snapshot.LatestSession.EndedAt);
        Assert.Null(snapshot.LatestSession.FirstPartialAt);
        Assert.Null(snapshot.LatestSession.FinalResultAt);
        Assert.Equal(0, snapshot.LatestSession.PartialResultCount);
        Assert.Equal(0, snapshot.LatestSession.FinalResultCount);
        Assert.Equal(0, snapshot.LatestSession.EmptyPartialResultCount);
        Assert.Equal(0, snapshot.LatestSession.EmptyFinalResultCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReportsCurrentUiCultureAsCurrentLanguageTag()
    {
        using var culture = new CurrentUiCultureScope("zh-CN");
        var sut = CreateService(string.Empty, VoiceInputPermissionResult.Granted());

        var snapshot = await sut.GetSnapshotAsync();

        Assert.Equal("zh-CN", snapshot.CurrentLanguageTag);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenPermissionDenied_PreservesAuthorizationRequirement()
    {
        var permission = new VoiceInputPermissionResult(
            VoiceInputPermissionStatus.Denied,
            "Microphone access is blocked for SalmonEgg.",
            RequiresAuthorization: true);
        var sut = CreateService(string.Empty, permission);

        var snapshot = await sut.GetSnapshotAsync();

        Assert.Equal(permission, snapshot.Permission);
    }

    [Fact]
    public async Task GetSnapshotAsync_UsesNonInteractivePermissionQueryInsteadOfInteractiveEnsure()
    {
        var permission = VoiceInputPermissionResult.Granted();
        var voiceInputService = new FakeVoiceInputService(permission);
        var runtimeSource = new FakeVoiceInputRuntimeDiagnosticsSource(new VoiceInputRuntimeDiagnostics(null, null, null));
        var sut = new VoiceInputDiagnosticsService(
            voiceInputService,
            runtimeSource,
            new FakeAppDataService("C:/app/logs"),
            new FakeLogFileCatalog(string.Empty));

        var snapshot = await sut.GetSnapshotAsync();

        Assert.Equal(permission, snapshot.Permission);
        Assert.Equal(0, voiceInputService.EnsurePermissionCallCount);
        Assert.Equal(1, voiceInputService.GetPermissionStatusCallCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenLatestSessionHasFinalResult_PrefersMostRecentRequest()
    {
        var logText = """
            2026-06-02 00:39:01.000 +08:00 [INF] [Thread:2] Voice input start requested. RequestId=older LanguageTag=en-US
            2026-06-02 00:39:01.500 +08:00 [INF] [Thread:2] Voice input recognizer ready. RequestId=older LanguageTag=en-US
            2026-06-02 00:39:02.000 +08:00 [INF] [Thread:2] Voice input final result received. RequestId=older TextLength=12
            2026-06-02 00:40:01.000 +08:00 [INF] [Thread:2] Voice input start requested. RequestId=latest LanguageTag=zh-CN
            2026-06-02 00:40:03.000 +08:00 [INF] [Thread:2] Voice input recognizer ready. RequestId=latest LanguageTag=zh-CN
            2026-06-02 00:40:05.000 +08:00 [INF] [Thread:2] Voice input first partial received. RequestId=latest TextLength=4
            2026-06-02 00:40:06.000 +08:00 [INF] [Thread:2] Voice input final result received. RequestId=latest TextLength=8
            """;
        var sut = CreateService(logText, VoiceInputPermissionResult.Granted());

        var snapshot = await sut.GetSnapshotAsync();

        Assert.NotNull(snapshot.LatestSession);
        Assert.Equal("latest", snapshot.LatestSession!.RequestId);
        Assert.Equal(VoiceInputDiagnosticSessionOutcome.FinalResultReceived, snapshot.LatestSession.Outcome);
        Assert.NotNull(snapshot.LatestSession.FirstPartialAt);
        Assert.NotNull(snapshot.LatestSession.FinalResultAt);
        Assert.Equal(1, snapshot.LatestSession.PartialResultCount);
        Assert.Equal(1, snapshot.LatestSession.FinalResultCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenRuntimeDiagnosticsProvideDefaultDeviceAndCallbackCounts_MergesThemIntoLatestSession()
    {
        var logText = """
            2026-06-02 02:13:59.340 +08:00 [INF] [Thread:2] Voice input start requested. RequestId=runtime-request LanguageTag=zh-CN
            2026-06-02 02:14:02.074 +08:00 [INF] [Thread:2] Voice input recognizer ready. RequestId=runtime-request LanguageTag=zh-CN
            2026-06-02 02:14:06.265 +08:00 [INF] [Thread:2] Voice input stop requested. RequestId=runtime-request WasListening=true
            2026-06-02 02:14:06.270 +08:00 [INF] [Thread:12] Voice input session ended. RequestId=runtime-request
            """;
        var runtimeDiagnostics = new VoiceInputRuntimeDiagnostics(
            DefaultInputDeviceName: "USB Microphone",
            DefaultInputDeviceId: "device-1",
            LatestSession: new VoiceInputDiagnosticSession(
                "runtime-request",
                VoiceInputDiagnosticSessionOutcome.ReadyWithoutRecognition,
                StartRequestedAt: new DateTimeOffset(2026, 6, 2, 2, 13, 59, 340, TimeSpan.FromHours(8)),
                RecognizerReadyAt: new DateTimeOffset(2026, 6, 2, 2, 14, 2, 74, TimeSpan.FromHours(8)),
                FirstPartialAt: null,
                FinalResultAt: null,
                StopRequestedAt: new DateTimeOffset(2026, 6, 2, 2, 14, 6, 265, TimeSpan.FromHours(8)),
                EndedAt: new DateTimeOffset(2026, 6, 2, 2, 14, 6, 270, TimeSpan.FromHours(8)),
                ErrorAt: null,
                ErrorCode: null,
                ErrorMessage: null,
                LanguageTag: "zh-CN",
                PartialResultCount: 0,
                FinalResultCount: 0,
                EmptyPartialResultCount: 2,
                EmptyFinalResultCount: 1,
                CompletionStatus: "StoppedByApp"));
        var sut = CreateService(logText, VoiceInputPermissionResult.Granted(), runtimeDiagnostics);

        var snapshot = await sut.GetSnapshotAsync();

        Assert.Equal("USB Microphone", snapshot.DefaultInputDeviceName);
        Assert.Equal("device-1", snapshot.DefaultInputDeviceId);
        Assert.NotNull(snapshot.LatestSession);
        Assert.Equal("runtime-request", snapshot.LatestSession!.RequestId);
        Assert.Equal(2, snapshot.LatestSession.EmptyPartialResultCount);
        Assert.Equal(1, snapshot.LatestSession.EmptyFinalResultCount);
        Assert.Equal("StoppedByApp", snapshot.LatestSession.CompletionStatus);
    }

    private static VoiceInputDiagnosticsService CreateService(
        string logText,
        VoiceInputPermissionResult permission,
        VoiceInputRuntimeDiagnostics? runtimeDiagnostics = null)
    {
        var paths = new FakeAppDataService("C:/app/logs");
        var voiceInputService = new FakeVoiceInputService(permission);
        var logFileCatalog = new FakeLogFileCatalog(logText);
        var runtimeSource = new FakeVoiceInputRuntimeDiagnosticsSource(runtimeDiagnostics ?? new VoiceInputRuntimeDiagnostics(null, null, null));
        return new VoiceInputDiagnosticsService(voiceInputService, runtimeSource, paths, logFileCatalog);
    }

    private sealed class FakeAppDataService : IAppDataService
    {
        public FakeAppDataService(string logsDirectoryPath)
        {
            LogsDirectoryPath = logsDirectoryPath;
        }

        public string AppDataRootPath => "C:/app";

        public string ConfigRootPath => "C:/app/config";

        public string LogsDirectoryPath { get; }

        public string CacheRootPath => "C:/app/cache";

        public string ExportsDirectoryPath => "C:/app/exports";
    }

    private sealed class FakeLogFileCatalog : ILogFileCatalog
    {
        private readonly string _logText;

        public FakeLogFileCatalog(string logText)
        {
            _logText = logText;
        }

        public Task<LogFileSummary?> GetLatestAsync(string logsDirectoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult<LogFileSummary?>(new LogFileSummary(
                $"{logsDirectoryPath}/app.log",
                new DateTimeOffset(2026, 6, 2, 0, 40, 19, TimeSpan.FromHours(8))));

        public Task<string?> ReadTailAsync(string filePath, int maxChars, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(_logText);
    }

    private sealed class FakeVoiceInputService : IVoiceInputService
    {
        private readonly VoiceInputPermissionResult _permission;

        public FakeVoiceInputService(VoiceInputPermissionResult permission)
        {
            _permission = permission;
        }

        public bool IsSupported => _permission.Status != VoiceInputPermissionStatus.Unsupported;

        public bool IsListening => false;

        public int EnsurePermissionCallCount { get; private set; }

        public int GetPermissionStatusCallCount { get; private set; }

        public event EventHandler<VoiceInputPartialResult>? PartialResultReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceInputFinalResult>? FinalResultReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceInputSessionEndedResult>? SessionEnded
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceInputErrorResult>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task<VoiceInputPermissionResult> EnsurePermissionAsync(CancellationToken cancellationToken = default)
        {
            EnsurePermissionCallCount++;
            return Task.FromResult(_permission);
        }

        public Task<VoiceInputPermissionResult> GetPermissionStatusAsync(CancellationToken cancellationToken = default)
        {
            GetPermissionStatusCallCount++;
            return Task.FromResult(_permission);
        }

        public Task<bool> TryRequestAuthorizationHelpAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task StartAsync(VoiceInputSessionOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeVoiceInputRuntimeDiagnosticsSource : IVoiceInputRuntimeDiagnosticsSource
    {
        private readonly VoiceInputRuntimeDiagnostics _snapshot;

        public FakeVoiceInputRuntimeDiagnosticsSource(VoiceInputRuntimeDiagnostics snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<VoiceInputRuntimeDiagnostics> GetRuntimeDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);
    }

    private sealed class CurrentUiCultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CurrentUiCultureScope(string cultureName)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
