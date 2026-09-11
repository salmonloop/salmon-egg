using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Observability;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpSessionProjectionTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("agent_message", "agent_message_chunk", AcpMessageKind.Agent)]
    [InlineData("user_message", "user_message_chunk", AcpMessageKind.User)]
    [InlineData("agent_thought", "agent_thought_chunk", AcpMessageKind.Thought)]
    public async Task SessionUpdate_MixedMessageUpsertsAndChunks_ReplacesThenAppends(
        string wholeKind, string chunkKind, AcpMessageKind kind)
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Message(chunkKind, "m", "first"));
        peer.Update("one", Whole(wholeKind, "m", "replacement"));
        var replaced = peer.Client.GetSessionSnapshot("one")!;

        // Act
        peer.Update("one", Message(chunkKind, "m", "tail"));
        peer.Update("one", "{\"sessionUpdate\":\"" + wholeKind + "\",\"messageId\":\"m\"}");

        // Assert
        var message = Assert.Single(peer.Client.GetSessionSnapshot("one")!.Messages);
        Assert.Equal(kind, message.Kind);
        Assert.Equal(["replacement", "tail"], Texts(message));
        Assert.Equal(["replacement"], Texts(Assert.Single(replaced.Messages)));
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("27")]
    [InlineData("\"broken\"")]
    public void ReplaySession_MessageContentClearOrDefault_ClearsPreviousChunks(string value)
    {
        // Arrange
        var updates = new[]
        {
            Json(Message("agent_message_chunk", "m", "previous")),
            Json("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m\",\"content\":" + value + "}")
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", updates);

        // Assert
        Assert.Empty(Assert.Single(snapshot.Messages).Content);
    }

    [Fact]
    public void ReplaySession_MessageMetadata_UpsertsOnlyAtMessageScope()
    {
        // Arrange
        var initial = Json("""{"sessionUpdate":"agent_message","messageId":"m","_meta":{"message":"owned"}}""");
        var chunk = Json("""{"sessionUpdate":"agent_message_chunk","messageId":"m","content":{"type":"text","text":"hi"},"_meta":{"chunk":"only"}}""");

        // Act
        var beforeClear = AcpSessionDraftExtensions.ReplaySession("one", [initial, chunk]);
        var afterClear = AcpSessionDraftExtensions.ReplaySession("one", [initial, chunk,
            Json("""{"sessionUpdate":"agent_message","messageId":"m","_meta":null}""")]);

        // Assert
        Assert.Equal("owned", Assert.Single(beforeClear.Messages).Meta!.Value.GetProperty("message").GetString());
        Assert.False(beforeClear.Messages[0].Meta!.Value.TryGetProperty("chunk", out _));
        Assert.Null(Assert.Single(afterClear.Messages).Meta);
        Assert.Equal(["hi"], Texts(afterClear.Messages[0]));
    }

    [Fact]
    public void ReplaySession_NewMessageWithoutContent_UsesEmptyDefaultAndFirstSeenOrder()
    {
        // Arrange
        var updates = new[]
        {
            Json("""{"sessionUpdate":"user_message","messageId":"second"}"""),
            Json(Whole("agent_message", "first", "one")),
            Json(Whole("user_message", "second", "two"))
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", updates);

        // Assert
        Assert.Equal(["second", "first"], snapshot.Messages.Select(static m => m.MessageId));
        Assert.Equal(["two"], Texts(snapshot.Messages[0]));
    }

    [Fact]
    public async Task SessionUpdate_ToolUpsert_ReplacesCollectionsAndPatchesOnlyPresentFields()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","title":"Read file","kind":"read","status":"in_progress","content":[{"type":"content","content":{"type":"text","text":"old"}}],"locations":[{"path":"/workspace/old"}],"rawInput":{"path":"one"},"rawOutput":{"value":1},"_meta":{"tool":"owned"}}""");
        var before = peer.Client.GetSessionSnapshot("one")!;

        // Act
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","title":null,"status":"completed","content":[{"type":"content","content":{"type":"text","text":"new"}}],"locations":[],"rawOutput":null}""");
        peer.Update("one", """{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"content","content":{"type":"text","text":"tail"}},"_meta":{"chunk":true}}""");

        // Assert
        var tool = Assert.Single(peer.Client.GetSessionSnapshot("one")!.ToolCalls);
        Assert.Null(tool.Title);
        Assert.Equal(ToolCallKind.Read, tool.Kind);
        Assert.Equal(ToolCallStatus.Completed, tool.Status);
        Assert.Equal(["new", "tail"], ToolTexts(tool));
        Assert.Empty(tool.Locations);
        Assert.Equal("one", tool.RawInput!.Value.GetProperty("path").GetString());
        Assert.Null(tool.RawOutput);
        Assert.Equal("owned", tool.Meta!.Value.GetProperty("tool").GetString());
        Assert.Equal(["old"], ToolTexts(before.ToolCalls[0]));
    }

    [Fact]
    public void ReplaySession_ToolPatchNulls_ClearAllOptionalState()
    {
        // Arrange
        var initial = Json("""{"sessionUpdate":"tool_call_update","toolCallId":"t","title":"title","kind":"read","status":"pending","content":[{"type":"terminal","terminalId":"x"}],"locations":[{"path":"/x"}],"rawInput":{},"rawOutput":{},"_meta":{}}""");
        var clear = Json("""{"sessionUpdate":"tool_call_update","toolCallId":"t","title":null,"kind":null,"status":null,"content":null,"locations":null,"rawInput":null,"rawOutput":null,"_meta":null}""");

        // Act
        var tool = Assert.Single(AcpSessionDraftExtensions.ReplaySession("one", [initial, clear]).ToolCalls);

        // Assert
        Assert.Null(tool.Title);
        Assert.Null(tool.Kind);
        Assert.Null(tool.Status);
        Assert.Empty(tool.Content);
        Assert.Empty(tool.Locations);
        Assert.Null(tool.RawInput);
        Assert.Null(tool.RawOutput);
        Assert.Null(tool.Meta);
    }

    [Fact]
    public void ReplaySession_ToolChunkBeforeUpsert_CreatesUnknownStateThenPatches()
    {
        // Arrange
        var chunk = Json("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"terminal","terminalId":"term"}}""");

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [chunk,
            Json("""{"sessionUpdate":"tool_call_update","toolCallId":"t","status":"future_status","kind":"future_kind"}""")]);

        // Assert
        var tool = Assert.Single(snapshot.ToolCalls);
        Assert.Null(tool.Title);
        Assert.Equal("future_status", tool.Status!.Value.Value);
        Assert.Equal("future_kind", tool.Kind!.Value.Value);
        Assert.Equal("term", Assert.IsType<TerminalToolCallContent>(Assert.Single(tool.Content)).TerminalId);
    }

    [Fact]
    public void ReplaySession_DefaultablePatchAndCollections_RecoversOnlyInvalidItems()
    {
        // Arrange
        var updates = new[]
        {
            Json("""{"sessionUpdate":"agent_message","messageId":"m","content":[17,{"type":"text","text":"kept"},{"type":"_future","payload":{"x":1}}],"_meta":"bad"}"""),
            Json("""{"sessionUpdate":"tool_call_update","toolCallId":"t","title":5,"kind":{},"status":[],"content":[false,{"type":"terminal","terminalId":"term"}],"locations":[{"line":4},{"path":"/valid","line":"bad"}],"_meta":3}""")
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", updates);

        // Assert
        Assert.Equal(2, snapshot.Messages[0].Content.Length);
        Assert.Null(snapshot.Messages[0].Meta);
        Assert.Null(snapshot.ToolCalls[0].Title);
        Assert.Null(snapshot.ToolCalls[0].Kind);
        Assert.Null(snapshot.ToolCalls[0].Status);
        Assert.Single(snapshot.ToolCalls[0].Content);
        var location = Assert.Single(snapshot.ToolCalls[0].Locations);
        Assert.Equal("/valid", location.Path);
        Assert.Null(location.Line);
    }

    [Fact]
    public async Task SessionUpdate_TerminalSnapshotsAndChunks_DecodesEachChunkAndReplacesOutput()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", """{"sessionUpdate":"terminal_output_chunk","terminalId":"t","data":"YQ=="}""");
        peer.Update("one", """{"sessionUpdate":"terminal_output_chunk","terminalId":"t","data":"YmM="}""");
        var before = peer.Client.GetSessionSnapshot("one")!;

        // Act
        peer.Update("one", """{"sessionUpdate":"terminal_update","terminalId":"t","command":"echo","cwd":"/workspace","output":{"data":"eA==","_meta":{"snapshot":true}},"exitStatus":{},"_meta":{"terminal":true}}""");
        peer.Update("one", """{"sessionUpdate":"terminal_output_chunk","terminalId":"t","data":"eXo=","_meta":{"chunk":true}}""");
        peer.Update("one", """{"sessionUpdate":"terminal_update","terminalId":"t"}""");

        // Assert
        var terminal = Assert.Single(peer.Client.GetSessionSnapshot("one")!.Terminals);
        Assert.Equal("abc", Encoding.UTF8.GetString(before.Terminals[0].Output.AsSpan()));
        Assert.Equal("xyz", Encoding.UTF8.GetString(terminal.Output.AsSpan()));
        Assert.Equal("echo", terminal.Command);
        Assert.Equal("/workspace", terminal.Cwd);
        Assert.True(terminal.HasExited);
        Assert.Null(terminal.ExitStatus!.ExitCode);
        Assert.True(terminal.OutputMeta!.Value.GetProperty("snapshot").GetBoolean());
        Assert.False(terminal.Meta!.Value.TryGetProperty("chunk", out _));
        Assert.DoesNotContain(peer.Sent, static request => request.Method.StartsWith("terminal/", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaySession_TerminalExplicitNulls_ClearStateAndExitFlag()
    {
        // Arrange
        var initial = Json("""{"sessionUpdate":"terminal_update","terminalId":"t","command":"cmd","cwd":"/x","output":{"data":"eA=="},"exitStatus":{"exitCode":0},"_meta":{}}""");
        var clear = Json("""{"sessionUpdate":"terminal_update","terminalId":"t","command":null,"cwd":null,"output":null,"exitStatus":null,"_meta":null}""");

        // Act
        var terminal = Assert.Single(AcpSessionDraftExtensions.ReplaySession("one", [initial, clear]).Terminals);

        // Assert
        Assert.Null(terminal.Command);
        Assert.Null(terminal.Cwd);
        Assert.Empty(terminal.Output);
        Assert.False(terminal.HasExited);
        Assert.Null(terminal.ExitStatus);
        Assert.Null(terminal.Meta);
    }

    [Theory]
    [InlineData("17")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":\"!invalid!\"}")]
    public void ReplaySession_MalformedOptionalTerminalOutput_DefaultsToClear(string output)
    {
        // Arrange
        var updates = new[]
        {
            Json("""{"sessionUpdate":"terminal_output_chunk","terminalId":"t","data":"eA=="}"""),
            Json("{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t\",\"command\":\"kept\",\"output\":" + output + "}")
        };

        // Act
        var terminal = Assert.Single(AcpSessionDraftExtensions.ReplaySession("one", updates).Terminals);

        // Assert
        Assert.Equal("kept", terminal.Command);
        Assert.Empty(terminal.Output);
    }

    [Fact]
    public void ReplaySession_DefaultableTerminalExitFields_RetainsExitedObject()
    {
        // Arrange
        var update = Json("""{"sessionUpdate":"terminal_update","terminalId":"t","command":1,"cwd":false,"exitStatus":{"exitCode":-1,"signal":2,"_meta":3}}""");

        // Act
        var terminal = Assert.Single(AcpSessionDraftExtensions.ReplaySession("one", [update]).Terminals);

        // Assert
        Assert.True(terminal.HasExited);
        Assert.Null(terminal.Command);
        Assert.Null(terminal.Cwd);
        Assert.Null(terminal.ExitStatus!.ExitCode);
        Assert.Null(terminal.ExitStatus.Signal);
        Assert.Null(terminal.ExitStatus.Meta);
    }

    [Fact]
    public async Task SessionUpdate_MalformedTerminalChunk_DoesNotMutateProjectionOrRaiseUpdate()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        var received = 0;
        peer.Client.SessionUpdateReceived += (_, _) => received++;

        // Act
        peer.Update("one", """{"sessionUpdate":"terminal_output_chunk","terminalId":"bad","data":"bad!"}""");

        // Assert
        Assert.Equal(0, received);
        Assert.Empty(peer.Client.GetSessionSnapshot("one")!.Terminals);
        Assert.Single(peer.Errors);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sessionUpdate\":1}")]
    [InlineData("{\"sessionUpdate\":\"agent_message\"}")]
    [InlineData("{\"sessionUpdate\":\"agent_message\",\"messageId\":null}")]
    [InlineData("{\"sessionUpdate\":\"agent_message_chunk\",\"messageId\":\"m\"}")]
    [InlineData("{\"sessionUpdate\":\"agent_message_chunk\",\"messageId\":\"m\",\"content\":null}")]
    [InlineData("{\"sessionUpdate\":\"tool_call_update\"}")]
    [InlineData("{\"sessionUpdate\":\"tool_call_content_chunk\",\"toolCallId\":\"t\",\"content\":null}")]
    [InlineData("{\"sessionUpdate\":\"terminal_update\",\"terminalId\":null}")]
    [InlineData("{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t\"}")]
    [InlineData("{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t\",\"data\":null}")]
    public void ReplaySession_InvalidRequiredFields_RejectsInsteadOfInventingIdentity(string json)
    {
        // Arrange
        var update = Json(json);

        // Act / Assert
        Assert.Throws<JsonException>(() => AcpSessionDraftExtensions.ReplaySession("one", [update]));
    }

    [Fact]
    public void ReplaySession_EmptyStringIds_AreNotStricterThanSchema()
    {
        // Arrange
        var updates = new[]
        {
            Json(Whole("agent_message", "", "text")),
            Json("""{"sessionUpdate":"tool_call_update","toolCallId":""}"""),
            Json("""{"sessionUpdate":"terminal_update","terminalId":""}""")
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("", updates);

        // Assert
        Assert.Equal("", Assert.Single(snapshot.Messages).MessageId);
        Assert.Equal("", Assert.Single(snapshot.ToolCalls).ToolCallId);
        Assert.Equal("", Assert.Single(snapshot.Terminals).TerminalId);
    }

    [Fact]
    public void ReplaySession_UnknownPayloads_PreservesRawDataWithoutTreatingThemAsKnown()
    {
        // Arrange
        const string unknown = """{"sessionUpdate":"future_update", "payload":1e2,"private":{"x":1}}""";
        var updates = new[]
        {
            Json(unknown),
            Json("""{"sessionUpdate":"agent_message","messageId":"m","content":[{"type":"_vendor","answer":[1,2]}]}"""),
            Json("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"_vendor_tool","answer":{"a":1}}}""")
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", updates);

        // Assert
        Assert.Equal(unknown, Assert.Single(snapshot.UnprojectedUpdates).GetRawText());
        var toolContent = Assert.IsType<CustomToolCallContent>(Assert.Single(snapshot.ToolCalls[0].Content));
        Assert.Equal(1, toolContent.RawPayload.GetProperty("answer").GetProperty("a").GetInt32());
        Assert.Equal(2, snapshot.Messages[0].Content[0].RawPayload!.Value.GetProperty("answer").GetArrayLength());
    }

    [Fact]
    public async Task SessionUpdate_UnprojectedCountLimit_RetainsRecentValuesWithoutSuppressingEvents()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        var received = new List<JsonElement>();
        peer.Client.SessionUpdateReceived += (_, args) =>
        {
            if (args.Update is { UnknownUpdateKind: not null } update)
            {
                received.Add(update.ExtensionData!["sequence"]);
            }
        };
        var history = Enumerable.Range(0, AcpSessionSnapshot.MaxUnprojectedUpdates + 2)
            .Select(static index => Json("{\"sessionUpdate\":\"_vendor\",\"sequence\":" + index + "}"))
            .ToArray();
        foreach (var update in history.Take(AcpSessionSnapshot.MaxUnprojectedUpdates))
        {
            peer.Update("one", update.GetRawText());
        }
        var before = peer.Client.GetSessionSnapshot("one")!;

        // Act
        foreach (var update in history.Skip(AcpSessionSnapshot.MaxUnprojectedUpdates))
        {
            peer.Update("one", update.GetRawText());
        }
        peer.Update("one", Whole("agent_message", "m", "unaffected"));
        var after = peer.Client.GetSessionSnapshot("one")!;
        var replay = AcpSessionDraftExtensions.ReplaySession("one", history);

        // Assert
        Assert.Equal(AcpSessionSnapshot.MaxUnprojectedUpdates, after.UnprojectedUpdates.Length);
        Assert.Equal(2, after.OmittedUnprojectedUpdateCount);
        Assert.Equal(history.Skip(2).Select(static value => value.GetRawText()),
            after.UnprojectedUpdates.Select(static value => value.GetRawText()));
        Assert.Equal(after.UnprojectedUpdates.Select(static value => value.GetRawText()),
            replay.UnprojectedUpdates.Select(static value => value.GetRawText()));
        Assert.Equal(after.OmittedUnprojectedUpdateCount, replay.OmittedUnprojectedUpdateCount);
        Assert.Equal(history.Length, received.Count);
        Assert.Equal(0, before.OmittedUnprojectedUpdateCount);
        Assert.Equal(0, before.UnprojectedUpdates[0].GetProperty("sequence").GetInt32());
        Assert.Equal(["unaffected"], Texts(Assert.Single(after.Messages)));
    }

    [Fact]
    public void ReplaySession_UnprojectedByteLimit_UsesUtf8SizeAndEvictsOldestFirst()
    {
        // Arrange
        var update = Json("{\"sessionUpdate\":\"_vendor\",\"text\":\""
            + new string('界', AcpSessionSnapshot.MaxUnprojectedUtf8Bytes / 6) + "\"}");
        Assert.True(update.GetRawText().Length * 2 < AcpSessionSnapshot.MaxUnprojectedUtf8Bytes);
        Assert.True(Encoding.UTF8.GetByteCount(update.GetRawText()) * 2 > AcpSessionSnapshot.MaxUnprojectedUtf8Bytes);

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [update, update]);

        // Assert
        Assert.Equal(update.GetRawText(), Assert.Single(snapshot.UnprojectedUpdates).GetRawText());
        Assert.Equal(1, snapshot.OmittedUnprojectedUpdateCount);
    }

    [Fact]
    public void ReplaySession_OversizedUnprojectedUpdate_RecordsOmissionWithoutEvictingFittingHistory()
    {
        // Arrange
        var first = Json("""{"sessionUpdate":"_first","value":1e2}""");
        var oversized = Json("{\"sessionUpdate\":\"_large\",\"text\":\""
            + new string('x', AcpSessionSnapshot.MaxUnprojectedUtf8Bytes) + "\"}");
        var last = Json("""{"sessionUpdate":"_last","value":true}""");

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [first, oversized, last]);

        // Assert
        Assert.Equal([first.GetRawText(), last.GetRawText()],
            snapshot.UnprojectedUpdates.Select(static value => value.GetRawText()));
        Assert.Equal(1, snapshot.OmittedUnprojectedUpdateCount);
    }

    [Fact]
    public void ReplaySession_UnprojectedUpdateAtByteLimit_RetainsItVerbatim()
    {
        // Arrange
        const string prefix = "{\"sessionUpdate\":\"_vendor\",\"text\":\"";
        const string suffix = "\"}";
        var payload = prefix + new string('x', AcpSessionSnapshot.MaxUnprojectedUtf8Bytes - prefix.Length - suffix.Length) + suffix;

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", [Json(payload)]);

        // Assert
        Assert.Equal(payload, Assert.Single(snapshot.UnprojectedUpdates).GetRawText());
        Assert.Equal(0, snapshot.OmittedUnprojectedUpdateCount);
    }

    [Fact]
    public async Task SessionUpdate_EntityExtensions_KeepLastSeenValuesWithoutAbsorbingChunkFields()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", """{"sessionUpdate":"agent_message","messageId":"m","future":{"value":1e2},"kept":true}""");
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","future":{"value":1e2},"kept":true}""");
        peer.Update("one", """{"sessionUpdate":"terminal_update","terminalId":"term","future":{"value":1e2},"kept":true}""");
        var before = peer.Client.GetSessionSnapshot("one")!;

        // Act
        peer.Update("one", """{"sessionUpdate":"agent_message","messageId":"m","future":null}""");
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","future":null}""");
        peer.Update("one", """{"sessionUpdate":"terminal_update","terminalId":"term","future":null}""");
        peer.Update("one", """{"sessionUpdate":"agent_message_chunk","messageId":"m","content":{"type":"text","text":"tail"},"future":"chunk-only"}""");
        peer.Update("one", """{"sessionUpdate":"tool_call_content_chunk","toolCallId":"t","content":{"type":"terminal","terminalId":"term"},"future":"chunk-only"}""");
        peer.Update("one", """{"sessionUpdate":"terminal_output_chunk","terminalId":"term","data":"eA==","future":"chunk-only"}""");
        var after = peer.Client.GetSessionSnapshot("one")!;

        // Assert
        foreach (var extensions in new[] { before.Messages[0].ExtensionData, before.ToolCalls[0].ExtensionData, before.Terminals[0].ExtensionData })
        {
            Assert.Equal("1e2", extensions["future"].GetProperty("value").GetRawText());
        }
        foreach (var extensions in new[] { after.Messages[0].ExtensionData, after.ToolCalls[0].ExtensionData, after.Terminals[0].ExtensionData })
        {
            Assert.Equal(JsonValueKind.Null, extensions["future"].ValueKind);
            Assert.True(extensions["kept"].GetBoolean());
            Assert.Equal(2, extensions.Count);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("_future")]
    public void ReplaySession_UnknownContentDiscriminator_DoesNotInvokeStricterWriter(string type)
    {
        // Arrange
        var content = "{\"type\":\"" + type + "\",\"opaque\":1e2}";
        var update = Json("{\"sessionUpdate\":\"agent_message\",\"messageId\":\"m\",\"content\":[" + content + "]}");

        // Act
        var message = Assert.Single(AcpSessionDraftExtensions.ReplaySession("one", [update]).Messages);

        // Assert
        Assert.Equal(content, Assert.Single(message.Content).RawPayload!.Value.GetRawText());
    }

    [Fact]
    public async Task SessionUpdate_MutatingEventAndSnapshotDtos_CannotChangeStoredProjection()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Client.SessionUpdateReceived += (_, args) =>
        {
            if (args.Update is AgentWholeMessageUpdate update)
            {
                update.Content!.Clear();
            }
        };
        peer.Update("one", """{"sessionUpdate":"agent_message","messageId":"m","content":[{"type":"text","text":"kept","_meta":{"a":1}}]}""");
        var snapshot = peer.Client.GetSessionSnapshot("one")!;

        // Act
        snapshot.Messages[0].Content[0].Meta!["a"] = "mutated";
        peer.Update("one", Message("agent_message_chunk", "m", "later"));

        // Assert
        Assert.Equal(["kept"], Texts(snapshot.Messages[0]));
        Assert.Equal(1, Assert.IsType<JsonElement>(snapshot.Messages[0].Content[0].Meta!["a"]).GetInt32());
        Assert.Equal(["kept", "later"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
    }

    [Fact]
    public async Task SessionUpdate_ObserverReadsSnapshot_SeesTheCurrentUpdate()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        AcpSessionSnapshot? observed = null;
        peer.Client.SessionUpdateReceived += (_, _) => observed = peer.Client.GetSessionSnapshot("one");

        // Act
        peer.Update("one", Whole("user_message", "m", "current"));

        // Assert
        Assert.Equal(["current"], Texts(Assert.Single(observed!.Messages)));
    }

    [Fact]
    public async Task SessionUpdate_SameIdsAcrossSessions_RemainIsolated()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();

        // Act
        peer.Update("one", Whole("agent_message", "m", "first"));
        peer.Update("two", Whole("agent_message", "m", "second"));
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","title":"one"}""");
        peer.Update("two", """{"sessionUpdate":"tool_call_update","toolCallId":"t","title":"two"}""");

        // Assert
        Assert.Equal(["first"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
        Assert.Equal(["second"], Texts(peer.Client.GetSessionSnapshot("two")!.Messages[0]));
        Assert.Equal("one", peer.Client.GetSessionSnapshot("one")!.ToolCalls[0].Title);
        Assert.Equal("two", peer.Client.GetSessionSnapshot("two")!.ToolCalls[0].Title);
    }

    [Fact]
    public async Task ResumeSessionAsync_FullReplay_ReplacesPriorHistoryBeforeFirstReplayedChunk()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "removed", "old"));
        peer.Update("one", Message("agent_message_chunk", "m", "old duplicate"));
        peer.Update("one", "{\"sessionUpdate\":\"_large\",\"text\":\""
            + new string('x', AcpSessionSnapshot.MaxUnprojectedUtf8Bytes) + "\"}");
        Assert.Equal(1, peer.Client.GetSessionSnapshot("one")!.OmittedUnprojectedUpdateCount);
        peer.OnResume = _ => peer.Update("one", Message("agent_message_chunk", "m", "replayed"));

        // Act
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), TestToken);

        // Assert
        var snapshot = peer.Client.GetSessionSnapshot("one")!;
        Assert.Equal("m", Assert.Single(snapshot.Messages).MessageId);
        Assert.Equal(["replayed"], Texts(snapshot.Messages[0]));
        Assert.Empty(snapshot.UnprojectedUpdates);
        Assert.Equal(0, snapshot.OmittedUnprojectedUpdateCount);
    }

    [Fact]
    public async Task ResumeSessionAsync_WithoutReplay_KeepsPriorHistory()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "kept"));

        // Act
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", []), TestToken);

        // Assert
        Assert.Equal(["kept"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
    }

    [Fact]
    public async Task CreateSessionAsync_EmptyHistory_ExposesAnEmptyTrackedSnapshot()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();

        // Act
        var created = await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);

        // Assert
        var snapshot = peer.Client.GetSessionSnapshot(created.SessionId);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Messages);
        Assert.Empty(snapshot.ToolCalls);
        Assert.Empty(snapshot.Terminals);
        Assert.Null(snapshot.WorkState);
    }

    [Fact]
    public async Task ResumeSessionAsync_CustomRawCursor_DoesNotPretendTypedStartWasSent()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "kept"));
        var cursor = SessionReplayFrom.Start with { RawPayload = Json("""{"type":"_vendor_cursor","position":7}""") };

        // Act
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [], replayFrom: cursor), TestToken);

        // Assert
        Assert.Equal(["kept"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
        var sent = Assert.Single(peer.Sent, static request => request.Method == "session/resume");
        Assert.Equal("_vendor_cursor", sent.Params!.Value.GetProperty("replayFrom").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResumeSessionAsync_ClosedSession_ReplayIsAcceptedBeforeResumeResponse()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "old", "removed"));
        await peer.Client.CloseSessionAsync(new SessionCloseParams("one"), TestToken);
        peer.OnResume = _ => peer.Update("one", Whole("agent_message", "new", "restored"));

        // Act
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), TestToken);

        // Assert
        Assert.Equal(["restored"], Texts(Assert.Single(peer.Client.GetSessionSnapshot("one")!.Messages)));
    }

    [Fact]
    public async Task ResumeSessionAsync_AlreadyCancelled_DoesNotClearHistoryOrSend()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "kept"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer.Client.ResumeSessionAsync(
            new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), cancelled.Token));
        Assert.Equal(["kept"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
        Assert.DoesNotContain(peer.Sent, static request => request.Method == "session/resume");
    }

    [Fact]
    public async Task ResumeSessionAsync_CancelledWait_PreventsInterleavedReplayUntilPeerResponse()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.HoldResume = true;
        using var caller = new CancellationTokenSource();
        var first = peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), caller.Token);
        peer.Update("one", Message("user_message_chunk", "m", "replay"));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => peer.Client.ResumeSessionAsync(
            new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), TestToken));
        Assert.Equal(1, peer.Sent.Count(static request => request.Method == "session/resume"));
        Assert.Equal(["replay"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
        peer.ReplyResume();
        peer.HoldResume = false;
        await peer.Client.ResumeSessionAsync(new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), TestToken);
        Assert.Empty(peer.Client.GetSessionSnapshot("one")!.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeSessionAsync_UnknownWriteOutcome_RetainsReplayUntilPeerResponse(bool throwOnWrite)
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.HoldResume = true;
        peer.ResumeWrite = () => throwOnWrite
            ? Task.FromException<bool>(new IOException("The replay write outcome is unknown."))
            : Task.FromResult(false);
        var request = new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start);

        // Act
        var failure = await Record.ExceptionAsync(() => peer.Client.ResumeSessionAsync(request, TestToken));
        peer.Update("one", Message("agent_message_chunk", "m", "first replay"));
        var overlapping = await Record.ExceptionAsync(() => peer.Client.ResumeSessionAsync(request, TestToken));

        // Assert
        if (throwOnWrite) Assert.IsType<IOException>(failure);
        else Assert.IsType<InvalidOperationException>(failure);
        Assert.IsType<InvalidOperationException>(overlapping);
        Assert.Single(peer.Sent, static frame => frame.Method == "session/resume");
        Assert.Equal(["first replay"], Texts(Assert.Single(peer.Client.GetSessionSnapshot("one")!.Messages)));

        peer.ReplyResume();
        peer.ResumeWrite = null;
        peer.HoldResume = false;
        peer.OnResume = _ => peer.Update("one", Message("agent_message_chunk", "m", "next replay"));
        await peer.Client.ResumeSessionAsync(request, TestToken);
        Assert.Equal(2, peer.Sent.Count(static frame => frame.Method == "session/resume"));
        Assert.Equal(["next replay"], Texts(Assert.Single(peer.Client.GetSessionSnapshot("one")!.Messages)));
    }

    [Fact]
    public async Task ResumeSessionAsync_PreparationFailure_DoesNotClearHistoryOrClaimReplay()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "kept"));
        using var trace = new Activity("replay-preparation-failure").Start();
        var failure = new InvalidOperationException("The tracing subscriber rejected this request.");
        var request = new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start);

        // Act
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AcpActivitySources.ClientName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                if (options.Parent.TraceId == trace.TraceId && options.Name == "acp.request session/resume") throw failure;
                return ActivitySamplingResult.None;
            }
        })
        {
            ActivitySource.AddActivityListener(listener);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
                peer.Client.ResumeSessionAsync(request, TestToken)));
        }

        // Assert
        Assert.DoesNotContain(peer.Sent, static frame => frame.Method == "session/resume");
        Assert.Equal(["kept"], Texts(Assert.Single(peer.Client.GetSessionSnapshot("one")!.Messages)));
        await peer.Client.ResumeSessionAsync(request, TestToken);
        Assert.Single(peer.Sent, static frame => frame.Method == "session/resume");
        Assert.Empty(peer.Client.GetSessionSnapshot("one")!.Messages);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("delete")]
    public async Task SessionRemoval_LateUpdate_DoesNotResurrectProjection(string operation)
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "removed"));

        // Act
        if (operation == "close") await peer.Client.CloseSessionAsync(new SessionCloseParams("one"), TestToken);
        else await peer.Client.DeleteSessionAsync(new SessionDeleteParams("one"), TestToken);
        peer.Update("one", Whole("agent_message", "late", "rejected"));

        // Assert
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
    }

    [Fact]
    public async Task SessionUpdate_DisconnectAndQueuedOldCallback_CannotRepopulateNewConnection()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "old"));
        var oldDelivery = peer.CaptureDelivery();
        await peer.Client.DisconnectAsync();
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
        await peer.InitializeAsync();
        peer.Update("one", Whole("agent_message", "m", "new"));

        // Act
        oldDelivery(ProjectionPeer.UpdateFrame("one", Whole("agent_message", "m", "stale")));

        // Assert
        Assert.Equal(["new"], Texts(peer.Client.GetSessionSnapshot("one")!.Messages[0]));
    }

    [Fact]
    public async Task SessionUpdate_ConnectionDrop_HidesProjectionImmediately()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "m", "old"));

        // Act
        peer.DropConnection();

        // Assert
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
    }

    [Fact]
    public async Task ResumeSessionAsync_DelayedOldConnectionWrite_CannotEnterReplacementConnection()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        peer.Update("one", Whole("agent_message", "old", "old"));
        var blockedWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.BeforeResumeWrite = () => blockedWrite.Task;
        var oldResume = peer.Client.ResumeSessionAsync(
            new SessionResumeParams("one", "/workspace", [], replayFrom: SessionReplayFrom.Start), TestToken);
        await peer.Client.DisconnectAsync();
        await peer.InitializeAsync();
        peer.Update("one", Whole("agent_message", "new", "replacement"));

        // Act
        blockedWrite.SetResult(true);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldResume.WaitAsync(TestToken));
        Assert.DoesNotContain(peer.Sent, static request => request.Method == "session/resume");
        var current = peer.Client.GetSessionSnapshot("one")!;
        Assert.Equal("new", Assert.Single(current.Messages).MessageId);
        Assert.Equal(["replacement"], Texts(current.Messages[0]));
    }

    [Fact]
    public async Task CancelSessionAsync_TrailingUpdatesUntilIdle_RemainProjected()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        await peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        peer.Update("one", """{"sessionUpdate":"state_update","state":"running"}""");
        var cancel = peer.Client.CancelSessionAsync(new SessionCancelParams("one"), TestToken);

        // Act
        peer.Update("one", Message("agent_message_chunk", "m", "trailing"));
        Assert.False(cancel.IsCompleted);
        peer.Update("one", """{"sessionUpdate":"state_update","state":"idle","stopReason":"cancelled"}""");
        await cancel.WaitAsync(TestToken);

        // Assert
        var snapshot = peer.Client.GetSessionSnapshot("one")!;
        Assert.Equal(["trailing"], Texts(snapshot.Messages[0]));
        Assert.Equal(StopReason.Cancelled, Assert.IsType<IdleSessionWorkState>(snapshot.WorkState).StopReason);
    }

    [Fact]
    public async Task SessionUpdate_V1Connection_RemainsPassthroughWithoutDraftProjection()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync(AcpProtocolVersion.V1);
        var received = new List<SessionUpdate>();
        peer.Client.SessionUpdateReceived += (_, args) => received.Add(Assert.IsAssignableFrom<SessionUpdate>(args.Update));

        // Act
        peer.Update("one", Whole("agent_message", "m", "future"));
        peer.Update("one", """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"stable"}}""");

        // Assert
        Assert.Null(peer.Client.GetSessionSnapshot("one"));
        Assert.Equal("agent_message", Assert.IsType<SessionUpdate>(received[0]).UnknownUpdateKind);
        Assert.Equal("stable", Assert.IsType<TextContentBlock>(Assert.IsType<AgentMessageUpdate>(received[1]).Content).Text);
    }

    [Fact]
    public async Task SessionUpdate_V1InvalidOptionalToolField_StillFailsExistingTypeContract()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync(AcpProtocolVersion.V1);
        var received = false;
        peer.Client.SessionUpdateReceived += (_, _) => received = true;

        // Act
        peer.Update("one", """{"sessionUpdate":"tool_call_update","toolCallId":"t","title":42}""");

        // Assert
        Assert.False(received);
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task ReplaySession_MixedHistory_MatchesNormalHandlerSnapshots()
    {
        // Arrange
        using var peer = await ProjectionPeer.CreateAsync();
        var history = new[]
        {
            Json(Whole("user_message", "user", "prompt")),
            Json(Message("agent_message_chunk", "agent", "first")),
            Json(Whole("agent_message", "agent", "replacement")),
            Json("""{"sessionUpdate":"tool_call_update","toolCallId":"tool","status":"in_progress"}"""),
            Json("""{"sessionUpdate":"tool_call_content_chunk","toolCallId":"tool","content":{"type":"terminal","terminalId":"term"}}"""),
            Json("""{"sessionUpdate":"terminal_output_chunk","terminalId":"term","data":"aGk="}"""),
            Json("""{"sessionUpdate":"state_update","state":"idle","stopReason":"end_turn"}""")
        };

        // Act
        foreach (var update in history) peer.Update("one", update.GetRawText());
        var replay = AcpSessionDraftExtensions.ReplaySession("one", history);
        var live = peer.Client.GetSessionSnapshot("one")!;

        // Assert
        Assert.Equal(live.Messages.SelectMany(Texts), replay.Messages.SelectMany(Texts));
        Assert.Equal(live.ToolCalls[0].Status, replay.ToolCalls[0].Status);
        Assert.Equal(live.Terminals[0].Output, replay.Terminals[0].Output);
        Assert.Equal(Assert.IsType<IdleSessionWorkState>(live.WorkState).StopReason,
            Assert.IsType<IdleSessionWorkState>(replay.WorkState).StopReason);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public void ReplaySession_ArbitraryTerminalChunkBoundaries_PreservesOriginalBytes()
    {
        FsCheckPropertyRunner.Run(this, nameof(ArbitraryTerminalChunkBoundariesProperty));
    }

    [Fact]
    public void ReplaySession_ArbitraryMessageReplacements_ReplaceAllPriorChunks()
    {
        FsCheckPropertyRunner.Run(this, nameof(ArbitraryMessageReplacementsProperty));
    }

    private void ArbitraryTerminalChunkBoundariesProperty(byte[]? first, byte[]? second, byte[]? replacement, byte[]? tail)
    {
        // Arrange
        first ??= [];
        second ??= [];
        replacement ??= [];
        tail ??= [];
        var chunks = new[]
        {
            TerminalChunk(first),
            TerminalChunk(second)
        };
        var replay = new[]
        {
            chunks[0], chunks[1],
            Json("{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t\",\"output\":{\"data\":\""
                + Convert.ToBase64String(replacement) + "\"}}"),
            TerminalChunk(tail)
        };

        // Act
        var accumulated = AcpSessionDraftExtensions.ReplaySession("one", chunks);
        var replaced = AcpSessionDraftExtensions.ReplaySession("one", replay);

        // Assert
        Assert.Equal(first.Concat(second), accumulated.Terminals[0].Output);
        Assert.Equal(replacement.Concat(tail), replaced.Terminals[0].Output);
    }

    private void ArbitraryMessageReplacementsProperty(byte[]? before, byte[]? replacement, byte[]? after)
    {
        // Arrange
        var originalText = Convert.ToBase64String(before ?? []);
        var replacementText = Convert.ToBase64String(replacement ?? []);
        var tail = Convert.ToBase64String(after ?? []);
        var history = new[]
        {
            Json(Message("agent_message_chunk", "m", originalText)),
            Json(Whole("agent_message", "m", replacementText)),
            Json(Message("agent_message_chunk", "m", tail))
        };

        // Act
        var snapshot = AcpSessionDraftExtensions.ReplaySession("one", history);

        // Assert
        Assert.Equal([replacementText, tail], Texts(Assert.Single(snapshot.Messages)));
    }

    private static JsonElement TerminalChunk(byte[] bytes)
        => Json("{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t\",\"data\":\""
            + Convert.ToBase64String(bytes) + "\"}");

    private static string[] Texts(AcpMessageSnapshot message)
        => message.Content.Select(static item => Assert.IsType<TextContentBlock>(item).Text).ToArray();

    private static string[] ToolTexts(AcpToolCallSnapshot tool)
        => tool.Content.Select(static item => Assert.IsType<TextContentBlock>(Assert.IsType<ContentToolCallContent>(item).Content).Text).ToArray();

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Message(string kind, string id, string text)
        => "{\"sessionUpdate\":\"" + kind + "\",\"messageId\":\"" + id + "\",\"content\":{\"type\":\"text\",\"text\":\"" + text + "\"}}";

    private static string Whole(string kind, string id, string text)
        => "{\"sessionUpdate\":\"" + kind + "\",\"messageId\":\"" + id + "\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}";

    private sealed class ProjectionPeer : IAcpTransport
    {
        private readonly MessageParser _parser = new();
        private readonly int _version;
        private JsonRpcRequest? _resume;

        private ProjectionPeer(int version)
        {
            _version = version;
            Client = new AcpClient(this);
            Client.ErrorOccurred += (_, error) => Errors.Add(error);
        }

        public bool IsConnected { get; private set; }
        internal AcpClient Client { get; }
        internal List<string> Errors { get; } = [];
        internal List<JsonRpcRequest> Sent { get; } = [];
        internal Action<JsonRpcRequest>? OnResume { get; set; }
        internal Func<Task>? BeforeResumeWrite { get; set; }
        internal Func<Task<bool>>? ResumeWrite { get; set; }
        internal bool HoldResume { get; set; }

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred;

        internal static async Task<ProjectionPeer> CreateAsync(int version = AcpProtocolVersion.V2)
        {
            var peer = new ProjectionPeer(version);
            await peer.InitializeAsync();
            return peer;
        }

        internal Task<InitializeResponse> InitializeAsync()
        {
            var parameters = new InitializeParams(new ClientInfo("projection-peer", "1.0"), new ClientCapabilities())
            {
                ProtocolVersion = _version
            };
            return _version == AcpProtocolVersion.V2
                ? Client.InitializeDraftAsync(parameters, TestToken)
                : Client.InitializeAsync(parameters, TestToken);
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_parser.ParseMessage(message) is not JsonRpcRequest request) return Task.FromResult(true);
            if (request.Method == "session/resume" && BeforeResumeWrite is { } beforeWrite)
            {
                return SendAfterGateAsync(request, beforeWrite(), cancellationToken);
            }
            return Send(request);
        }

        private Task<bool> Send(JsonRpcRequest request)
        {
            Sent.Add(request);
            if (request.Method == "initialize")
            {
                var response = new InitializeResponse(_version, new AgentInfo("projection-agent", "1.0"), new AgentCapabilities
                {
                    SessionCapabilities = new SessionCapabilities
                    {
                        Close = new SessionCloseCapabilities(),
                        Resume = new SessionResumeCapabilities(),
                        Delete = new SessionDeleteCapabilities()
                    }
                });
                Reply(request, JsonSerializer.Serialize(response, AcpWireFormat.For(_version).TypeInfo<InitializeResponse>()));
            }
            else if (request.Method == "session/resume")
            {
                _resume = request;
                OnResume?.Invoke(request);
                if (ResumeWrite is not null) return ResumeWrite();
                if (!HoldResume) ReplyResume();
            }
            else
            {
                Reply(request, request.Method == "session/new" ? "{\"sessionId\":\"one\"}" : "{}");
            }
            return Task.FromResult(true);
        }

        private async Task<bool> SendAfterGateAsync(JsonRpcRequest request, Task gate, CancellationToken cancellationToken)
        {
            await gate.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await Send(request).ConfigureAwait(false);
        }

        internal void ReplyResume() => Reply(_resume!, "{}");

        internal void Update(string sessionId, string update) => Deliver(UpdateFrame(sessionId, update));

        internal static string UpdateFrame(string sessionId, string update)
            => "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"" + sessionId + "\",\"update\":" + update + "}}";

        internal Action<string> CaptureDelivery()
        {
            var handlers = MessageReceived;
            return json => handlers?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
        }

        internal void DropConnection()
        {
            IsConnected = false;
            ErrorOccurred?.Invoke(this, new AcpTransportErrorEventArgs("connection lost"));
        }

        public void Dispose()
        {
            Client.Dispose();
            IsConnected = false;
        }

        private void Reply(JsonRpcRequest request, string result)
            => Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":" + result + "}");

        private void Deliver(string json) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
    }
}
