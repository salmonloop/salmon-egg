using Xunit;
using SalmonEgg.Acp.Protocol;
using System.Text.Json;

namespace SalmonEgg.Acp.Tests.Protocol;

public sealed class SessionListRuntimeTypesTests
{
    [Fact]
    public void SessionListParams_SerializesCursorField()
    {
        var payload = new SessionListParams
        {
            Cwd = "/repo",
            Cursor = "cursor-1"
        };

        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"cursor\":\"cursor-1\"", json);
    }

    [Fact]
    public void SessionListResponse_DeserializesNextCursorField()
    {
        var json = """
        {
          "sessions": [],
          "nextCursor": "cursor-2"
        }
        """;

        var response = JsonSerializer.Deserialize<SessionListResponse>(json);

        Assert.NotNull(response);
        Assert.Equal("cursor-2", response!.NextCursor);
    }
}
