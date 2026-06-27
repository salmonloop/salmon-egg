using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class AuthenticateTypesTests
{
    [Test]
    public void AuthenticateResponse_SerializesAsEmptyObject()
    {
        var json = JsonSerializer.Serialize(new AuthenticateResponse());

        Assert.That(json, Is.EqualTo("{}"));
    }
}
