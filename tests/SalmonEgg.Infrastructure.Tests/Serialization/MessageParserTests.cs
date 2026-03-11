using System.Text.Json;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Infrastructure.Tests.Serialization;

public class MessageParserTests
{
    [Fact]
    public void Options_ShouldDeserialize_PlanUpdate_WithSnakeCaseEnumValues()
    {
        var json = """
        {
          "sessionId": "sess_test",
          "update": {
            "sessionUpdate": "plan",
            "entries": [
              { "content": "Work Items", "status": "in_progress", "priority": "medium" }
            ]
          }
        }
        """;

        var parser = new MessageParser();
        var updateParams = JsonSerializer.Deserialize<SessionUpdateParams>(json, parser.Options);

        Assert.NotNull(updateParams);
        Assert.NotNull(updateParams!.Update);
        Assert.IsType<PlanUpdate>(updateParams.Update);

        var plan = (PlanUpdate)updateParams.Update;
        Assert.NotNull(plan.Entries);
        Assert.Single(plan.Entries!);
        Assert.Equal(PlanEntryStatus.InProgress, plan.Entries![0].Status);
        Assert.Equal(PlanEntryPriority.Medium, plan.Entries![0].Priority);
    }

    [Fact]
    public void SerializeMessage_ShouldOmitNullOptionalFields()
    {
        var parser = new MessageParser();

        var promptParams = new SessionPromptParams
        {
            SessionId = "sess_test",
            Prompt = new[] { new TextContentBlock { Text = "hi" } },
            MaxTokens = null,
            StopSequences = null
        };

        var request = new JsonRpcRequest(
            id: 3,
            method: "session/prompt",
            @params: JsonSerializer.SerializeToElement(promptParams, typeof(SessionPromptParams), parser.Options));

        var json = parser.SerializeMessage(request);

        Assert.DoesNotContain("\"maxTokens\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"stopSequences\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeMessage_ShouldKeepResultNull_ForJsonRpcResponses()
    {
        var parser = new MessageParser();

        // JSON-RPC responses must include either "result" or "error". For some ACP methods the result is null.
        var nullResult = JsonSerializer.SerializeToElement<object?>(null, parser.Options);
        var response = new JsonRpcResponse(id: 1, result: nullResult);

        var json = parser.SerializeMessage(response);

        Assert.Contains("\"result\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", json, StringComparison.Ordinal);
    }
}

