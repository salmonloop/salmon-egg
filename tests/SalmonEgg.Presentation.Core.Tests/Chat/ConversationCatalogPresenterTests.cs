using System;
using System.Collections.Generic;
using System.ComponentModel;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

[Collection("NonParallel")]
public sealed class ConversationCatalogPresenterTests
{
    [Fact]
    public void Refresh_WhenCallerIsOffUiThread_DefersSnapshotPublicationUntilDispatcherRuns()
    {
        var dispatcher = new QueueingUiDispatcher();
        var presenter = new ConversationCatalogPresenter(dispatcher);
        var changedProperties = new List<string>();
        presenter.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                changedProperties.Add(e.PropertyName!);
            }
        };

        var snapshot = new[]
        {
            new ConversationCatalogItem(
                "conv-1",
                "Conversation 1",
                @"C:\Repo\Alpha",
                new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 13, 10, 5, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 13, 10, 10, 0, DateTimeKind.Utc),
                ProjectAffinityOverrideProjectId: "project-a")
        };

        presenter.Refresh(snapshot);

        Assert.Empty(presenter.Snapshot);
        Assert.Equal(0, presenter.ConversationListVersion);
        Assert.Empty(changedProperties);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunAll();

        Assert.Equal(snapshot, presenter.Snapshot);
        Assert.Equal(1, presenter.ConversationListVersion);
        Assert.Contains(nameof(IConversationCatalogReadModel.Snapshot), changedProperties);
        Assert.Contains(nameof(IConversationCatalogReadModel.ConversationListVersion), changedProperties);
    }
}
