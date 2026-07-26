using SalmonEgg.Infrastructure.Network;

namespace SalmonEgg.Infrastructure.Tests.Network;

public sealed class SseEventAccumulatorTests
{
    [Fact]
    public void TryAppendLine_SingleDataLine_DispatchesOnBlankLine()
    {
        var accumulator = new SseEventAccumulator();

        Assert.False(accumulator.TryAppendLine("data: {\"id\":1}", out _));
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal("{\"id\":1}", data);
    }

    [Fact]
    public void TryAppendLine_MultiLineData_JoinsWithNewline()
    {
        var accumulator = new SseEventAccumulator();

        accumulator.TryAppendLine("data: first", out _);
        accumulator.TryAppendLine("data: second", out _);
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal("first\nsecond", data);
    }

    [Fact]
    public void TryAppendLine_ValueWithoutLeadingSpace_IsNotTruncated()
    {
        var accumulator = new SseEventAccumulator();

        accumulator.TryAppendLine("data:x", out _);
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal("x", data);
    }

    [Fact]
    public void TryAppendLine_OnlyFirstLeadingSpaceIsStripped()
    {
        var accumulator = new SseEventAccumulator();

        accumulator.TryAppendLine("data:  spaced", out _);
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal(" spaced", data);
    }

    [Fact]
    public void TryAppendLine_CommentsAndForeignFields_DoNotAffectData()
    {
        var accumulator = new SseEventAccumulator();

        Assert.False(accumulator.TryAppendLine(": heartbeat", out _));
        Assert.False(accumulator.TryAppendLine("event: message", out _));
        Assert.False(accumulator.TryAppendLine("id: 42", out _));
        Assert.False(accumulator.TryAppendLine("retry: 1000", out _));
        accumulator.TryAppendLine("data: payload", out _);
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal("payload", data);
    }

    [Fact]
    public void TryAppendLine_BlankLineWithoutData_DispatchesNothing()
    {
        var accumulator = new SseEventAccumulator();

        Assert.False(accumulator.TryAppendLine("event: ping", out _));
        Assert.False(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Null(data);
    }

    [Fact]
    public void TryAppendLine_BareDataFieldName_AppendsEmptyValue()
    {
        var accumulator = new SseEventAccumulator();

        accumulator.TryAppendLine("data", out _);
        accumulator.TryAppendLine("data: tail", out _);
        Assert.True(accumulator.TryAppendLine(string.Empty, out var data));
        Assert.Equal("\ntail", data);
    }
}
