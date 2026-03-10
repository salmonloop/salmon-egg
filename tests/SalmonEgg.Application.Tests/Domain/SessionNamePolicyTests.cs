using SalmonEgg.Domain.Models.Session;

namespace SalmonEgg.Application.Tests.Domain;

public class SessionNamePolicyTests
{
    [Fact]
    public void CreateDefault_UsesShortSessionId()
    {
        Assert.Equal("会话 12345678", SessionNamePolicy.CreateDefault("1234567890"));
        Assert.Equal("会话 abc", SessionNamePolicy.CreateDefault("abc"));
    }

    [Fact]
    public void Sanitize_TrimsAndCapsLength()
    {
        Assert.Equal("hello", SessionNamePolicy.Sanitize("  hello  "));

        var longName = new string('a', SessionNamePolicy.MaxLength + 10);
        Assert.Equal(SessionNamePolicy.MaxLength, SessionNamePolicy.Sanitize(longName)!.Length);
    }

    [Fact]
    public void Sanitize_EmptyReturnsEmptyString()
    {
        Assert.Equal(string.Empty, SessionNamePolicy.Sanitize("   "));
    }
}
