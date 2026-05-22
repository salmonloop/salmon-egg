using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Tool;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;

public sealed class TaskOverviewChangeProjectionCoordinator
{
    public IReadOnlyList<TaskOverviewChangeViewModel> Project(IEnumerable<ConversationMessageSnapshot> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .SelectMany(static message => message.ToolCallContent ?? Enumerable.Empty<ToolCallContent>())
            .OfType<DiffToolCallContent>()
            .Where(IsStableAcpDiff)
            .Select(CreateChange)
            .ToArray();
    }

    private static TaskOverviewChangeViewModel CreateChange(DiffToolCallContent diff)
    {
        var added = CountLines(diff.NewText);
        var removed = CountLines(diff.OldText);
        return new TaskOverviewChangeViewModel
        {
            Path = diff.Path ?? string.Empty,
            Kind = ResolveKind(diff),
            LineSummary = $"+{added} / -{removed}"
        };
    }

    private static TaskOverviewChangeKind ResolveKind(DiffToolCallContent diff)
        => diff.OldText is null
            ? TaskOverviewChangeKind.Added
            : TaskOverviewChangeKind.Modified;

    private static bool IsStableAcpDiff(DiffToolCallContent diff)
        => !string.IsNullOrWhiteSpace(diff.Path)
            && Path.IsPathFullyQualified(diff.Path)
            && diff.NewText is not null;

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var lineBreaks = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                lineBreaks++;
            }
        }

        return text[^1] == '\n' ? lineBreaks : lineBreaks + 1;
    }
}
