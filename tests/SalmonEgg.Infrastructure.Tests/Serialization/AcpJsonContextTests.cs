using System.Text.Json;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Infrastructure.Serialization;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Serialization;

public sealed class AcpJsonContextTests
{
    [Fact]
    public void AuthenticateResponse_SerializesWithGeneratedContextAsEmptyObject()
    {
        var json = JsonSerializer.Serialize(
            new AuthenticateResponse(),
            AcpJsonContext.Default.AuthenticateResponse);

        Assert.Equal("{}", json);
    }
}
