using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.JsonRpc;

namespace SalmonEgg.Domain.Tests.Models.JsonRpc;

[TestFixture]
public sealed class AcpExceptionTests
{
    [Test]
    public void Message_WhenJsonRpcErrorDataContainsDetails_IncludesRemoteDetails()
    {
        using var document = JsonDocument.Parse("""{"details":"Already initialized"}""");

        var exception = new AcpException(
            JsonRpcErrorCode.InternalError,
            "Internal error",
            document.RootElement.Clone());

        Assert.That(exception.Message, Is.EqualTo("Internal error: Already initialized"));
    }

    [Test]
    public void Message_WhenJsonRpcErrorDataHasNoKnownDetailField_IncludesRawRemoteData()
    {
        using var document = JsonDocument.Parse("""{"reason":"Bridge lifecycle mismatch"}""");

        var exception = new AcpException(
            JsonRpcErrorCode.InternalError,
            "Internal error",
            document.RootElement.Clone());

        Assert.That(exception.Message, Is.EqualTo("""Internal error: {"reason":"Bridge lifecycle mismatch"}"""));
    }
}
