using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Elicitation;

public sealed class UrlElicitationInteractionTests
{
    [Theory]
    [InlineData(ExternalUriOpenResult.Opened)]
    [InlineData(ExternalUriOpenResult.Dispatched)]
    public async Task Submit_UserConsent_OpensOnceAndAcceptsWithoutContent(ExternalUriOpenResult outcome)
    {
        // Arrange
        var launcher = new Launcher { Outcome = outcome };
        var responses = new List<ElicitationAcceptContent?>();
        using var request = Create(launcher, content => { responses.Add(content); return Task.FromResult(true); });

        // Assert: creating and inspecting the request never navigates or responds.
        Assert.Equal(0, launcher.Opens);
        Assert.Empty(responses);
        Assert.Equal("https://xn--bcher-kva.example/authorize?canary=private", request.FullUrl);
        Assert.Equal("xn--bcher-kva.example", request.UrlHost);
        Assert.True(request.HasUrlWarning);

        // Act
        await request.SubmitCommand.ExecuteAsync(null);
        await request.SubmitCommand.ExecuteAsync(null);
        await request.CancelCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(1, launcher.Opens);
        Assert.Null(Assert.Single(responses));
        Assert.True(request.IsAwaitingCompletion);
        Assert.False(request.CanSubmit);
        Assert.False(request.IsCompleted);
        Assert.Contains("Opening was requested", request.UrlStatus);
        Assert.DoesNotContain("browser is open", request.UrlStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reopen_ExplicitSecondConsent_NavigatesWithoutAnotherProtocolResponse()
    {
        // Arrange
        var launcher = new Launcher { Outcome = ExternalUriOpenResult.Dispatched };
        var accepted = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); });
        await request.ReopenCommand.ExecuteAsync(null);
        Assert.Equal(0, launcher.Opens);

