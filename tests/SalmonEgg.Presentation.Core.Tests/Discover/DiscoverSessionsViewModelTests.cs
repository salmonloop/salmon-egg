using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Models.Tool;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Discover;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Discover;

[Collection("NonParallel")]
public sealed class DiscoverSessionsViewModelTests
{
    [Fact]
    public async Task RefreshSessionsAsync_ProjectsNeedsMappingAndUnclassifiedAffinityStatesPerRow()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse
                    {
                        Sessions =
                        {
                            new AgentSessionInfo
                            {
                                SessionId = "remote-needs-mapping",
                                Title = "Needs Mapping",
                                Description = "No local project root match",
                                UpdatedAt = "2026-03-28T10:00:00+08:00",
                                Cwd = "/remote/worktree/service-a"
                            },
                            new AgentSessionInfo
                            {
                                SessionId = "remote-unclassified",
                                Title = "Unclassified",
                                Description = "No cwd from remote metadata",
                                UpdatedAt = "2026-03-28T10:05:00+08:00",
                                Cwd = string.Empty
                            }
                        }
                    }
                }
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new StubImportCoordinator(),
                new StubNavigationCoordinator());

            await viewModel.RefreshSessionsCommand.ExecuteAsync(null);

            Assert.Equal(DiscoverSessionsLoadPhase.Loaded, viewModel.LoadPhase);
            Assert.Equal(2, viewModel.AgentSessions.Count);

            var needsMappingRow = Assert.Single(viewModel.AgentSessions.Where(row => row.Id == "remote-needs-mapping"));
            Assert.Equal(ProjectAffinitySource.NeedsMapping, needsMappingRow.AffinitySource);
            Assert.Equal("Needs mapping", needsMappingRow.ProjectAffinityBadgeText);
            Assert.True(needsMappingRow.NeedsUserAttention);
            Assert.Contains("mapping", needsMappingRow.AffinityStatusText, StringComparison.OrdinalIgnoreCase);

            var unclassifiedRow = Assert.Single(viewModel.AgentSessions.Where(row => row.Id == "remote-unclassified"));
            Assert.Equal(ProjectAffinitySource.Unclassified, unclassifiedRow.AffinitySource);
            Assert.Equal("Unclassified", unclassifiedRow.ProjectAffinityBadgeText);
            Assert.False(unclassifiedRow.NeedsUserAttention);
            Assert.Contains("working directory", unclassifiedRow.AffinityStatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task RefreshSessionsAsync_WhenRemoteListIsEmpty_UsesEmptyPhaseInsteadOfError()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse()
                }
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new StubImportCoordinator(),
                new StubNavigationCoordinator());

            await viewModel.RefreshSessionsCommand.ExecuteAsync(null);

            Assert.Equal(DiscoverSessionsLoadPhase.Empty, viewModel.LoadPhase);
            Assert.True(viewModel.ShowEmptyState);
            Assert.False(viewModel.HasError);
            Assert.False(viewModel.IsListVisible);
            Assert.Empty(viewModel.AgentSessions);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhenImportFailsAfterAwait_MarshalsErrorStateThroughUiContext()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var importCoordinator = new DelayedImportCoordinator(
                async () =>
                {
                    await Task.Delay(10);
                    return new DiscoverSessionImportResult(false, null, "导入失败");
                });
            using var viewModel = CreateViewModel(
                profilesViewModel,
                new FakeDiscoverSessionsConnectionFacade(),
                importCoordinator,
                new StubNavigationCoordinator());

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);
            Assert.Equal("导入失败", viewModel.ErrorMessage);
            Assert.True(syncContext.PostCount > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_ActivatesImportedLocalConversationAndHydratesSharedChatFacade()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CountingSynchronizationContext());
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = true
            };
            var importCoordinator = new RecordingImportCoordinator(
                new DiscoverSessionImportResult(true, "local-conversation-1", null));
            var navigationCoordinator = new StubNavigationCoordinator
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(("remote-session-1", @"C:\repo\remote", "profile-1", "Remote Session"), importCoordinator.LastRequest);
            Assert.Equal(("local-conversation-1", null), navigationCoordinator.LastActivation);
            Assert.Equal(1, connectionFacade.HydrateCalls);
            Assert.Null(viewModel.ErrorMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhenSelectedProfileChangesWhilePending_UsesOriginalProfileBinding()
    {
        var syncContext = new InterceptingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile1 = CreateProfile();
            var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2" };
            var profilesViewModel = CreateProfilesViewModel(profile1);
            profilesViewModel.Profiles.Add(profile2);

            var importCoordinator = new RecordingImportCoordinator(
                new DiscoverSessionImportResult(false, null, "导入失败"));
            using var viewModel = CreateViewModel(
                profilesViewModel,
                new FakeDiscoverSessionsConnectionFacade(),
                importCoordinator,
                new StubNavigationCoordinator());

            syncContext.BeforeNextEnqueue = () => SetSelectedProfileWithoutNotification(profilesViewModel, profile2);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(
                ("remote-session-1", @"C:\repo\remote", "profile-1", "Remote Session"),
                importCoordinator.LastRequest);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhenHydrationFails_SetsPageErrorState()
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CountingSynchronizationContext());
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = false
            };
            var importCoordinator = new RecordingImportCoordinator(
                new DiscoverSessionImportResult(true, "local-conversation-1", null));
            var navigationCoordinator = new StubNavigationCoordinator
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.Equal(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);
            Assert.Equal("导入后的会话历史加载失败，请检查 ACP 连接状态。", viewModel.ErrorMessage);
            Assert.Equal(1, connectionFacade.HydrateCalls);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_AfterAsyncImport_MarshalsActivationAndHydrationBackToUiContext()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                HydrateResult = true,
                ExpectedSynchronizationContext = syncContext,
                RequireExpectedSynchronizationContextForHydrate = true
            };
            var importCoordinator = new DelayedImportCoordinator(async () =>
            {
                await Task.Delay(10);
                return new DiscoverSessionImportResult(true, "local-conversation-1", null);
            });
            var navigationCoordinator = new ContextAssertingNavigationCoordinator(syncContext)
            {
                ActivationResult = true
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                importCoordinator,
                navigationCoordinator);

            await viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            Assert.True(navigationCoordinator.WasCalledOnExpectedContext);
            Assert.True(connectionFacade.HydrateCalledOnExpectedContext);
            Assert.Null(viewModel.ErrorMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhileImportIsRunning_KeepsLifecycleLoadingVisible()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var allowImportCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importCoordinator = new DelayedImportCoordinator(async () =>
            {
                importStarted.TrySetResult(null);
                await allowImportCompletion.Task;
                return new DiscoverSessionImportResult(true, "local-conversation-1", null);
            });
            using var viewModel = CreateViewModel(
                profilesViewModel,
                new FakeDiscoverSessionsConnectionFacade
                {
                    HydrateResult = true
                },
                importCoordinator,
                new StubNavigationCoordinator
                {
                    ActivationResult = true
                });

            var loadTask = viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            await importStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在导入会话...", viewModel.LoadingStatus);

            allowImportCompletion.TrySetResult(null);
            await loadTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task LoadSessionAsync_WhileActivationAndHydrationAreRunning_KeepsLifecycleLoadingVisible()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile = CreateProfile();
            var profilesViewModel = CreateProfilesViewModel(profile);
            var allowActivationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowHydrationCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hydrationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse
                    {
                        Sessions =
                        {
                            new AgentSessionInfo
                            {
                                SessionId = "remote-session-1",
                                Title = "Remote Session",
                                Description = "Imported from ACP",
                                UpdatedAt = "2026-03-27T12:00:00+08:00",
                                Cwd = @"C:\repo\remote"
                            }
                        }
                    }
                },
                OnHydrateAsync = async cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hydrationStarted.TrySetResult(null);
                    await allowHydrationCompletion.Task.WaitAsync(cancellationToken);
                    return true;
                }
            };

            var navigationCoordinator = new DelayedNavigationCoordinator(async () =>
            {
                activationStarted.TrySetResult(null);
                await allowActivationCompletion.Task;
                return true;
            });

            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new RecordingImportCoordinator(new DiscoverSessionImportResult(true, "local-conversation-1", null)),
                navigationCoordinator);

            await viewModel.RefreshSessionsCommand.ExecuteAsync(null);
            Assert.Equal(DiscoverSessionsLoadPhase.Loaded, viewModel.LoadPhase);

            var loadTask = viewModel.LoadSessionCommand.ExecuteAsync(CreateSessionItem());

            await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在打开会话...", viewModel.LoadingStatus);

            allowActivationCompletion.TrySetResult(null);

            await hydrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(viewModel.IsLoading);
            Assert.Equal("正在加载会话历史...", viewModel.LoadingStatus);

            allowHydrationCompletion.TrySetResult(null);
            await loadTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void InitialState_IsWideAndList()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        Assert.Equal(DiscoverLayoutMode.Wide, viewModel.LayoutMode);
        Assert.Equal(DiscoverPaneMode.List, viewModel.ActivePaneMode);
        Assert.True(viewModel.ShowProfilesPane);
        Assert.True(viewModel.ShowDetailsPane);
        Assert.False(viewModel.ShowCompactBackButton);
    }

    [Fact]
    public void SetLayoutMode_Narrow_UpdatesShowProperties()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);

        Assert.Equal(DiscoverLayoutMode.Narrow, viewModel.LayoutMode);
        Assert.Equal(DiscoverPaneMode.List, viewModel.ActivePaneMode);
        Assert.True(viewModel.ShowProfilesPane);
        Assert.False(viewModel.ShowDetailsPane);
        Assert.False(viewModel.ShowCompactBackButton);
    }

    [Fact]
    public void OpenProfileDetails_InNarrowMode_MovesToDetailPane()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);
        viewModel.OpenProfileDetailsCommand.Execute(null);

        Assert.Equal(DiscoverPaneMode.Detail, viewModel.ActivePaneMode);
        Assert.False(viewModel.ShowProfilesPane);
        Assert.True(viewModel.ShowDetailsPane);
        Assert.True(viewModel.ShowCompactBackButton);
    }

    [Fact]
    public void BackToProfiles_InNarrowMode_ReturnsToListPane()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);
        viewModel.OpenProfileDetailsCommand.Execute(null);
        viewModel.BackToProfilesCommand.Execute(null);

        Assert.Equal(DiscoverPaneMode.List, viewModel.ActivePaneMode);
        Assert.True(viewModel.ShowProfilesPane);
        Assert.False(viewModel.ShowDetailsPane);
    }

    [Fact]
    public void SelectingProfile_InNarrowMode_MovesToDetailPane()
    {
        var profile1 = CreateProfile();
        var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2" };
        var profilesViewModel = CreateProfilesViewModel(profile1);
        profilesViewModel.Profiles.Add(profile2);
        
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);

        viewModel.SelectedProfile = profile2;

        Assert.Equal(DiscoverPaneMode.Detail, viewModel.ActivePaneMode);
        Assert.True(viewModel.ShowDetailsPane);
    }

    [Fact]
    public void ClearingProfileSelection_CoercesPaneToListMode()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);
        viewModel.OpenProfileDetailsCommand.Execute(null);

        viewModel.SelectedProfile = null;

        Assert.Equal(DiscoverPaneMode.List, viewModel.ActivePaneMode);
        Assert.True(viewModel.ShowProfilesPane);
    }

    [Fact]
    public void DiscoverSelection_IsLocallyOwned_AndNotOverwrittenBySharedProfileSelection()
    {
        var profile1 = CreateProfile();
        var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2" };
        var profilesViewModel = CreateProfilesViewModel(profile1);
        profilesViewModel.Profiles.Add(profile2);

        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SelectedProfile = profile2;

        Assert.Same(profile2, viewModel.SelectedProfile);
        Assert.Same(profile1, profilesViewModel.SelectedProfile);

        profilesViewModel.SelectedProfile = profile1;

        Assert.Same(profile2, viewModel.SelectedProfile);
    }

    [Fact]
    public void SwitchingToWideMode_ExposesBothPanes_PreservingSelection()
    {
        var profile = CreateProfile();
        var profilesViewModel = CreateProfilesViewModel(profile);
        using var viewModel = CreateViewModel(
            profilesViewModel,
            new FakeDiscoverSessionsConnectionFacade(),
            new StubImportCoordinator(),
            new StubNavigationCoordinator());

        viewModel.SetLayoutMode(DiscoverLayoutMode.Narrow);
        viewModel.OpenProfileDetailsCommand.Execute(null);
        
        // Act: switch back to wide
        viewModel.SetLayoutMode(DiscoverLayoutMode.Wide);

        Assert.Equal(DiscoverLayoutMode.Wide, viewModel.LayoutMode);
        Assert.True(viewModel.ShowProfilesPane);
        Assert.True(viewModel.ShowDetailsPane);
        Assert.Same(profile, viewModel.SelectedProfile);
    }

    [Fact]
    public async Task RefreshSessionsAsync_WhenProfileChangesDuringRefresh_DropsStaleResults()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile1 = new ServerConfiguration { Id = "profile-1", Name = "Profile 1" };
            var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2" };
            var profilesViewModel = CreateProfilesViewModel(profile1);
            profilesViewModel.Profiles.Add(profile2);
            DiscoverSessionsViewModel? viewModel = null;

            var allowProfile1Completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    OnListSessionsAsync = async (p, ct) =>
                    {
                        if (viewModel?.SelectedProfile?.Id == "profile-1")
                        {
                            await allowProfile1Completion.Task;
                            return new SessionListResponse
                            {
                                Sessions = { new AgentSessionInfo { SessionId = "stale-session", Title = "Stale" } }
                            };
                        }
                        return new SessionListResponse
                        {
                            Sessions = { new AgentSessionInfo { SessionId = "fresh-session", Title = "Fresh" } }
                        };
                    }
                }
            };

            using var vm = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new StubImportCoordinator(),
                new StubNavigationCoordinator());
            viewModel = vm;

            // Start refresh for profile 1
            var refresh1Task = vm.RefreshSessionsCommand.ExecuteAsync(null);

            // Change to profile 2 immediately
            vm.SelectedProfile = profile2;
            await vm.RefreshSessionsCommand.ExecuteAsync(null);

            // Allow profile 1 to complete
            allowProfile1Completion.TrySetResult(null);
            await refresh1Task;

            // Assert: profile 2's fresh sessions are current, stale ones were dropped
            var session = Assert.Single(vm.AgentSessions);
            Assert.Equal("fresh-session", session.Id);
            Assert.Equal(DiscoverSessionsLoadPhase.Loaded, vm.LoadPhase);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task OnConnectionFacadePropertyChanged_WhenProfileChanges_DropsStaleConnectionErrors()
    {
        var syncContext = new CountingSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var profile1 = new ServerConfiguration { Id = "profile-1", Name = "Profile 1" };
            var profile2 = new ServerConfiguration { Id = "profile-2", Name = "Profile 2" };
            var profilesViewModel = CreateProfilesViewModel(profile1);
            profilesViewModel.Profiles.Add(profile2);

            var connectionFacade = new FakeDiscoverSessionsConnectionFacade
            {
                CurrentChatService = new FakeChatService
                {
                    SessionListResponse = new SessionListResponse()
                }
            };
            using var viewModel = CreateViewModel(
                profilesViewModel,
                connectionFacade,
                new StubImportCoordinator(),
                new StubNavigationCoordinator());

            // Start refresh for profile 1 with a valid chat service so the test isolates stale facade
            // notifications instead of failing on missing connection prerequisites.
            var refresh1Task = viewModel.RefreshSessionsCommand.ExecuteAsync(null);
            await Task.Yield();
            Assert.NotEqual(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);

            // Switch to profile 2
            viewModel.SelectedProfile = profile2;
            
            // At this point, the connection gating generation has been incremented.
            // Simulate profile 1 connection error arriving late.
            // Real property changes from the facade happen on a background thread.
            connectionFacade.ConnectionErrorMessage = "Profile 1 error";

            // Assert: error did not overwrite loading state of profile 2
            // The gating logic in OnConnectionFacadePropertyChanged should catch the generation mismatch.
            Assert.NotEqual(DiscoverSessionsLoadPhase.Error, viewModel.LoadPhase);
            Assert.Null(viewModel.ErrorMessage);

            // Allow tests to clean up
            await refresh1Task;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static DiscoverSessionsViewModel CreateViewModel(
        AcpProfilesViewModel profilesViewModel,
        IDiscoverSessionsConnectionFacade connectionFacade,
        IDiscoverSessionImportCoordinator importCoordinator,
        INavigationCoordinator navigationCoordinator)
    {
        var projectPreferences = new NavigationProjectPreferencesAdapter(CreatePreferences());
        var uiDispatcher = SynchronizationContext.Current as IUiDispatcher ?? new ImmediateUiDispatcher();
        return new DiscoverSessionsViewModel(
            Mock.Of<ILogger<DiscoverSessionsViewModel>>(),
            navigationCoordinator,
            projectPreferences,
            profilesViewModel,
            connectionFacade,
            importCoordinator,
            uiDispatcher);
    }

    private static AcpProfilesViewModel CreateProfilesViewModel(ServerConfiguration profile)
    {
        var configurationService = new Mock<IConfigurationService>();
        var preferences = CreatePreferences();
        var profilesViewModel = new AcpProfilesViewModel(
            configurationService.Object,
            preferences,
            Mock.Of<ILogger<AcpProfilesViewModel>>(),
            new ImmediateUiDispatcher());
        profilesViewModel.Profiles.Add(profile);
        profilesViewModel.SelectedProfile = profile;
        return profilesViewModel;
    }

    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            new ImmediateUiDispatcher());
    }

    private static ServerConfiguration CreateProfile()
        => new()
        {
            Id = "profile-1",
            Name = "Demo Agent",
            Transport = TransportType.Stdio,
            StdioCommand = "agent.exe"
        };

    private static DiscoverSessionItemViewModel CreateSessionItem()
        => new(
            "remote-session-1",
            "Remote Session",
            "Imported from ACP",
            new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Local),
            @"C:\repo\remote");

    private static void SetSelectedProfileWithoutNotification(
        AcpProfilesViewModel profilesViewModel,
        ServerConfiguration? profile)
    {
        var field = typeof(AcpProfilesViewModel).GetField("_selectedProfile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(profilesViewModel, profile);
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);
        public bool HasThreadAccess => ReferenceEquals(Current, this);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            var originalContext = Current;
            try
            {
                SetSynchronizationContext(this);
                d(state);
            }
            finally
            {
                SetSynchronizationContext(originalContext);
            }
        }

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Post(_ => action(), null);
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await function().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }
    }

    private sealed class InterceptingSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        public bool HasThreadAccess => false;

        public Action? BeforeNextEnqueue { get; set; }

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            RunIntercepted(action);
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            RunIntercepted(action);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);
            return RunInterceptedAsync(function);
        }

        private void RunIntercepted(Action action)
        {
            var intercept = BeforeNextEnqueue;
            BeforeNextEnqueue = null;
            intercept?.Invoke();

            var originalContext = Current;
            try
            {
                SetSynchronizationContext(this);
                action();
            }
            finally
            {
                SetSynchronizationContext(originalContext);
            }
        }

        private async Task RunInterceptedAsync(Func<Task> function)
        {
            var intercept = BeforeNextEnqueue;
            BeforeNextEnqueue = null;
            intercept?.Invoke();

            var originalContext = Current;
            try
            {
                SetSynchronizationContext(this);
                await function().ConfigureAwait(false);
            }
            finally
            {
                SetSynchronizationContext(originalContext);
            }
        }
    }

    private sealed class FakeDiscoverSessionsConnectionFacade : IDiscoverSessionsConnectionFacade
    {
        private bool _isConnecting;
        private bool _isInitializing;
        private bool _isConnected;
        private string? _connectionErrorMessage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetProperty(ref _isConnecting, value, nameof(IsConnecting));
        }

        public bool IsInitializing
        {
            get => _isInitializing;
            private set => SetProperty(ref _isInitializing, value, nameof(IsInitializing));
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value, nameof(IsConnected));
        }

        public string? ConnectionErrorMessage
        {
            get => _connectionErrorMessage;
            set => SetProperty(ref _connectionErrorMessage, value, nameof(ConnectionErrorMessage));
        }

        public IChatService? CurrentChatService { get; set; }

        public bool HydrateResult { get; set; } = true;

        public int HydrateCalls { get; private set; }

        public SynchronizationContext? ExpectedSynchronizationContext { get; set; }

        public bool RequireExpectedSynchronizationContextForHydrate { get; set; }

        public bool HydrateCalledOnExpectedContext { get; private set; }

        public Func<CancellationToken, Task<bool>>? OnHydrateAsync { get; set; }

        public async Task ConnectToProfileAsync(ServerConfiguration profile)
        {
            IsConnecting = true;
            await Task.Yield();
            IsConnecting = false;
            IsInitializing = true;
            await Task.Yield();
            IsInitializing = false;
            IsConnected = true;
        }

        public Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HydrateCalls++;
            HydrateCalledOnExpectedContext = ReferenceEquals(SynchronizationContext.Current, ExpectedSynchronizationContext);
            if (RequireExpectedSynchronizationContextForHydrate && !HydrateCalledOnExpectedContext)
            {
                return Task.FromResult(false);
            }

            if (OnHydrateAsync != null)
            {
                return OnHydrateAsync(cancellationToken);
            }

            return Task.FromResult(HydrateResult);
        }

        private void SetProperty<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class RecordingImportCoordinator : IDiscoverSessionImportCoordinator
    {
        private readonly DiscoverSessionImportResult _result;

        public RecordingImportCoordinator(DiscoverSessionImportResult result)
        {
            _result = result;
        }

        public (string RemoteSessionId, string? RemoteSessionCwd, string? ProfileId, string? RemoteSessionTitle)? LastRequest { get; private set; }

        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = (remoteSessionId, remoteSessionCwd, profileId, remoteSessionTitle);
            return Task.FromResult(_result);
        }
    }

    private sealed class DelayedImportCoordinator : IDiscoverSessionImportCoordinator
    {
        private readonly Func<Task<DiscoverSessionImportResult>> _resultFactory;

        public DelayedImportCoordinator(Func<Task<DiscoverSessionImportResult>> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _resultFactory();
        }
    }

    private sealed class StubImportCoordinator : IDiscoverSessionImportCoordinator
    {
        public Task<DiscoverSessionImportResult> ImportAsync(
            string remoteSessionId,
            string? remoteSessionCwd,
            string? profileId,
            string? remoteSessionTitle = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new DiscoverSessionImportResult(true, "local-session", null));
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public bool ActivationResult { get; set; } = true;

        public (string SessionId, string? ProjectId)? LastActivation { get; private set; }

        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            LastActivation = (sessionId, projectId);
            return Task.FromResult(ActivationResult);
        }

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }

    }

    private sealed class DelayedNavigationCoordinator : INavigationCoordinator
    {
        private readonly Func<Task<bool>> _activation;

        public DelayedNavigationCoordinator(Func<Task<bool>> activation)
        {
            _activation = activation;
        }

        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => _activation();

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }

    }

    private sealed class ContextAssertingNavigationCoordinator : INavigationCoordinator
    {
        private readonly SynchronizationContext _expectedSynchronizationContext;

        public ContextAssertingNavigationCoordinator(SynchronizationContext expectedSynchronizationContext)
        {
            _expectedSynchronizationContext = expectedSynchronizationContext;
        }

        public bool ActivationResult { get; set; } = true;

        public bool WasCalledOnExpectedContext { get; private set; }

        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            WasCalledOnExpectedContext = ReferenceEquals(SynchronizationContext.Current, _expectedSynchronizationContext);
            return Task.FromResult(WasCalledOnExpectedContext && ActivationResult);
        }

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }

    }

    private sealed class FakeChatService : IChatService
    {
        public string? CurrentSessionId => null;

        public bool IsInitialized => true;

        public bool IsConnected => true;

        public AgentInfo? AgentInfo => null;

        public AgentCapabilities? AgentCapabilities => new(loadSession: true);

        public IReadOnlyList<SessionUpdateEntry> SessionHistory => Array.Empty<SessionUpdateEntry>();

        public Plan? CurrentPlan => null;

        public SessionModeState? CurrentMode => null;

        public SessionListResponse SessionListResponse { get; set; } = new();

        public event EventHandler<SessionUpdateEventArgs>? SessionUpdateReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<PermissionRequestEventArgs>? PermissionRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<FileSystemRequestEventArgs>? FileSystemRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalRequestEventArgs>? TerminalRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<TerminalStateChangedEventArgs>? TerminalStateChangedReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<AskUserRequestEventArgs>? AskUserRequestReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task<InitializeResponse> InitializeAsync(InitializeParams @params)
            => throw new NotSupportedException();

        public Task<SessionNewResponse> CreateSessionAsync(SessionNewParams @params)
            => throw new NotSupportedException();

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params)
            => LoadSessionAsync(@params, CancellationToken.None);

        public Task<SessionLoadResponse> LoadSessionAsync(SessionLoadParams @params, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Func<SessionListParams?, CancellationToken, Task<SessionListResponse>>? OnListSessionsAsync { get; set; }

        public Task<SessionListResponse> ListSessionsAsync(SessionListParams? @params = null, CancellationToken cancellationToken = default)
        {
            if (OnListSessionsAsync != null)
            {
                return OnListSessionsAsync(@params, cancellationToken);
            }
            return Task.FromResult(SessionListResponse);
        }

        public Task<SessionPromptResponse> SendPromptAsync(SessionPromptParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionSetModeResponse> SetSessionModeAsync(SessionSetModeParams @params)
            => throw new NotSupportedException();

        public Task<SessionSetConfigOptionResponse> SetSessionConfigOptionAsync(SessionSetConfigOptionParams @params)
            => throw new NotSupportedException();

        public Task<SessionCancelResponse> CancelSessionAsync(SessionCancelParams @params)
            => throw new NotSupportedException();

        public Task<AuthenticateResponse> AuthenticateAsync(AuthenticateParams @params, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> RespondToPermissionRequestAsync(object messageId, string outcome, string? optionId = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToFileSystemRequestAsync(object messageId, bool success, string? content = null, string? message = null)
            => throw new NotSupportedException();

        public Task<bool> RespondToAskUserRequestAsync(object messageId, IReadOnlyDictionary<string, string> answers)
            => throw new NotSupportedException();

        public Task<bool> DisconnectAsync()
            => throw new NotSupportedException();

        public Task<List<SalmonEgg.Domain.Models.Protocol.SessionMode>?> GetAvailableModesAsync()
            => throw new NotSupportedException();

        public void ClearHistory()
        {
        }
    }
}
