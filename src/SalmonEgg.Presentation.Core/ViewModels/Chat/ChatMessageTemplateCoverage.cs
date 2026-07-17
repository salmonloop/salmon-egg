using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// Authoritative inventory of chat content types produced by production projection.
/// Portable coverage tests use this to prove transcript rows cannot resolve to blank
/// templates under Skia/WinUI ListView materialization.
/// </summary>
public static class ChatMessageTemplateCoverage
{
    /// <summary>
    /// Content types that must be rendered by a dedicated DataTemplate (not directional
    /// text bubbles).
    /// </summary>
    public static IReadOnlyList<string> DedicatedTemplateContentTypes { get; } =
    [
        "tool_call"
    ];

    /// <summary>
    /// Content types that may legally use directional text bubbles when they always
    /// project a visible <see cref="ChatMessageViewModel.DisplayBodyText"/>.
    /// </summary>
    public static IReadOnlyList<string> DirectionalTextCompatibleContentTypes { get; } =
    [
        "text",
        "image",
        "audio",
        "resource",
        "resource_link",
        "resource_content",
        "mode_change",
        "plan_entry",
        "thinking"
    ];

    public static IReadOnlyList<string> AllProjectedContentTypes { get; } =
        DedicatedTemplateContentTypes
            .Concat(DirectionalTextCompatibleContentTypes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();

    public static bool RequiresDedicatedTemplate(string? contentType)
        => DedicatedTemplateContentTypes.Contains(contentType ?? string.Empty, StringComparer.Ordinal);

    public static ConversationMessageSnapshot CreateRepresentativeSnapshot(string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return contentType switch
        {
            "text" => new ConversationMessageSnapshot
            {
                Id = "cov-text",
                ContentType = "text",
                TextContent = "## coverage\n\n- item"
            },
            "tool_call" => new ConversationMessageSnapshot
            {
                Id = "cov-tool",
                ContentType = "tool_call",
                Title = "Read file",
                ToolCallId = "tool-1",
                ToolCallKind = SalmonEgg.Acp.Tool.ToolCallKind.Read,
                ToolCallStatus = SalmonEgg.Acp.Tool.ToolCallStatus.Completed
            },
            "image" => new ConversationMessageSnapshot
            {
                Id = "cov-image",
                ContentType = "image",
                ImageData = "AAA=",
                ImageMimeType = "image/png"
            },
            "audio" => new ConversationMessageSnapshot
            {
                Id = "cov-audio",
                ContentType = "audio",
                AudioData = "AAA=",
                AudioMimeType = "audio/wav"
            },
            "resource" => new ConversationMessageSnapshot
            {
                Id = "cov-resource",
                ContentType = "resource",
                TextContent = "file:///tmp/resource"
            },
            "resource_link" => new ConversationMessageSnapshot
            {
                Id = "cov-resource-link",
                ContentType = "resource_link",
                TextContent = "https://example.test/resource"
            },
            "resource_content" => new ConversationMessageSnapshot
            {
                Id = "cov-resource-content",
                ContentType = "resource_content",
                Title = "Resource Content"
            },
            "mode_change" => new ConversationMessageSnapshot
            {
                Id = "cov-mode",
                ContentType = "mode_change",
                ModeId = "code",
                Title = "Mode Changed"
            },
            "plan_entry" => new ConversationMessageSnapshot
            {
                Id = "cov-plan",
                ContentType = "plan_entry",
                Title = "Plan step",
                PlanEntry = new ConversationPlanEntrySnapshot
                {
                    Content = "Plan step",
                    Status = SalmonEgg.Acp.Plan.PlanEntryStatus.Pending,
                    Priority = SalmonEgg.Acp.Plan.PlanEntryPriority.Medium
                }
            },
            "thinking" => new ConversationMessageSnapshot
            {
                Id = "cov-thinking",
                ContentType = "thinking",
                TextContent = "Thinking…"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Unknown projected content type.")
        };
    }

    public static bool ExpectsVisibleDirectionalBody(ChatMessageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (RequiresDedicatedTemplate(viewModel.ContentType))
        {
            return false;
        }

        return viewModel.HasDisplayBody;
    }
}
