using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task StoreTranscript_WhenImageHasNoTextContent_ProjectsLocalizedMediaPlaceholder()
    {
        var syncContext = new QueueingSynchronizationContext();
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "ChatMedia_ImagePlaceholderWithMime", "[图片: {0}]");
        await using var fixture = CreateViewModel(syncContext, localizer: localizer);
        syncContext.RunAll();

        var snapshot = new ConversationMessageSnapshot
        {
            Id = "img-1",
            ContentType = "image",
            ImageMimeType = "image/png",
            TextContent = string.Empty,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Transcript = ImmutableList<ConversationMessageSnapshot>.Empty.Add(snapshot)
        });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        syncContext.RunAll();

        var message = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("[图片: image/png]", message.TextContent);
        Assert.Equal("[图片: image/png]", message.DisplayBodyText);
    }

    [Fact]
    public async Task StoreTranscript_WhenAudioHasNoTextContent_ProjectsLocalizedMediaPlaceholder()
    {
        var syncContext = new QueueingSynchronizationContext();
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "ChatMedia_AudioPlaceholder", "[音频]");
        await using var fixture = CreateViewModel(syncContext, localizer: localizer);
        syncContext.RunAll();

        var snapshot = new ConversationMessageSnapshot
        {
            Id = "audio-1",
            ContentType = "audio",
            AudioMimeType = string.Empty,
            TextContent = string.Empty,
            Timestamp = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "session-1",
            Transcript = ImmutableList<ConversationMessageSnapshot>.Empty.Add(snapshot)
        });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        syncContext.RunAll();

        var message = Assert.Single(fixture.ViewModel.MessageHistory);
        Assert.Equal("[音频]", message.TextContent);
        Assert.Equal("[音频]", message.DisplayBodyText);
    }
}
