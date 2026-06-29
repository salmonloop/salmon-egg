using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class NativeVoiceInputServiceSourceTests
{
    [Fact]
    public void NativeVoiceInputService_DoesNotReuseRecognizerAcrossVoiceSessions()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/NativeVoiceInputService.cs");

        Assert.Contains("CreateSessionRecognizerAsync", code);
        Assert.DoesNotContain("string.Equals(_activeLanguageTag, normalizedLanguage", code);
    }

    [Fact]
    public void NativeVoiceInputService_DisposesRecognizerWhenSessionCompletes()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/NativeVoiceInputService.cs");

        Assert.Contains("DisposeRecognizer();", code);
        Assert.Contains("finally", code);
    }

    [Fact]
    public void NativeVoiceInputService_UsesGracefulStopBeforeFallingBackToCancel()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/NativeVoiceInputService.cs");

        Assert.Contains(".StopAsync()", code);
        Assert.Contains(".CancelAsync()", code);
    }

    [Fact]
    public void NativeVoiceInputService_BoundsStopAndCancelWithTimeouts()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/NativeVoiceInputService.cs");

        Assert.Contains("VoiceInputStopTimeout", code);
        Assert.Contains("VoiceInputCancelTimeout", code);
        Assert.Contains(".WaitAsync(VoiceInputStopTimeout, cancellationToken)", code);
        Assert.Contains(".WaitAsync(VoiceInputCancelTimeout, cancellationToken)", code);
        Assert.Contains("StoppedByAppForcedEnd", code);
    }

    [Fact]
    public void NativeVoiceInputService_PerformsMicrophonePreflightOnUiThreadBeforeStartingRecognition()
    {
        var code = File.ReadAllText("../../../../../SalmonEgg/SalmonEgg/Presentation/Services/Input/NativeVoiceInputService.cs");

        Assert.Contains("MediaCaptureInitializationSettings", code);
        Assert.Contains("StreamingCaptureMode = StreamingCaptureMode.Audio", code);
        Assert.Contains("MediaCategory = MediaCategory.Speech", code);
        Assert.Contains("_uiDispatcher.EnqueueAsync", code);
        Assert.Contains("EnsureMicrophoneCaptureAccessAsync(options.RequestId, cancellationToken)", code);
    }
}
