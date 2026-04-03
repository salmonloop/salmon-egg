using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class AcpErrorClassifierTests
{
    [Theory]
    [InlineData(JsonRpcErrorCode.ResourceNotFound)]
    public void IsRemoteSessionNotFound_WhenKnownErrorCodes_ReturnsTrue(int errorCode)
    {
        var ex = new AcpException(errorCode, "session lookup failed");

        Assert.True(AcpErrorClassifier.IsRemoteSessionNotFound(ex));
    }

    [Fact]
    public void IsRemoteSessionNotFound_WhenMessageContainsSessionNotFound_ReturnsTrue()
    {
        var ex = new AcpException(JsonRpcErrorCode.InternalError, "Session abc not found");

        Assert.True(AcpErrorClassifier.IsRemoteSessionNotFound(ex));
    }

    [Fact]
    public void IsRemoteSessionNotFound_WhenErrorIsDifferent_ReturnsFalse()
    {
        var ex = new AcpException(JsonRpcErrorCode.AuthenticationRequired, "auth required");

        Assert.False(AcpErrorClassifier.IsRemoteSessionNotFound(ex));
    }
}
