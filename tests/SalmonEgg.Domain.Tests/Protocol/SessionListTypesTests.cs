using System.Text.Json;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionListTypesTests
{
    [Test]
    public void ListSessionsResponse_Should_Serialize_Correctly()
    {
        // Given: A ListSessionsResponse with sessions
        var response = new ListSessionsResponse
        {
            Sessions = new List<SessionInfo>
            {
                new SessionInfo
                {
                    SessionId = "test-session",
                    Cwd = "/home/user/project",
                    Title = "Test Session"
                }
            }
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(response);
        var parsed = JsonDocument.Parse(json);

        // Then: sessions should be an array in JSON
        Assert.That(parsed.RootElement.TryGetProperty("sessions", out var sessions), Is.True);
        Assert.That(sessions.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public void SessionInfo_Should_Serialize_Correctly()
    {
        // Given: A SessionInfo with data
        var sessionInfo = new SessionInfo
        {
            SessionId = "test-session",
            Cwd = "/home/user/project",
            Title = "Test Session",
            UpdatedAt = "2024-01-01T00:00:00Z"
        };

        // When: Serialize to JSON
        var json = JsonSerializer.Serialize(sessionInfo);
        var parsed = JsonDocument.Parse(json);

        // Then: All properties should be present
        Assert.That(parsed.RootElement.TryGetProperty("sessionId", out var sessionId), Is.True);
        Assert.That(sessionId.GetString(), Is.EqualTo("test-session"));
        Assert.That(parsed.RootElement.TryGetProperty("cwd", out var cwd), Is.True);
        Assert.That(cwd.GetString(), Is.EqualTo("/home/user/project"));
        Assert.That(parsed.RootElement.TryGetProperty("title", out var title), Is.True);
        Assert.That(title.GetString(), Is.EqualTo("Test Session"));
        Assert.That(parsed.RootElement.TryGetProperty("updatedAt", out var updatedAt), Is.True);
        Assert.That(updatedAt.GetString(), Is.EqualTo("2024-01-01T00:00:00Z"));
    }
}
