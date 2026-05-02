using System.Collections.ObjectModel;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat.PlanPanel;

public sealed class ChatPlanEntriesProjectionCoordinatorTests
{
    [Fact]
    public void Replace_CreatesProjectedEntries()
    {
        var coordinator = new ChatPlanEntriesProjectionCoordinator();

        var result = coordinator.Replace(
            new[]
            {
                new ConversationPlanEntrySnapshot { Content = "Step 1", Status = PlanEntryStatus.Pending, Priority = PlanEntryPriority.High },
                new ConversationPlanEntrySnapshot { Content = "Step 2", Status = PlanEntryStatus.Completed, Priority = PlanEntryPriority.Low }
            });

        Assert.Collection(
            result,
            entry =>
            {
                Assert.Equal("Step 1", entry.Content);
                Assert.Equal(PlanEntryStatus.Pending, entry.Status);
                Assert.Equal(PlanEntryPriority.High, entry.Priority);
            },
            entry =>
            {
                Assert.Equal("Step 2", entry.Content);
                Assert.Equal(PlanEntryStatus.Completed, entry.Status);
                Assert.Equal(PlanEntryPriority.Low, entry.Priority);
            });
    }

    [Fact]
    public void Sync_UpdatesExistingEntriesAndTrimsTail()
    {
        var coordinator = new ChatPlanEntriesProjectionCoordinator();
        var entries = new ObservableCollection<PlanEntryViewModel>
        {
            new() { Content = "Step 1", Status = PlanEntryStatus.Pending, Priority = PlanEntryPriority.Low },
            new() { Content = "Old tail", Status = PlanEntryStatus.Failed, Priority = PlanEntryPriority.High }
        };

        coordinator.Sync(
            entries,
            new[]
            {
                new ConversationPlanEntrySnapshot { Content = "Step 1", Status = PlanEntryStatus.Completed, Priority = PlanEntryPriority.High }
            });

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("Step 1", entry.Content);
                Assert.Equal(PlanEntryStatus.Completed, entry.Status);
                Assert.Equal(PlanEntryPriority.High, entry.Priority);
            });
    }

    [Fact]
    public void Observe_RaisesCallbackWhenCollectionChanges()
    {
        var coordinator = new ChatPlanEntriesProjectionCoordinator();
        var entries = new ObservableCollection<PlanEntryViewModel>();
        var raised = 0;

        coordinator.Observe(entries, () => raised++);
        entries.Add(new PlanEntryViewModel { Content = "Step 1" });

        Assert.Equal(1, raised);
    }
}
