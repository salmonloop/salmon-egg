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

    /// <summary>
    /// Multi-session stress seed for the left-nav gray-mask runtime probe. Writes N
    /// sessions under one project with deliberately staggered CatalogUpdatedAt timestamps
    /// so subsequent catalog ticks reorder the Children ObservableCollection while a
    /// selection exists — the precise trigger surface for the stranded-selection visual.
    /// Pair with SALMONEGG_NAV_MASK_PROBE=1 so the app self-drives activation across the
    /// set and audits the realized NavigationViewItem tree on every rebuild.
    /// </summary>
    public static SeedPaths WriteMultiSessionStressSeed(string appDataRoot, int sessionCount = 6)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        if (sessionCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionCount), "Stress seed requires at least two sessions.");
        }

        var paths = SeedPaths.Create(appDataRoot);
        Directory.CreateDirectory(paths.ConfigDirectory);
        Directory.CreateDirectory(paths.ConversationsDirectory);
        Directory.CreateDirectory(paths.ProjectRootPath);

        File.WriteAllText(paths.AppYamlPath, BuildAppYaml(paths.ProjectRootPath), Encoding.UTF8);
        File.WriteAllText(paths.ConversationsPath, BuildMultiSessionStressConversationsJson(paths.ProjectRootPath, sessionCount), Encoding.UTF8);

        return paths;
    }

    public static string BuildMultiSessionStressConversationsJson(string projectRootPath, int sessionCount)
    {
        // Base time and per-session offsets chosen so each session has a distinct,
        // monotonically increasing CatalogUpdatedAt. A later probe pass can bump these
        // (via a catalog refresh) to force a Move on the selected project's Children.
        var baseTime = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var conversations = new List<object>();
        for (var i = 0; i < sessionCount; i++)
        {
            var sessionId = $"skia-stress-session-{i + 1:D2}";
            var sessionTime = baseTime.AddMinutes(i);
            conversations.Add(new
            {
                conversationId = sessionId,
                displayName = $"Stress Session {i + 1}",
                createdAt = sessionTime,
                lastUpdatedAt = sessionTime,
                lastAccessedAt = sessionTime,
                cwd = projectRootPath,
                messages = new object[]
                {
                    new
                    {
                        id = $"{sessionId}-user-1",
                        timestamp = sessionTime,
                        contentType = "text",
                        textContent = $"Seed message for {sessionId}.",
                        isOutgoing = true
                    }
                }
            });
        }

        var document = new
        {
            version = 1,
            // Restore the first session so the app boots into a selected state.
            lastActiveConversationId = "skia-stress-session-01",
            conversations = conversations
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
