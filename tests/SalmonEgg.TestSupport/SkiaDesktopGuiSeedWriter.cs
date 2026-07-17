using System.Text;
using System.Text.Json;

namespace SalmonEgg.TestSupport;

/// <summary>
/// Portable AppData seed used by Skia Desktop GUI smoke. Writes real production
/// conversation/config files only — no UI hooks, no AT-SPI providers.
/// </summary>
public static class SkiaDesktopGuiSeedWriter
{
    public const string ConversationId = "skia-mixed-session-01";
    public const string ConversationDisplayName = "Skia Mixed Transcript 01";
    public const string MarkdownMarker = "SKIA_MD_MARKER_7f3a";
    public const string ToolCallId = "skia-tool-call-1";
    public const string ToolCallTitle = "Read config";
    public const string ProjectId = "project-1";

    public static SeedPaths WriteMixedTranscriptSeed(string appDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);

        var paths = SeedPaths.Create(appDataRoot);
        Directory.CreateDirectory(paths.ConfigDirectory);
        Directory.CreateDirectory(paths.ConversationsDirectory);
        Directory.CreateDirectory(paths.ProjectRootPath);

        File.WriteAllText(paths.AppYamlPath, BuildAppYaml(paths.ProjectRootPath), Encoding.UTF8);
        File.WriteAllText(
            paths.ConversationsPath,
            BuildMixedTranscriptConversationsJson(paths.ProjectRootPath),
            Encoding.UTF8);

        return paths;
    }

    public static string BuildMixedTranscriptConversationsJson(string projectRootPath)
    {
        var timestamp = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var document = new
        {
            version = 1,
            lastActiveConversationId = ConversationId,
            conversations = new object[]
            {
                new
                {
                    conversationId = ConversationId,
                    displayName = ConversationDisplayName,
                    createdAt = timestamp,
                    lastUpdatedAt = timestamp.AddMinutes(5),
                    lastAccessedAt = timestamp.AddMinutes(5),
                    cwd = projectRootPath,
                    messages = new object[]
                    {
                        new
                        {
                            id = "skia-user-1",
                            timestamp = timestamp,
                            contentType = "text",
                            textContent = "Please inspect the config and summarize.",
                            isOutgoing = true
                        },
                        new
                        {
                            id = "skia-tool-1",
                            timestamp = timestamp.AddSeconds(1),
                            contentType = "tool_call",
                            title = ToolCallTitle,
                            textContent = "",
                            isOutgoing = false,
                            toolCallId = ToolCallId,
                            toolCallKind = "read",
                            toolCallStatus = "completed",
                            toolCallJson = "{\"path\":\"app.yaml\"}"
                        },
                        new
                        {
                            id = "skia-md-1",
                            timestamp = timestamp.AddSeconds(2),
                            contentType = "text",
                            textContent = string.Join(
                                "\n",
                                $"## {MarkdownMarker}",
                                "",
                                "Mixed transcript row with `inline code`.",
                                "",
                                "```csharp",
                                "var ready = true;",
                                "```",
                                "",
                                "- tool call precedes this markdown",
                                "- seed must restore via lastActiveConversationId"),
                            isOutgoing = false
                        },
                        new
                        {
                            id = "skia-mode-1",
                            timestamp = timestamp.AddSeconds(3),
                            contentType = "mode_change",
                            title = "Mode Changed",
                            modeId = "code",
                            isOutgoing = false
                        },
                        new
                        {
                            id = "skia-image-1",
                            timestamp = timestamp.AddSeconds(4),
                            contentType = "image",
                            imageData = "AAA=",
                            imageMimeType = "image/png",
                            isOutgoing = false
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    public static string BuildAppYaml(string projectRootPath)
    {
        return string.Join(
            Environment.NewLine,
            [
                "version: 1",
                "projects:",
                $"  - id: {ProjectId}",
                "    display_name: Skia Smoke Project",
                $"    path: '{projectRootPath.Replace("'", "''", StringComparison.Ordinal)}'",
                $"last_selected_project_id: {ProjectId}",
                string.Empty
            ]);
    }

    public sealed record SeedPaths(
        string AppDataRoot,
        string ConfigDirectory,
        string ConversationsDirectory,
        string ConversationsPath,
        string AppYamlPath,
        string ProjectRootPath)
    {
        public static SeedPaths Create(string appDataRoot)
        {
            var configDirectory = Path.Combine(appDataRoot, "config");
            var conversationsDirectory = Path.Combine(appDataRoot, "conversations");
            return new SeedPaths(
                appDataRoot,
                configDirectory,
                conversationsDirectory,
                Path.Combine(conversationsDirectory, "conversations.v1.json"),
                Path.Combine(configDirectory, "app.yaml"),
                Path.Combine(appDataRoot, "projects", "skia-smoke-project"));
        }
    }
}
