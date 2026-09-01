using SalmonEgg.Acp.JsonRpc;
using Xunit;

namespace SalmonEgg.Acp.Tests.JsonRpc;

/// <summary>
/// Locks the classification of <c>-32800</c> (Cancelled). ACP lists it alongside the JSON-RPC
/// standard codes and the ACP extension codes, but it belongs to neither band, so it needs its own
/// category rather than a widened range.
/// </summary>
public sealed class JsonRpcErrorCodeTests
{
    [Fact]
    public void Cancelled_UsesTheCodeTheSpecificationAssigns()
    {
        Assert.Equal(-32800, JsonRpcErrorCode.Cancelled);
    }

    [Fact]
    public void Cancelled_IsNeitherAStandardNorAnAcpExtensionCode()
    {
        // The point of the dedicated predicate: -32800 sits between the two bands, so widening
        // either one to reach it would also swallow codes no specification has assigned.
        Assert.False(JsonRpcErrorCode.IsStandardErrorCode(JsonRpcErrorCode.Cancelled));
        Assert.False(JsonRpcErrorCode.IsAcpErrorCode(JsonRpcErrorCode.Cancelled));
        Assert.True(JsonRpcErrorCode.IsCancelledErrorCode(JsonRpcErrorCode.Cancelled));
    }

    [Theory]
    [InlineData(JsonRpcErrorCode.ParseError)]
    [InlineData(JsonRpcErrorCode.InternalError)]
    [InlineData(JsonRpcErrorCode.AuthenticationRequired)]
    [InlineData(JsonRpcErrorCode.CapabilityNotSupported)]
    [InlineData(-32799)]
    [InlineData(-32801)]
    public void IsCancelledErrorCode_RejectsEveryOtherCode(int code)
    {
        Assert.False(JsonRpcErrorCode.IsCancelledErrorCode(code));
    }

    [Fact]
    public void GetErrorMessage_ForCancelled_DoesNotFallBackToUnknown()
    {
        // The reported symptom: a compliant peer answers -32800 and the client renders
        // "Unknown error (code: -32800)", which cannot be told apart from a real failure.
        var message = JsonRpcErrorCode.GetErrorMessage(JsonRpcErrorCode.Cancelled);

        Assert.Equal("Request cancelled", message);
        Assert.DoesNotContain("Unknown", message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonRpcError_ClassifiesCancelledSeparatelyFromBothBands()
    {
        var cancelled = new JsonRpcError(
            JsonRpcErrorCode.Cancelled,
            JsonRpcErrorCode.GetErrorMessage(JsonRpcErrorCode.Cancelled));

        Assert.True(cancelled.IsCancelled());
        Assert.False(cancelled.IsStandardError());
        Assert.False(cancelled.IsAcpError());

        // A neighbouring code must not be mistaken for a cancellation.
        Assert.False(new JsonRpcError(JsonRpcErrorCode.InternalError, "boom").IsCancelled());
    }
}
