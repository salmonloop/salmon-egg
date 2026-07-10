using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class SessionListTypesTests
{
    [Fact]
    public void ListSessionsResponse_Should_Serialize_Correctly()
    {
        var response = new SessionListResponse
        {
            Sessions = new List<AgentSessionInfo>
            {
                new AgentSessionInfo
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
        Assert.True(parsed.RootElement.TryGetProperty("sessions", out var sessions));
        Assert.Equal(JsonValueKind.Array, sessions.ValueKind);
    }

    [Fact]
    public void SessionInfo_Should_Serialize_Correctly()
    {
        var sessionInfo = new AgentSessionInfo
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
        Assert.True(parsed.RootElement.TryGetProperty("sessionId", out var sessionId));
        Assert.Equal("test-session", sessionId.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("cwd", out var cwd));
        Assert.Equal("/home/user/project", cwd.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("title", out var title));
        Assert.Equal("Test Session", title.GetString());
        Assert.True(parsed.RootElement.TryGetProperty("updatedAt", out var updatedAt));
        Assert.Equal("2024-01-01T00:00:00Z", updatedAt.GetString());
    }
}
