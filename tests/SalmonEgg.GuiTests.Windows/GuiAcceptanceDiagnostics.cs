using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalmonEgg.GuiTests.Windows;

internal static class GuiAcceptanceDiagnostics
{
    public static void Record(string stage)
    {
        var directory = Environment.GetEnvironmentVariable("SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var record = new Stage(DateTimeOffset.UtcNow, Environment.ProcessId, stage);
        File.AppendAllText(Path.Combine(directory, "gui-stage.jsonl"),
            JsonSerializer.Serialize(record, GuiAcceptanceJsonContext.Default.Stage) + Environment.NewLine);
    }

    internal sealed record Stage(DateTimeOffset Time, int ProcessId, string Name);
}

[JsonSerializable(typeof(GuiAcceptanceDiagnostics.Stage))]
internal sealed partial class GuiAcceptanceJsonContext : JsonSerializerContext;
