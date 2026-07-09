using NUnit.Framework;
using SalmonEgg.Acp.Protocol;
using System.Text.Json;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class SessionListRuntimeTypesTests
{
    [Test]
    public void SessionListParams_SerializesCursorField()
    {
        var payload = new SessionListParams
        {
            Cwd = "/repo",
            Cursor = "cursor-1"
        };

        var json = JsonSerializer.Serialize(payload);

        Assert.That(json, Does.Contain("\"cursor\":\"cursor-1\""));
    }

    [Test]
    public void SessionListResponse_DeserializesNextCursorField()
    {
        var json = """
        {
          "sessions": [],
          "nextCursor": "cursor-2"
        }
        """;

        var response = JsonSerializer.Deserialize<SessionListResponse>(json);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.NextCursor, Is.EqualTo("cursor-2"));
    }
}
