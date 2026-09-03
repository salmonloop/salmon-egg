using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;
using Xunit;

namespace SalmonEgg.Acp.Tests.Tool;

public sealed class StructuredDiffTypesTests
{
    private static string SerializeV2(ToolCallContent value)
    {
        return JsonSerializer.Serialize(value, Wire.V2<ToolCallContent>());
    }

    [Fact]
    public void StructuredDiff_SerializesChangesAndPatch()
    {
        var json = SerializeV2(new StructuredDiff
        {
            Changes =
            [
                new DiffChange { Operation = DiffOperationKind.Modify, Path = "/work/a.txt", FileType = DiffFileTypeKind.Text },
                new DiffChange { Operation = DiffOperationKind.Move, OldPath = "/work/b.txt", Path = "/work/c.txt" }
            ],
            Patch = new DiffPatch { Format = DiffPatchFormatKind.GitPatch, Text = "diff --git a/work/a.txt b/work/a.txt" }
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("diff", root.GetProperty("type").GetString());
        var changes = root.GetProperty("changes");
        Assert.Equal(2, changes.GetArrayLength());
        Assert.Equal("modify", changes[0].GetProperty("operation").GetString());
        Assert.Equal("/work/c.txt", changes[1].GetProperty("path").GetString());
        Assert.Equal("/work/b.txt", changes[1].GetProperty("oldPath").GetString());
        Assert.Equal("git_patch", root.GetProperty("patch").GetProperty("format").GetString());
        Assert.False(root.TryGetProperty("oldText", out _));
        Assert.False(root.TryGetProperty("newText", out _));
    }

    // v1 and v2 share the "diff" discriminator, so the payload shape is the only thing telling them
    // apart - which makes the negotiated version part of the answer, not just the JSON.
    [Fact]
    public void StructuredDiff_AndV1FlatDiff_ShareDiscriminatorButRemainDistinct()
    {
        const string structuredJson = "{\"type\":\"diff\",\"changes\":[{\"operation\":\"add\",\"path\":\"/work/a\"}]}";
        const string flatJson = "{\"type\":\"diff\",\"path\":\"/work/a\",\"oldText\":null,\"newText\":\"x\"}";

        var v2 = Assert.IsType<StructuredDiff>(
            JsonSerializer.Deserialize(structuredJson, Wire.V2<ToolCallContent>()));
        Assert.Single(v2.Changes);

        // The flat form is v1's, and v2 keeps it on the surface, so it binds either way.
        Assert.IsType<DiffToolCallContent>(JsonSerializer.Deserialize(flatJson, Wire.V2<ToolCallContent>()));
        Assert.IsType<DiffToolCallContent>(JsonSerializer.Deserialize(flatJson, Wire.V1<ToolCallContent>()));
    }

    // Before the v2 shape was modeled, a structured diff arriving on a v1 connection landed in
    // passthrough and round-tripped. Modeling it made v1 bind a contract it then refused to write, so
    // the payload could be read and never sent back. Passthrough is restored, and asserted round-trip
    // rather than merely "not StructuredDiff" - the point is that nothing is lost.
    [Fact]
    public void StructuredDiff_OnAStableConnection_PassesThroughAndRoundTrips()
    {
        const string structuredJson =
            "{\"type\":\"diff\",\"changes\":[{\"operation\":\"add\",\"path\":\"/work/a\"}],\"patch\":{\"format\":\"git_patch\",\"text\":\"p\"}}";

        var parsed = JsonSerializer.Deserialize(structuredJson, Wire.V1<ToolCallContent>());

        var passthrough = Assert.IsType<CustomToolCallContent>(parsed);
        Assert.Equal("diff", passthrough.Type);
        Assert.Equal(
            structuredJson,
            JsonSerializer.Serialize(parsed, Wire.V1<ToolCallContent>()));
    }

    [Fact]
    public void StructuredDiff_SkipsMalformedChangesButKeepsValidSiblings()
    {
        var parsed = JsonSerializer.Deserialize(
            "{\"type\":\"diff\",\"changes\":[null,{\"operation\":\"copy\",\"oldPath\":\"/a\",\"path\":\"/b\"},42]}",
            Wire.V2<ToolCallContent>());

        var diff = Assert.IsType<StructuredDiff>(parsed);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffOperationKind.Copy, change.Operation);
        Assert.Equal("/a", change.OldPath);
    }

    [Fact]
    public void StructuredDiff_OnAStableConnection_RefusesToSerialize()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize<ToolCallContent>(
            new StructuredDiff { Changes = [new DiffChange { Operation = "add", Path = "/a" }] },
            Wire.V1<ToolCallContent>()));

        Assert.Equal(StructuredDiffWireFormat.V2OnlyMessage, exception.Message);
    }
}
