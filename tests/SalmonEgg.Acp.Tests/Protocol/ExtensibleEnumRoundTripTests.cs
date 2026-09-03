using System.Text.Json;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Acp.Tool;
using Xunit;

namespace SalmonEgg.Acp.Tests.Protocol;

/// <summary>
/// Locks the ACP extensibility contract for the five open protocol string enums:
/// unknown wire values must be preserved and round-tripped losslessly, while
/// non-string tokens remain type errors.
/// </summary>
public sealed class ExtensibleEnumRoundTripTests
{
    public static TheoryData<string> FutureAndExtensionValues() => new()
    {
        "future_variant",
        "_impl_specific",
        "cancelled_but_not_really",
    };

    [Theory]
    [MemberData(nameof(FutureAndExtensionValues))]
    public void StopReason_UnknownValue_RoundTripsLosslessly(string wire)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new StopReason(wire), AcpJsonContext.Default.StopReason);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.StopReason);

        Assert.Equal(new StopReason(wire), roundTripped);
        Assert.Equal(wire, roundTripped.Value);
        Assert.NotEqual(StopReason.EndTurn, roundTripped);
        Assert.Equal(wire, JsonSerializer.Deserialize<JsonElement>(json).GetString());
    }

    [Theory]
    [MemberData(nameof(FutureAndExtensionValues))]
    public void ToolCallStatus_UnknownValue_RoundTripsLosslessly(string wire)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new ToolCallStatus(wire), AcpJsonContext.Default.ToolCallStatus);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.ToolCallStatus);

        Assert.Equal(new ToolCallStatus(wire), roundTripped);
        Assert.Equal(wire, roundTripped.Value);
        Assert.NotEqual(ToolCallStatus.Pending, roundTripped);
    }

    [Theory]
    [MemberData(nameof(FutureAndExtensionValues))]
    public void ToolCallKind_UnknownValue_RoundTripsLosslessly_AndStaysDistinctFromOther(string wire)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new ToolCallKind(wire), AcpJsonContext.Default.ToolCallKind);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.ToolCallKind);

        Assert.Equal(new ToolCallKind(wire), roundTripped);
        Assert.Equal(wire, roundTripped.Value);
        // Well-known "other" is a named member; an unrecognized future kind keeps its own value.
        Assert.NotEqual(ToolCallKind.Other, roundTripped);
    }

    [Fact]
    public void ToolCallKind_Other_Literal_RoundTripsAsOther()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(ToolCallKind.Other, AcpJsonContext.Default.ToolCallKind);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.ToolCallKind);

        Assert.Equal(ToolCallKind.Other, roundTripped);
        Assert.Equal("other", roundTripped.Value);
    }

    [Theory]
    [MemberData(nameof(FutureAndExtensionValues))]
    public void PlanEntryStatus_UnknownValue_RoundTripsLosslessly(string wire)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new PlanEntryStatus(wire), AcpJsonContext.Default.PlanEntryStatus);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.PlanEntryStatus);

        Assert.Equal(new PlanEntryStatus(wire), roundTripped);
        Assert.Equal(wire, roundTripped.Value);
        Assert.NotEqual(PlanEntryStatus.Pending, roundTripped);
    }

    [Fact]
    public void PlanEntryStatus_Cancelled_IsANamedWellKnownValue()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(PlanEntryStatus.Cancelled, AcpJsonContext.Default.PlanEntryStatus);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.PlanEntryStatus);

        Assert.Equal(PlanEntryStatus.Cancelled, roundTripped);
        Assert.Equal("cancelled", roundTripped.Value);
        Assert.Equal("cancelled", JsonSerializer.Deserialize<JsonElement>(json).GetString());
    }

    [Theory]
    [MemberData(nameof(FutureAndExtensionValues))]
    public void PlanEntryPriority_UnknownValue_RoundTripsLosslessly(string wire)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new PlanEntryPriority(wire), AcpJsonContext.Default.PlanEntryPriority);
        var roundTripped = JsonSerializer.Deserialize(json, AcpJsonContext.Default.PlanEntryPriority);

        Assert.Equal(new PlanEntryPriority(wire), roundTripped);
        Assert.Equal(wire, roundTripped.Value);
        Assert.NotEqual(PlanEntryPriority.Medium, roundTripped);
    }

    [Fact]
    public void SessionPromptResponse_UnknownStopReason_DeserializesWithoutThrowing()
    {
        const string json = """
        {
          "stopReason": "future_variant"
        }
        """;

        var response = JsonSerializer.Deserialize(json, AcpJsonContext.Default.SessionPromptResponse);

        Assert.NotNull(response);
        Assert.Equal(new StopReason("future_variant"), response!.StopReason);
        Assert.Equal("future_variant", response.StopReason.Value);
    }

    [Fact]
    public void StopReason_NonStringToken_StillThrows()
    {
        const string json = "42";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AcpJsonContext.Default.StopReason));
    }

    /// <summary>
    /// 类型契约:裸 <see cref="ToolCallStatus"/> 不是 schema 里的某个字段,没有任何容忍标注,
    /// 因此直接反序列化一个非字符串 token 仍须抛错。容忍属于**字段**而不属于类型 ——
    /// <c>ToolCallUpdate.status</c> 那一侧标了 x-deserialize-default-on-error,由属性级
    /// 转换器负责回落,见 <c>ToolCallStatusToleranceJsonConverter</c>。
    /// </summary>
    [Fact]
    public void ToolCallStatus_NonStringToken_StillThrows()
    {
        const string json = "true";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AcpJsonContext.Default.ToolCallStatus));
    }

    [Fact]
    public void PlanEntryStatus_NonStringToken_StillThrows()
    {
        const string json = "null";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, AcpJsonContext.Default.PlanEntryStatus));
    }

    [Fact]
    public void ExtensibleEnums_RejectNullConstructorArgument()
    {
        Assert.Throws<ArgumentNullException>(() => new StopReason(null!));
        Assert.Throws<ArgumentNullException>(() => new ToolCallStatus(null!));
        Assert.Throws<ArgumentNullException>(() => new ToolCallKind(null!));
        Assert.Throws<ArgumentNullException>(() => new PlanEntryStatus(null!));
        Assert.Throws<ArgumentNullException>(() => new PlanEntryPriority(null!));
    }

    [Fact]
    public void ExtensibleEnums_EqualityIsOrdinalOnWireValue()
    {
        Assert.Equal(StopReason.EndTurn, new StopReason("end_turn"));
        Assert.NotEqual(StopReason.EndTurn, new StopReason("END_TURN"));
        Assert.Equal(ToolCallKind.Other, new ToolCallKind("other"));
        Assert.NotEqual(ToolCallKind.Other, new ToolCallKind("Other"));
        Assert.True(PlanEntryStatus.Cancelled == new PlanEntryStatus("cancelled"));
        Assert.True(PlanEntryPriority.High != new PlanEntryPriority("HIGH"));
    }
}
