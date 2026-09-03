using System.Text;
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class TerminalUpdateTypesTests
{
    private static SessionUpdateParams? Parse(string updateJson) =>
        JsonSerializer.Deserialize(
            "{\"sessionId\":\"session-1\",\"update\":" + updateJson + "}",
            Wire.V2<SessionUpdateParams>());

    [Fact]
    public void TerminalSessionUpdate_FullPayload_MapsEveryField()
    {
        var update = Assert.IsType<TerminalSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t-1\","
            + "\"command\":\"ls -la\",\"cwd\":\"/home/user\","
            + "\"output\":{\"data\":\"aGk=\"},"
            + "\"exitStatus\":{\"exitCode\":0,\"signal\":null}}")?.Update);

        Assert.Equal("t-1", update.TerminalId);
        Assert.Equal("ls -la", update.Command);
        Assert.Equal("/home/user", update.Cwd);
        Assert.Equal("aGk=", update.Output!.Data);
        Assert.Equal(0u, update.ExitStatus!.ExitCode);
        Assert.Null(update.ExitStatus.Signal);
    }

    // Everything but the id is patch semantics: absent leaves the current value alone while null
    // clears it. A plain nullable field cannot express that difference.
    [Fact]
    public void TerminalSessionUpdate_AbsentFields_AreDistinctFromExplicitNulls()
    {
        var absent = Assert.IsType<TerminalSessionUpdate>(
            Parse("{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t-1\"}")?.Update);
        var cleared = Assert.IsType<TerminalSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t-1\","
            + "\"command\":null,\"cwd\":null,\"output\":null,\"exitStatus\":null}")?.Update);

        Assert.False(absent.HasCommand);
        Assert.False(absent.HasCwd);
        Assert.False(absent.HasOutput);
        Assert.False(absent.HasExitStatus);

        Assert.True(cleared.HasCommand);
        Assert.True(cleared.HasCwd);
        Assert.True(cleared.HasOutput);
        Assert.True(cleared.HasExitStatus);
        Assert.Null(cleared.Command);
        Assert.Null(cleared.Output);
        Assert.Null(cleared.ExitStatus);
    }

    // An empty exitStatus object still means "the terminal exited": presence is the signal, not the
    // fields inside it.
    [Fact]
    public void TerminalSessionUpdate_EmptyExitStatus_MarksExitWithoutCodeOrSignal()
    {
        var update = Assert.IsType<TerminalSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t-1\",\"exitStatus\":{}}")?.Update);

        Assert.True(update.HasExitStatus);
        Assert.NotNull(update.ExitStatus);
        Assert.Null(update.ExitStatus!.ExitCode);
        Assert.Null(update.ExitStatus.Signal);
    }

    [Fact]
    public void TerminalOutputChunkSessionUpdate_MapsTerminalIdAndData()
    {
        var update = Assert.IsType<TerminalOutputChunkSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t-9\",\"data\":\"Zm9v\"}")?.Update);

        Assert.Equal("t-9", update.TerminalId);
        Assert.Equal("Zm9v", update.Data);
    }

    // Each chunk is independently base64-encoded, so consumers decode per chunk and concatenate bytes.
    // Concatenating the base64 text first corrupts any boundary whose payload is not a multiple of 3.
    [Fact]
    public void TerminalOutputChunks_DecodePerChunkThenConcatenateBytes()
    {
        var first = Assert.IsType<TerminalOutputChunkSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t-1\",\"data\":\""
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("ab")) + "\"}")?.Update);
        var second = Assert.IsType<TerminalOutputChunkSessionUpdate>(Parse(
            "{\"sessionUpdate\":\"terminal_output_chunk\",\"terminalId\":\"t-1\",\"data\":\""
            + Convert.ToBase64String(Encoding.UTF8.GetBytes("cd")) + "\"}")?.Update);

        var perChunk = Encoding.UTF8.GetString(
            [.. Convert.FromBase64String(first.Data), .. Convert.FromBase64String(second.Data)]);
        Assert.Equal("abcd", perChunk);

        // Proof the naive path is wrong rather than merely discouraged.
        Assert.NotEqual(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("abcd")),
            first.Data + second.Data);
    }

    [Fact]
    public void TerminalSessionUpdate_SerializesWithoutLeakingPresenceFlags()
    {
        var value = new SessionUpdateParams(
            "session-1",
            new TerminalSessionUpdate
            {
                TerminalId = "t-1",
                Command = "ls",
                HasCommand = true,
                Output = new TerminalOutput { Data = "aGk=" },
                HasOutput = true
            });

        var json = JsonSerializer.Serialize(value, Wire.V2<SessionUpdateParams>());

        using var document = JsonDocument.Parse(json);
        var update = document.RootElement.GetProperty("update");

        Assert.Equal("terminal_update", update.GetProperty("sessionUpdate").GetString());
        Assert.Equal("t-1", update.GetProperty("terminalId").GetString());
        Assert.Equal("ls", update.GetProperty("command").GetString());
        Assert.Equal("aGk=", update.GetProperty("output").GetProperty("data").GetString());
        Assert.False(update.TryGetProperty("hasCommand", out _));
        Assert.False(update.TryGetProperty("hasOutput", out _));
        Assert.False(update.TryGetProperty("HasCommand", out _));
    }

    [Fact]
    public void TerminalUpdates_RoundTripThroughTheSessionUpdateContract()
    {
        const string UpdateJson =
            "{\"sessionUpdate\":\"terminal_update\",\"terminalId\":\"t-3\",\"exitStatus\":{\"signal\":\"SIGTERM\"}}";

        var parsed = Parse(UpdateJson);
        var json = JsonSerializer.Serialize(parsed!, Wire.V2<SessionUpdateParams>());
        var reparsed = JsonSerializer.Deserialize(json, Wire.V2<SessionUpdateParams>());

        var update = Assert.IsType<TerminalSessionUpdate>(reparsed?.Update);
        Assert.Equal("t-3", update.TerminalId);
        Assert.True(update.HasExitStatus);
        Assert.Equal("SIGTERM", update.ExitStatus!.Signal);
        Assert.Null(update.ExitStatus.ExitCode);
    }

    // v2 removed every terminal/* method: the Client no longer creates or controls terminals over ACP.
    // The v1 request types stay on the public surface because ApiCompat forbids removing them, so the
    // distinction has to be asserted rather than assumed from their absence.
    [Fact]
    public void V2TerminalSurface_IsNotificationOnly_WhileV1RequestTypesRemain()
    {
        Assert.True(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalSessionUpdate)));
        Assert.True(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalOutputChunkSessionUpdate)));

        Assert.False(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalCreateRequest)));
        Assert.False(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalOutputRequest)));
        Assert.False(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalKillRequest)));
        Assert.False(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalReleaseRequest)));
        Assert.False(typeof(SessionUpdate).IsAssignableFrom(typeof(TerminalWaitForExitRequest)));
    }
}
