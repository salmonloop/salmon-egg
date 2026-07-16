using System.Threading.Tasks;
using System.Collections.Immutable;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using Xunit;
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Interactions;

public sealed class ChatInteractionDialogFactoryTests
{
    [Fact]
    public async Task CreatePermissionRequestViewModel_SuccessfulResponse_DismissesDialog()
    {
        var dismissed = false;
        var sut = ChatInteractionDialogFactory.CreatePermissionRequestViewModel(
            new PermissionRequestEventArgs
            {
                MessageId = "permission-1",
                SessionId = "remote-1",
                Options =
                [
                    new PermissionOption("opt-1", "Option 1", "allow_once")
                ]
            },
            (messageId, outcome, optionId) =>
            {
                Assert.Equal("permission-1", messageId);
                Assert.Equal("selected", outcome);
                Assert.Equal("opt-1", optionId);
                return Task.FromResult(true);
            },
            () => dismissed = true);

        Assert.Equal(string.Empty, sut.Options[0].Description);

        await sut.RespondCommand.ExecuteAsync(sut.Options[0]);

        Assert.True(dismissed);
    }

    [Fact]
    public async Task CreateFileSystemRequestViewModel_Response_DismissesDialog()
    {
        var dismissed = false;
        var sut = ChatInteractionDialogFactory.CreateFileSystemRequestViewModel(
            new FileSystemRequestEventArgs
            {
                MessageId = "fs-1",
                SessionId = "remote-1",
                Method = "fs/read_text_file",
                Kind = FileSystemRequestKind.ReadTextFile,
                Path = "/tmp/file.txt",
                Content = "abc"
            },
            (messageId, success, content, message) =>
            {
                Assert.Equal("fs-1", messageId);
                Assert.True(success);
                Assert.Equal("payload", content);
                Assert.Null(message);
                return Task.CompletedTask;
            },
            () => dismissed = true);

        Assert.Equal("fs/read_text_file", sut.Method);
        Assert.Equal(FileSystemRequestKind.ReadTextFile, sut.Kind);

        sut.ResponseContent = "payload";
        await sut.RespondCommand.ExecuteAsync(true);

        Assert.True(dismissed);
    }
}
