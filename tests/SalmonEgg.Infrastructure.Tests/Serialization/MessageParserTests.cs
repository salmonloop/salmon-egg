using System.Text.Json;
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
}

