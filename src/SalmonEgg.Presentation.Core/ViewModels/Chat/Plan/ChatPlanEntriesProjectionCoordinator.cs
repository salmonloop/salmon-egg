using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;

public sealed class ChatPlanEntriesProjectionCoordinator
{
    private ObservableCollection<PlanEntryViewModel>? _observedPlanEntries;
    private Action? _onEntriesChanged;

    public ObservableCollection<PlanEntryViewModel> Replace(
        IReadOnlyList<ConversationPlanEntrySnapshot>? planEntries)
    {
        var entries = planEntries ?? Array.Empty<ConversationPlanEntrySnapshot>();
        return new ObservableCollection<PlanEntryViewModel>(CreateEntries(entries));
    }

    public void Sync(
        ObservableCollection<PlanEntryViewModel> target,
        IReadOnlyList<ConversationPlanEntrySnapshot>? planEntries)
    {
        ArgumentNullException.ThrowIfNull(target);

        var entries = planEntries ?? Array.Empty<ConversationPlanEntrySnapshot>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var content = entry.Content ?? string.Empty;
            if (i < target.Count)
            {
                if (target[i].Content != content)
                {
                    while (target.Count > i)
                    {
                        target.RemoveAt(i);
                    }

                    target.Add(CreateEntry(entry));
                }
                else
                {
                    target[i].Status = entry.Status;
                    target[i].Priority = entry.Priority;
                }
            }
            else
            {
                target.Add(CreateEntry(entry));
            }
        }

        while (target.Count > entries.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    public void Observe(
        ObservableCollection<PlanEntryViewModel>? planEntries,
        Action? onEntriesChanged)
    {
        if (_observedPlanEntries != null)
        {
            _observedPlanEntries.CollectionChanged -= OnPlanEntriesCollectionChanged;
        }

        _observedPlanEntries = planEntries;
        _onEntriesChanged = onEntriesChanged;

        if (_observedPlanEntries != null)
        {
            _observedPlanEntries.CollectionChanged += OnPlanEntriesCollectionChanged;
        }
    }

    private static IEnumerable<PlanEntryViewModel> CreateEntries(
        IReadOnlyList<ConversationPlanEntrySnapshot> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            yield return CreateEntry(entries[i]);
        }
    }

    private static PlanEntryViewModel CreateEntry(ConversationPlanEntrySnapshot entry)
        => new()
        {
            Content = entry.Content ?? string.Empty,
            Status = entry.Status,
            Priority = entry.Priority
        };

    private void OnPlanEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _onEntriesChanged?.Invoke();
}
