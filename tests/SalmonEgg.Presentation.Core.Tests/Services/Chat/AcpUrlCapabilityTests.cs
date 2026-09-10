using Moq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Services.Chat;

public sealed class AcpUrlCapabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateDefault_PlatformCapability_AdvertisesOnlyVerifiedUrlSupport(bool supported)
    {
        // Arrange
        var capabilities = new Mock<IPlatformCapabilityService>();
        capabilities.SetupGet(x => x.SupportsUrlElicitation).Returns(supported);

        // Act
        var request = AcpInitializeRequestFactory.CreateDefault(capabilities.Object);

        // Assert
        Assert.NotNull(request.ClientCapabilities.Elicitation?.Form);
        Assert.Equal(supported, request.ClientCapabilities.Elicitation?.Url is not null);
    }

    [Fact]
    public void CreateDefault_NoPlatformEvidence_DoesNotAdvertiseUrl()
    {
        // Act
        var request = AcpInitializeRequestFactory.CreateDefault();

        // Assert
        Assert.NotNull(request.ClientCapabilities.Elicitation?.Form);
        Assert.Null(request.ClientCapabilities.Elicitation?.Url);
    }
}
