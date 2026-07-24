using System.Collections.Generic;
using System.Collections.ObjectModel;
using SalmonEgg.Acp.Tool;
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
        Assert.Empty(vm.ToolCallDetailItems);
        Assert.Contains(nameof(ChatMessageViewModel.HasToolCallJson), changedProperties);
    }

    [Fact]
    public void MetadataOnlyToolCall_ShowsToolCallPillWithoutPayload()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-2",
            toolCallId: "call-2",
            rawInput: null,
            rawOutput: null,
            kind: SalmonEgg.Acp.Tool.ToolCallKind.Execute,
            status: SalmonEgg.Acp.Tool.ToolCallStatus.InProgress,
            title: "Running tests");

        Assert.False(vm.HasToolCallJson);
        Assert.True(vm.ShouldShowToolCallPill);
    }

    [Fact]
    public void CancelledToolCall_UsesDedicatedCancelledStateInsteadOfFailedState()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-3",
            toolCallId: "call-3",
            rawInput: null,
            rawOutput: null,
            kind: SalmonEgg.Acp.Tool.ToolCallKind.Execute,
            status: SalmonEgg.Acp.Tool.ToolCallStatus.Cancelled,
            title: "Running tests");

        Assert.True(vm.IsToolCallCancelled);
        Assert.False(vm.IsToolCallFailed);
    }

    [Fact]
    public void ToolCallLocationsChange_RaisesHasToolCallLocationsPropertyChanged()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-4",
            toolCallId: "call-4",
            rawInput: null,
            rawOutput: null,
            kind: ToolCallKind.Edit,
            status: ToolCallStatus.InProgress,
            title: "Editing file");

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        vm.ToolCallLocations =
        [
            new ToolCallLocation(@"C:\repo\demo.cs", 42)
        ];

        Assert.True(vm.HasToolCallLocations);
        Assert.Contains(nameof(ChatMessageViewModel.HasToolCallLocations), changedProperties);
    }

    [Fact]
    public void PendingPermissionRequestChange_EnablesInlinePermissionActions()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-5",
            toolCallId: "call-5",
            rawInput: null,
            rawOutput: null,
            kind: ToolCallKind.Read,
            status: ToolCallStatus.Pending,
            title: "Read file");

        vm.PendingPermissionRequest = new PermissionRequestViewModel();

        Assert.True(vm.HasPendingPermissionRequest);
    }

    [Fact]
    public void PermissionOptions_ProjectAllProtocolOptions()
    {
        var permissionRequest = new PermissionRequestViewModel
        {
            Options = new ObservableCollection<PermissionOptionViewModel>
            {
                new() { OptionId = "allow-once", Name = "Allow once", Kind = "allow_once" },
                new() { OptionId = "allow-always", Name = "Always allow", Kind = "allow_always" },
                new() { OptionId = "reject-once", Name = "Reject", Kind = "reject_once" }
            }
        };

        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-8",
            toolCallId: "call-8",
            rawInput: null,
            rawOutput: null,
            kind: ToolCallKind.Execute,
            status: ToolCallStatus.Pending,
            title: "Run command");

        vm.PendingPermissionRequest = permissionRequest;

        Assert.Equal(3, vm.PendingPermissionRequest.Options.Count);
        Assert.All(vm.PendingPermissionRequest.Options, option => Assert.False(string.IsNullOrWhiteSpace(option.OptionId)));
    }

    [Fact]
    public void ToolCallRawInput_SummaryShowsPrimaryTargetAndKeepsRawForTier2()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-6",
            toolCallId: "call-6",
            rawInput: "{\"path\":\"C:/repo/appsettings.json\",\"query\":\"Logging\",\"arguments\":{\"line\":12}}",
            rawOutput: null,
            kind: ToolCallKind.Read,
            status: ToolCallStatus.Completed,
            title: "Read configuration");

        // Tier 0: a single semantic summary (the action target), not flattened fields.
        Assert.Contains("C:/repo/appsettings.json", vm.ToolCallSummary);
        // Tier 1 artifacts only — no flattened raw input fields.
        Assert.Empty(vm.ToolCallDetailItems);
        // Tier 2: raw input retained verbatim for the "View raw" affordance.
        Assert.Equal("{\"path\":\"C:/repo/appsettings.json\",\"query\":\"Logging\",\"arguments\":{\"line\":12}}", vm.ToolCallRawInputJson);
    }

    public static TheoryData<ToolCallKind, string, string> PrimaryTargetByKindCases() => new()
    {
        { ToolCallKind.Read, "{\"path\":\"src/app.cs\"}", "src/app.cs" },
        { ToolCallKind.Search, "{\"query\":\"foo\"}", "foo" },
        { ToolCallKind.Execute, "{\"command\":\"dotnet\",\"arguments\":\"test\"}", "dotnet test" },
        { ToolCallKind.Fetch, "{\"url\":\"https://example.com\"}", "https://example.com" },
    };

    [Theory]
    [MemberData(nameof(PrimaryTargetByKindCases))]
    public void ToolCallSummary_PicksPrimaryTargetByKind(ToolCallKind kind, string rawInput, string expected)
    {
        var vm = ChatMessageViewModel.CreateFromToolCall("summary", "call", rawInput, null, kind, ToolCallStatus.InProgress, null);

        Assert.Equal(expected, vm.ToolCallSummary);
    }

    [Fact]
    public void ToolCallSummary_FallsBackToLocationWhenInputEmpty()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall("summary", "call", null, null, ToolCallKind.Edit, ToolCallStatus.InProgress, "Edit file");
        vm.ToolCallLocations = new[] { new ToolCallLocation("/repo/a.cs", 7) };

        Assert.Equal("/repo/a.cs:7", vm.ToolCallSummary);
    }

    [Fact]
    public void ToolCallRawInputAndRawOutput_RemainSeparateProtocolFields()
    {
        var vm = ChatMessageViewModel.CreateFromToolCall(
            id: "tool-7",
            toolCallId: "call-7",
            rawInput: "{\"command\":\"dotnet test\"}",
            rawOutput: "{\"exitCode\":0}",
            kind: ToolCallKind.Execute,
            status: ToolCallStatus.Completed,
            title: "Run tests");

        Assert.Equal("{\"command\":\"dotnet test\"}", vm.ToolCallRawInputJson);
        Assert.Equal("{\"exitCode\":0}", vm.ToolCallRawOutputJson);
        Assert.Equal("{\"command\":\"dotnet test\"}", vm.ToolCallJson);
    }
}
