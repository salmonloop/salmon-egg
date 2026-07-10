using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Domain.Tests.Models.JsonRpc;

public sealed class AcpExceptionTests
{
    [Fact]
    public void Message_WhenJsonRpcErrorDataContainsDetails_IncludesRemoteDetails()
    {
        using var document = JsonDocument.Parse("""{"details":"Already initialized"}""");

        var exception = new AcpException(
            JsonRpcErrorCode.InternalError,
            "Internal error",
            document.RootElement.Clone());

        Assert.Equal("Internal error: Already initialized", exception.Message);
    }

    [Fact]
    public void Message_WhenJsonRpcErrorDataHasNoKnownDetailField_IncludesRawRemoteData()
    {
        using var document = JsonDocument.Parse("""{"reason":"Bridge lifecycle mismatch"}""");

        var exception = new AcpException(
            JsonRpcErrorCode.InternalError,
            "Internal error",
            document.RootElement.Clone());

        Assert.Equal("""Internal error: {"reason":"Bridge lifecycle mismatch"}""", exception.Message);
    }
}
