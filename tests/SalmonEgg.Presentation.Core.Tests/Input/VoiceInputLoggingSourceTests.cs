using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Input;

public sealed class VoiceInputLoggingSourceTests
{
    [Fact]
    public void ChatViewModel_DoesNotDuplicateFirstPartialLoggingOwnedByVoiceService()
    {
        var code = File.ReadAllText("../../../../../src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs");

        Assert.DoesNotContain("Voice input first partial received.", code);
    }
}
