using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class NativeVoiceInputServiceSourceTests
{
    [Fact]
    public void NativeVoiceInputService_DoesNotReuseRecognizerAcrossVoiceSessions()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\NativeVoiceInputService.cs");

        Assert.Contains("CreateSessionRecognizerAsync", code);
        Assert.DoesNotContain("string.Equals(_activeLanguageTag, normalizedLanguage", code);
    }

    [Fact]
    public void NativeVoiceInputService_DisposesRecognizerWhenSessionCompletes()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\NativeVoiceInputService.cs");

        Assert.Contains("DisposeRecognizer();", code);
        Assert.Contains("finally", code);
    }

    [Fact]
    public void NativeVoiceInputService_UsesGracefulStopBeforeFallingBackToCancel()
    {
        var code = File.ReadAllText(@"..\..\..\..\..\SalmonEgg\SalmonEgg\Presentation\Services\Input\NativeVoiceInputService.cs");

        Assert.Contains("ContinuousRecognitionSession.StopAsync()", code);
        Assert.Contains("ContinuousRecognitionSession.CancelAsync()", code);
    }
}
