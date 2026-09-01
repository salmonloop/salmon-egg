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
        using var scope = AcpProtocolWriteContext.Enter(AcpProtocolVersion.V2);
        return JsonSerializer.Serialize(value, AcpJsonContext.Default.ToolCallContent);
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

    [Fact]
    public void StructuredDiff_AndV1FlatDiff_ShareDiscriminatorButRemainDistinct()
    {
        var structured = JsonSerializer.Deserialize<ToolCallContent>(
            "{\"type\":\"diff\",\"changes\":[{\"operation\":\"add\",\"path\":\"/work/a\"}]}",
            AcpJsonContext.Default.ToolCallContent);
        var flat = JsonSerializer.Deserialize<ToolCallContent>(
            "{\"type\":\"diff\",\"path\":\"/work/a\",\"oldText\":null,\"newText\":\"x\"}",
            AcpJsonContext.Default.ToolCallContent);

        var v2 = Assert.IsType<StructuredDiff>(structured);
        Assert.Single(v2.Changes);
        Assert.IsType<DiffToolCallContent>(flat);
    }

    [Fact]
    public void StructuredDiff_SkipsMalformedChangesButKeepsValidSiblings()
    {
        var parsed = JsonSerializer.Deserialize<ToolCallContent>(
            "{\"type\":\"diff\",\"changes\":[null,{\"operation\":\"copy\",\"oldPath\":\"/a\",\"path\":\"/b\"},42]}",
            AcpJsonContext.Default.ToolCallContent);

        var diff = Assert.IsType<StructuredDiff>(parsed);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(DiffOperationKind.Copy, change.Operation);
        Assert.Equal("/a", change.OldPath);
    }

    [Fact]
    public void StructuredDiff_UnderV1WriteContext_RefusesToSerialize()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Serialize<ToolCallContent>(
            new StructuredDiff { Changes = [new DiffChange { Operation = "add", Path = "/a" }] },
            AcpJsonContext.Default.ToolCallContent));

        Assert.Equal(StructuredDiffWireFormat.V2OnlyMessage, exception.Message);
    }
}
