using System;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatMessageViewModelTimestampTests
{
    [Fact]
    public void ApplySnapshot_WithNullTimestamp_HidesTimestamp()
    {
        var snapshot = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "replayed",
            Timestamp = null
        };

        var vm = new ChatMessageViewModel();
        vm.ApplySnapshot(snapshot, projectionIndex: 0);

        Assert.Null(vm.Timestamp);
        Assert.False(vm.HasTimestamp);
    }

    [Fact]
    public void ApplySnapshot_WithUtcTimestamp_ProjectsLocalTimeAndShowsTimestamp()
    {
        var utc = new DateTime(2026, 3, 1, 12, 30, 0, DateTimeKind.Utc);
        var snapshot = new ConversationMessageSnapshot
        {
            Id = "m-1",
            ContentType = "text",
            TextContent = "local-owned",
            Timestamp = utc
        };

        var vm = new ChatMessageViewModel();
        vm.ApplySnapshot(snapshot, projectionIndex: 0);

        Assert.Equal(utc.ToLocalTime(), vm.Timestamp);
        Assert.True(vm.HasTimestamp);
    }

    [Fact]
    public void CreateFromTextContent_DoesNotInventTimestamp()
    {
        var vm = ChatMessageViewModel.CreateFromTextContent(
            "m-1",
            new SalmonEgg.Acp.Content.TextContentBlock("hello"),
            isOutgoing: false);

        Assert.Null(vm.Timestamp);
        Assert.False(vm.HasTimestamp);
    }

    [Fact]
    public void CreateFromToolCall_DoesNotInventTimestamp()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-1",
            toolCallId: "call-1",
            rawInput: null,
            rawOutput: null,
            kind: null,
            status: null,
            title: null);

        Assert.Null(vm.Timestamp);
        Assert.False(vm.HasTimestamp);
    }

    [Fact]
    public void TimestampChange_RaisesHasTimestampPropertyChanged()
    {
        var vm = new ChatMessageViewModel();
        var changed = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        vm.Timestamp = DateTime.Now;

        Assert.True(vm.HasTimestamp);
        Assert.Contains(nameof(ChatMessageViewModel.Timestamp), changed);
        Assert.Contains(nameof(ChatMessageViewModel.HasTimestamp), changed);
    }
}
