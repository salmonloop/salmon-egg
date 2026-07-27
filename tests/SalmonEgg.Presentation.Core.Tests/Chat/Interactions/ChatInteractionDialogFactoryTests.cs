using System.Threading.Tasks;
using System.Collections.Generic;
using SalmonEgg.Acp.Protocol;
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
            new PermissionRequestEventArgs(
                "permission-1",
                "remote-1",
                toolCall: null,
                options:
                [
                    new PermissionOption("opt-1", "Option 1", "allow_once")
                ],
                respond: static (_, _) => Task.CompletedTask),
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
            new FileSystemRequestEventArgs(
                "fs-1",
                "remote-1",
                "fs/read_text_file",
                FileSystemRequestKind.ReadTextFile,
                "/tmp/file.txt",
                encoding: null,
                content: "abc",
                respond: static (_, _, _) => Task.CompletedTask),
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