        // Act
        await request.SubmitCommand.ExecuteAsync(null);
        await request.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(1, launcher.Opens);
        await request.ReopenCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, launcher.Opens);
        Assert.Equal(1, accepted);
        Assert.False(request.IsCompleted);
        request.Dispose();
        await request.ReopenCommand.ExecuteAsync(null);
        Assert.Equal(2, launcher.Opens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Respond_DeclineOrCancel_NeverOpens(bool decline)
    {
        // Arrange
        var launcher = new Launcher();
        var accepted = 0;
        var declined = 0;
        var cancelled = 0;
        var cleared = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); },
            () => { declined++; return Task.FromResult(true); },
            () => { cancelled++; return Task.FromResult(true); },
            _ => { cleared++; return Task.CompletedTask; });

        // Act
        await (decline ? request.DeclineCommand : request.CancelCommand).ExecuteAsync(null);
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, launcher.Opens);
        Assert.Equal(0, accepted);
        Assert.Equal(decline ? 1 : 0, declined);
        Assert.Equal(decline ? 0 : 1, cancelled);
        Assert.Equal(1, cleared);
    }

    [Theory]
    [InlineData(ExternalUriOpenResult.Blocked)]
    [InlineData(ExternalUriOpenResult.Failed)]
    [InlineData(ExternalUriOpenResult.Unavailable)]
    public async Task Submit_OpenFails_DoesNotAcceptAndAllowsExplicitRetry(ExternalUriOpenResult outcome)
    {
        // Arrange
        var launcher = new Launcher { Outcome = outcome };
        var accepted = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); });

        // Act
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, accepted);
        Assert.True(request.HasError);
        Assert.False(request.IsAwaitingCompletion);
        launcher.Outcome = ExternalUriOpenResult.Opened;
        await request.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(1, accepted);
        Assert.Equal(2, launcher.Opens);
    }

    [Fact]
    public async Task Submit_ResponseFails_RetriesReplyWithoutReopening()
    {
        // Arrange
        var launcher = new Launcher();
        var accepted = 0;
        using var request = Create(launcher, _ => Task.FromResult(++accepted > 1));

        // Act
        await request.SubmitCommand.ExecuteAsync(null);
        Assert.Equal("Retry response", request.SubmitText);
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(1, launcher.Opens);
        Assert.Equal(2, accepted);
        Assert.True(request.IsAwaitingCompletion);
    }

    [Fact]
    public async Task Submit_ConcurrentCommands_SendsOneResponseAndOpensOnce()
    {
        // Arrange
        var opened = new TaskCompletionSource<ExternalUriOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new Launcher { Pending = opened.Task };
        var accepted = 0;
        var cancelled = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); },
            cancel: () => { cancelled++; return Task.FromResult(true); });

        // Act
        var first = request.SubmitCommand.ExecuteAsync(null);
        await request.SubmitCommand.ExecuteAsync(null);
        await request.CancelCommand.ExecuteAsync(null);
        opened.SetResult(ExternalUriOpenResult.Opened);
        await first;

        // Assert
        Assert.Equal(1, launcher.Opens);
        Assert.Equal(1, accepted);
        Assert.Equal(0, cancelled);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com/")]
    public async Task Submit_InvalidUrl_NeverOpensOrAccepts(string url)
    {
        // Arrange
        var launcher = new Launcher();
        var accepted = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); }, url: url);

        // Act
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.False(request.CanSubmit);
        Assert.True(request.HasError);
        Assert.Equal(0, launcher.Opens);
        Assert.Equal(0, accepted);
    }

    [Fact]
    public async Task Submit_DisposedProjection_NeverOpensOrAccepts()
    {
        // Arrange
        var launcher = new Launcher();
        var accepted = 0;
        var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); });
        request.Dispose();

        // Act
        await request.SubmitCommand.ExecuteAsync(null);

        // Assert
        Assert.False(request.CanSubmit);
        Assert.Equal(0, launcher.Opens);
        Assert.Equal(0, accepted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveConversation_PendingOrAcceptedUrl_CannotNavigateFromStaleCommands(bool acceptedFirst)
    {
        // Arrange
        var panels = new ChatConversationPanelStateCoordinator();
        var launcher = new Launcher();
        var accepted = 0;
        using var request = Create(launcher, _ => { accepted++; return Task.FromResult(true); });
        Assert.True(panels.TryStoreElicitationRequest("first", request));
        if (acceptedFirst)
        {
            await request.SubmitCommand.ExecuteAsync(null);
        }

        // Act
        panels.RemoveConversation("first", isCurrentConversation: false);
        await request.SubmitCommand.ExecuteAsync(null);
        await request.ReopenCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(request.FullUrl);
        Assert.Null(panels.GetPendingElicitationRequest("first"));
        Assert.Equal(acceptedFirst ? 1 : 0, launcher.Opens);
        Assert.Equal(acceptedFirst ? 1 : 0, accepted);
    }

    [Fact]
    public async Task NewRequest_AfterUrlConsent_ReplacesNoticeAndExpiresOldReopenCommand()
    {
        // Arrange
        var panels = new ChatConversationPanelStateCoordinator();
        var launcher = new Launcher();
        using var previous = Create(launcher, _ => Task.FromResult(true));
        using var current = Create(launcher, _ => Task.FromResult(true));
        Assert.True(panels.TryStoreElicitationRequest("first", previous));
        Assert.False(panels.TryStoreElicitationRequest("first", current));
        await previous.SubmitCommand.ExecuteAsync(null);

        // Act
        Assert.True(panels.TryStoreElicitationRequest("first", current));
        await previous.ReopenCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(previous.FullUrl);
        Assert.Same(current, panels.SyncConversation("first").PendingElicitationRequest);
        Assert.Equal(1, launcher.Opens);
        Assert.False(panels.RemoveElicitationRequest("first", previous));
    }

    private static ElicitationRequestViewModel Create(
        Launcher launcher,
        Func<ElicitationAcceptContent?, Task<bool>> accept,
        Func<Task<bool>>? decline = null,
        Func<Task<bool>>? cancel = null,
        Func<ElicitationRequestViewModel, Task>? clear = null,
        string url = "https://xn--bcher-kva.example/authorize?canary=private")
        => ElicitationInteractionViewModelFactory.Create(new ElicitationRequestEventArgs(
            "request-1", new UrlElicitationRequest
            {
                Scope = ElicitationScope.ForSession("session-1"),
                Message = "Authorize access",
                ElicitationId = "opaque-1",
                Url = url
            }, accept, decline ?? (() => Task.FromResult(true)), cancel ?? (() => Task.FromResult(true))),
            clear ?? (_ => Task.CompletedTask), uriLauncher: launcher);

    private sealed class Launcher : IExternalUriLauncher
    {
        public bool IsSupported => true;
        public int Opens { get; private set; }
        public ExternalUriOpenResult Outcome { get; set; } = ExternalUriOpenResult.Opened;
        public Task<ExternalUriOpenResult>? Pending { get; set; }

        public Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken)
        {
            Opens++;
            return Pending ?? Task.FromResult(Outcome);
        }
    }
}
