using System.Collections.Generic;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatMessageViewModelToolCallTests
{
    [Fact]
    public void ToolCallJsonChange_RaisesHasToolCallJsonPropertyChanged()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-1",
            toolCallId: "call-1",
            rawInput: null,
            rawOutput: null,
            kind: null,
            status: null,
            title: null);

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        vm.ToolCallJson = "{\"path\":\"/tmp/demo.txt\"}";

        Assert.True(vm.HasToolCallJson);
        Assert.Contains(nameof(ChatMessageViewModel.HasToolCallJson), changedProperties);
    }
}
