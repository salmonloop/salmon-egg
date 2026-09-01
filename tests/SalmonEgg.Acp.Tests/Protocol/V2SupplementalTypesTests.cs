using System.Text.Json;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class V2SupplementalTypesTests
{
    [Fact]
    public void MarkerCapabilities_UseEmptyObjectForSupport()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new PromptImageCapabilities(), AcpJsonContext.Default.PromptImageCapabilities));
        Assert.Equal("{}", JsonSerializer.Serialize(new McpHttpCapabilities(), AcpJsonContext.Default.McpHttpCapabilities));
        Assert.Equal("{}", JsonSerializer.Serialize(new TerminalAuthCapabilities(), AcpJsonContext.Default.TerminalAuthCapabilities));
    }

    [Fact]
    public void Icon_RoundTripsOptionalPresentationFields()
    {
        var icon = JsonSerializer.Deserialize("{\"src\":\"https://example.test/icon.svg\",\"mimeType\":\"image/svg+xml\",\"sizes\":[\"any\"],\"theme\":\"dark\"}", AcpJsonContext.Default.Icon);
        Assert.Equal("https://example.test/icon.svg", icon!.Src);
        Assert.Equal(IconThemeKind.Dark, icon.Theme);
        Assert.Single(icon.Sizes!);
    }

    [Fact]
    public void SessionListCursor_IsOpaque()
    {
        SessionListCursor cursor = "agent-defined:opaque";
        Assert.Equal("agent-defined:opaque", cursor.Value);
        Assert.Equal("agent-defined:opaque", (string)cursor);
    }

    [Fact]
    public void TextCommandInput_RequiresOnlyAHint()
    {
        var input = JsonSerializer.Deserialize("{\"hint\":\"branch name\"}", AcpJsonContext.Default.TextCommandInput);
        Assert.Equal("branch name", input!.Hint);
    }

    [Fact]
    public void V2PlanUpdate_UsesPlanEnvelopeAndItemsContent()
    {
        var update = JsonSerializer.Deserialize<SessionUpdateParams>(
            "{\"sessionId\":\"s\",\"update\":{\"sessionUpdate\":\"plan_update\",\"plan\":{\"type\":\"items\",\"planId\":\"p\",\"entries\":[{\"content\":\"Do it\",\"priority\":\"high\",\"status\":\"pending\"}]}}}",
            AcpJsonContext.Default.SessionUpdateParams);
        var planUpdate = Assert.IsType<V2PlanUpdate>(update!.Update);
        var items = Assert.IsType<PlanItemsUpdateContent>(planUpdate.Plan);
        Assert.Equal("p", items.PlanId);
        Assert.Single(items.Entries);
    }

    [Fact]
    public void UnknownPlanContent_RoundTripsRawPayload()
    {
        const string Json = "{\"type\":\"_vendor_plan\",\"planId\":\"p\",\"data\":{\"a\":1}}";
        var content = JsonSerializer.Deserialize<PlanUpdateContent>(Json, AcpJsonContext.Default.PlanUpdateContent);
        Assert.IsType<CustomPlanUpdateContent>(content);
        Assert.Equal(Json, JsonSerializer.Serialize(content, AcpJsonContext.Default.PlanUpdateContent));
    }
}
