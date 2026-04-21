using System;
using System.Threading;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SalmonEgg.Application.Services;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Logging;
using SalmonEgg.Infrastructure.Network;
using SalmonEgg.Infrastructure.Serialization;
using SalmonEgg.Infrastructure.Services;
using SalmonEgg.Infrastructure.Services.Security;
using SalmonEgg.Infrastructure.Storage;
using SalmonEgg.Infrastructure.Transport;
using SalmonEgg.Infrastructure.Client;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SalmonEgg.Presentation.ViewModels.Start;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Mvux.ShellLayout;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Search;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Services.Input;
#if WINDOWS
#endif

namespace SalmonEgg;

/// <summary>
    /// Dependency injection container configuration
/// Requirements: 7.5
/// </summary>
public static class DependencyInjection
{
    private const string GuiEnabledEnvVar = "SALMONEGG_GUI";
    private const string GuiSlowSessionLoadMsEnvVar = "SALMONEGG_GUI_SLOW_SESSION_LOAD_MS";

    /// <summary>
    /// Configures all services and dependencies
    /// </summary>
    public static IServiceCollection AddSalmonEgg(this IServiceCollection services)
    {
        ConfigureLogging(services);
        RegisterDomainServices(services);
        RegisterInfrastructureServices(services);
        return services;
    }


    private static void ConfigureLogging(IServiceCollection services)
    {
        var appDataPath = GetAppDataPath();
        var logger = LoggingConfiguration.ConfigureLogging(appDataPath);
        Log.Logger = logger;
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });
        services.AddSingleton(logger);
    }

    private static void RegisterDomainServices(IServiceCollection services)
    {
        // ACP Protocol Services
        services.AddSingleton<IAcpProtocolService, AcpMessageParser>();

        // Message Parser and Validator
        services.AddSingleton<IMessageParser, MessageParser>();
        services.AddSingleton<IMessageValidator, MessageValidator>();

        // Session Manager
        services.AddSingleton<ISessionManager, Infrastructure.Services.SessionManager>();

        // Path Validator
        services.AddSingleton<IPathValidator, Infrastructure.Services.Security.PathValidator>();

        // Permission Manager
        services.AddSingleton<IPermissionManager, Infrastructure.Services.Security.PermissionManager>();

        // Error Logger
        services.AddSingleton<IErrorLogger, ErrorLogger>();

        // Connection Manager (factory method supporting dynamic transport selection)
        services.AddSingleton<IConnectionManager>(sp =>
        	{
        				var protocolService = sp.GetRequiredService<IAcpProtocolService>();
        				var logger = sp.GetRequiredService<Serilog.ILogger>();

        				Infrastructure.Network.ITransport TransportFactory(TransportType type)
        				{
        					var l = sp.GetRequiredService<Serilog.ILogger>();
        					return type switch
        					{
        						TransportType.HttpSse => new HttpSseTransport(l),
        						_ => new WebSocketTransport(l)
        					};
        				}

        				return new ConnectionManager(protocolService, logger, TransportFactory);
        			});
    }

    private static void RegisterInfrastructureServices(IServiceCollection services)
    {
        // Infrastructure Services
        services.AddSingleton<IUiDispatcher>(sp =>
        {
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var logger = sp.GetRequiredService<ILogger<WinUiDispatcher>>();
            if (queue == null)
            {
                logger.LogCritical("DispatcherQueue.GetForCurrentThread() returned null during IUiDispatcher resolution. UI marshaling will fail.");
            }
            return new WinUiDispatcher(queue!, logger);
        });
#if WINDOWS
        services.AddSingleton<WindowsRawGameControllerMapper>();
        services.AddSingleton<IGamepadInputService, WindowsGamepadInputService>();
#elif __ANDROID__
        services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
#else
        services.AddSingleton<IGamepadInputService, NoOpGamepadInputService>();
#endif

#if WINDOWS
        services.AddSingleton<IVoiceInputService, NativeVoiceInputService>();
#else
        services.AddSingleton<IVoiceInputService>(NoOpVoiceInputService.Instance);
#endif
        services.AddSingleton<IGamepadNavigationDispatcher, MainShellGamepadNavigationDispatcher>();

        // Secure Storage
        services.AddSingleton<ISecureStorage, SecureStorage>();

        // App settings (config/app.yaml)
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();
        services.AddSingleton<IAppDocumentService, AppDocumentService>();
        services.AddSingleton<IConversationStore, ConversationStore>();
        services.AddSingleton<IPlatformCapabilityService, PlatformCapabilityService>();
        services.AddSingleton<IPlatformIconService, PlatformIconService>();
        services.AddSingleton<IAppStartupService, AppStartupService>();
        services.AddSingleton<IAppLanguageService, AppLanguageService>();
        services.AddSingleton<IConfigurationService, ConfigurationManager>();
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();
        services.AddSingleton<SalmonEgg.Domain.Interfaces.ITransportFactory, TransportFactory>();
        services.AddSingleton<IDiagnosticsBundleService, SalmonEgg.Infrastructure.Services.DiagnosticsBundleService>();
        services.AddSingleton<ILiveLogStreamService, SalmonEgg.Infrastructure.Services.LiveLogStreamService>();
        services.AddSingleton<IPlatformShellService, SalmonEgg.Infrastructure.Services.PlatformShellService>();
        services.AddSingleton<IConversationPreviewStore, ConversationPreviewStore>();

        services.AddSingleton<IState<ChatState>>(sp => State.Value(sp, () => ChatState.Empty));
        services.AddSingleton<IChatStore, ChatStore>();
        services.AddSingleton<IState<ChatConnectionState>>(sp => State.Value(sp, () => ChatConnectionState.Empty));
        services.AddSingleton<IChatConnectionStore, ChatConnectionStore>();
        services.AddSingleton<IAcpConnectionCoordinator>(sp =>
            new AcpConnectionCoordinator(
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<AcpConnectionCoordinator>>()));
        services.AddSingleton(sp =>
            AcpConnectionEvictionOptionsLoader.Load(
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AcpConnectionEvictionOptionsLoader")));
        services.AddSingleton<AcpConnectionEvictionOptionsBridge>();
        services.AddSingleton<IAcpConnectionEvictionPolicy>(sp =>
            new ConservativeAcpConnectionEvictionPolicy(
                sp.GetRequiredService<AcpConnectionEvictionOptions>()));
        services.AddSingleton<IAcpConnectionSessionCleaner>(sp =>
            new AcpConnectionSessionCleaner(
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionEvictionPolicy>(),
                sp.GetRequiredService<AcpConnectionEvictionOptions>(),
                sp.GetRequiredService<ILogger<AcpConnectionSessionCleaner>>()));
        services.AddSingleton<IAcpConnectionPoolManager>(sp =>
            new AcpConnectionPoolManager(
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionCleaner>(),
                sp.GetRequiredService<ILogger<AcpConnectionPoolManager>>()));
        services.AddSingleton<IAcpSessionCommandOrchestrator>(sp =>
            new AcpSessionCommandOrchestrator(
                sp.GetRequiredService<ILogger<AcpSessionCommandOrchestrator>>()));

        services.AddSingleton<IShellLayoutStore>(sp =>
        {
            var initialState = ShellLayoutState.Default;
            var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
            var state = State.Value(sp, () => initialState);
            var snapshot = State.Value(sp, () => initialSnapshot);
            return new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);
        });
        services.AddSingleton<IShellLayoutMetricsSink, ShellLayoutMetricsSink>();
        services.AddTransient<ConnectToServerUseCase>();
        services.AddTransient<DisconnectUseCase>();
        services.AddTransient<SendMessageUseCase>();
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IMessageService, MessageService>();

        var chatServiceDecorator = CreateChatServiceDecorator();
        services.AddSingleton<ChatServiceFactory>(sp =>
        {
            var transportFactory = sp.GetRequiredService<ITransportFactory>();
            var parser = sp.GetRequiredService<IMessageParser>();
            var validator = sp.GetRequiredService<IMessageValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChatServiceFactory(
                transportFactory,
                parser,
                validator,
                errorLogger,
                sessionManager,
                logger,
                chatServiceDecorator);
        });
        services.AddTransient<ConfigurationEditorViewModel>();
        services.AddSingleton<IConversationWorkspacePreferences>(sp =>
            new AppPreferencesConversationWorkspacePreferences(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<IChatStateProjector, ChatStateProjector>();
        services.AddSingleton<IAcpSessionUpdateProjector, AcpSessionUpdateProjector>();

        // New Chat ViewModel (refactored)
        // Must be singleton so connection/session state survives navigation and is shared between Settings and Chat pages.
        services.AddSingleton<ConversationCatalogPresenter>();
        services.AddSingleton<IConversationCatalogReadModel>(sp =>
            sp.GetRequiredService<ConversationCatalogPresenter>());
        services.AddSingleton<IState<ConversationAttentionState>>(sp => State.Value(sp, () => ConversationAttentionState.Empty));
        services.AddSingleton<IConversationAttentionStore, ConversationAttentionStore>();
        services.AddSingleton<ConversationCatalogDisplayPresenter>();
        services.AddSingleton<IConversationCatalogDisplayReadModel>(sp =>
            sp.GetRequiredService<ConversationCatalogDisplayPresenter>());
        services.AddSingleton<IProjectAffinityResolver, ProjectAffinityResolver>();
        services.AddSingleton<INavigationProjectPreferences>(sp =>
            new NavigationProjectPreferencesAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<INavigationProjectSelectionStore>(sp =>
            new NavigationProjectSelectionStoreAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        // ACP chat service factory — adapts ChatServiceFactory to the IAcpChatServiceFactory seam
        // used by AcpChatCoordinator.
        services.AddSingleton<IAcpChatServiceFactory>(sp =>
            new AcpChatServiceFactoryAdapter(sp.GetRequiredService<ChatServiceFactory>()));
        services.AddSingleton<IAcpConnectionCommands>(sp =>
        {
            _ = sp.GetRequiredService<AcpConnectionEvictionOptionsBridge>();
            return new AcpChatCoordinator(
                sp.GetRequiredService<IAcpChatServiceFactory>(),
                sp.GetRequiredService<ILogger<AcpChatCoordinator>>(),
                sp.GetRequiredService<IAcpConnectionCoordinator>(),
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionCleaner>(),
                sp.GetRequiredService<IAcpConnectionPoolManager>(),
                sp.GetRequiredService<IAcpSessionCommandOrchestrator>());
        });
        services.AddSingleton<IChatService>(sp =>
        {
            var factory = sp.GetRequiredService<ChatServiceFactory>();
            return factory.CreateDefaultChatService();
        });
        services.AddSingleton<IErrorRecoveryService>(sp =>
        {
            var chatService = sp.GetRequiredService<IChatService>();
            var pathValidator = sp.GetRequiredService<IPathValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            return new ErrorRecoveryService(chatService, pathValidator, errorLogger);
        });

        services.AddSingleton<ChatViewModel>(sp =>
        {
            var dispatcher = sp.GetRequiredService<IUiDispatcher>();
            return ActivatorUtilities.CreateInstance<ChatViewModel>(
                sp,
                dispatcher,
                sp.GetRequiredService<IShellNavigationRuntimeState>());
        });
        services.AddSingleton<IConversationSessionSwitcher>(sp => sp.GetRequiredService<ChatViewModel>());

        services.AddSingleton<ChatShellViewModel>();
        services.AddSingleton<ShellSessionActivationOverlayViewModel>();
        services.AddSingleton<ConversationCatalogFacade>();
        services.AddSingleton<IConversationCatalog>(sp => sp.GetRequiredService<ConversationCatalogFacade>());
        services.AddSingleton<IDiscoverSessionsConnectionFacade>(sp =>
            new DiscoverSessionsConnectionFacade(
                sp.GetRequiredService<IAcpChatServiceFactory>(),
                sp.GetRequiredService<ChatViewModel>().HydrateActiveConversationAsync,
                sp.GetRequiredService<ILogger<DiscoverSessionsConnectionFacade>>()));
        services.AddSingleton<IDiscoverSessionImportCoordinator>(sp =>
            new DiscoverSessionImportCoordinator(
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IConversationBindingCommands>(),
                sp.GetRequiredService<ILogger<DiscoverSessionImportCoordinator>>()));
        services.AddSingleton<ISettingsChatConnection>(sp =>
            new SettingsChatConnectionAdapter(sp.GetRequiredService<ChatViewModel>()));
        services.AddSingleton<IChatLaunchWorkflowChatFacade>(sp =>
            new ChatLaunchWorkflowChatFacadeAdapter(
                sp.GetRequiredService<ChatViewModel>(),
                sp.GetRequiredService<IChatConnectionStore>()));
        services.AddSingleton<IChatSessionCatalog>(sp =>
            new ChatViewModelSessionCatalogAdapter(sp.GetRequiredService<IConversationCatalog>()));

        // Extracted workspace is still registered so ChatViewModel can delegate local conversation state.
        services.AddSingleton<ChatConversationWorkspace>();
        services.AddSingleton<BindingCoordinator>(sp =>
            new BindingCoordinator(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IChatStore>()));
        services.AddSingleton<IConversationBindingCommands>(sp => sp.GetRequiredService<BindingCoordinator>());
        services.AddSingleton<IConversationMutationPipeline, ConversationMutationPipeline>();
        services.AddSingleton<IWorkspaceWriter>(sp =>
            new WorkspaceWriter(sp.GetRequiredService<ChatConversationWorkspace>(), sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<Func<Action<SessionUpdateEventArgs>, IUiDispatcher, Action<string?>?, AcpEventAdapter>>(sp =>
            (handler, dispatcher, resyncRequired) => new AcpEventAdapter(
                handler,
                dispatcher,
                resyncRequired: resyncRequired,
                logger: sp.GetService<ILogger<AcpEventAdapter>>()));
        services.AddSingleton<IConversationActivationCoordinator>(sp =>
            new ConversationActivationCoordinator(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IConversationBindingCommands>(),
                sp.GetRequiredService<IChatStore>(),
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<ConversationActivationCoordinator>>(),
                sp.GetRequiredService<IConversationMutationPipeline>()));

        // Main shell navigation (Start + Projects -> Sessions tree)
        services.AddSingleton<INavigationSelectionProjector, NavigationSelectionProjector>();
        services.AddSingleton<ShellSelectionStateStore>();
        services.AddSingleton<ShellNavigationRuntimeStateStore>();
        services.AddSingleton<IShellSelectionReadModel>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<IShellSelectionMutationSink>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<IShellNavigationRuntimeState>(sp => sp.GetRequiredService<ShellNavigationRuntimeStateStore>());
        services.AddSingleton<MainNavigationViewModel>(sp =>
            new MainNavigationViewModel(
                sp.GetRequiredService<IConversationCatalog>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<IUiInteractionService>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<ILogger<MainNavigationViewModel>>(),
                sp.GetRequiredService<INavigationPaneState>(),
                sp.GetRequiredService<IShellLayoutMetricsSink>(),
                sp.GetRequiredService<INavigationSelectionProjector>(),
                sp.GetRequiredService<IShellSelectionReadModel>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IConversationCatalogReadModel>(),
                sp.GetRequiredService<IProjectAffinityResolver>(),
                sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<INavigationCoordinator>(sp =>
            new NavigationCoordinator(
                sp.GetRequiredService<IShellSelectionMutationSink>(),
                sp.GetRequiredService<IShellNavigationRuntimeState>(),
                sp.GetRequiredService<IConversationSessionSwitcher>(),
                sp.GetRequiredService<INavigationProjectSelectionStore>(),
                sp.GetRequiredService<IShellNavigationService>()));

        // Global search
        services.AddSingleton<IGlobalSearchPipeline, DefaultGlobalSearchPipeline>();
        services.AddSingleton<GlobalSearchViewModel>();

        // Discover sessions
        services.AddTransient<SalmonEgg.Presentation.ViewModels.Discover.DiscoverSessionsViewModel>();

        // Start page orchestrator (Start creates session and submits)
        services.AddSingleton<StartViewModel>();
        services.AddSingleton<IChatLaunchWorkflow>(sp =>
            new ChatLaunchWorkflow(
                sp.GetRequiredService<IChatLaunchWorkflowChatFacade>(),
                sp.GetRequiredService<ISessionManager>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                CreateStartCwdResolver(
                    sp.GetRequiredService<MainNavigationViewModel>(),
                    sp.GetRequiredService<AppPreferencesViewModel>()),
                sp.GetRequiredService<ILogger<ChatLaunchWorkflow>>(),
                sp.GetRequiredService<ConversationCatalogFacade>()));

        // App preferences used by General/Appearance settings and window behaviors.
        services.AddSingleton<AppPreferencesViewModel>();
        services.AddSingleton<WindowBackdropService>();

        // General settings
        services.AddSingleton<GeneralSettingsViewModel>();

        // ACP session registry: single source of truth for per-profile connection sessions.
        // Registered as a singleton concrete type first, then aliased to both interfaces
        // so ChatViewModel coordinator and Settings-page ViewModels share the same instance.
        services.AddSingleton<InMemoryAcpConnectionSessionRegistry>();
        services.AddSingleton<IAcpConnectionSessionRegistry>(sp =>
            sp.GetRequiredService<InMemoryAcpConnectionSessionRegistry>());
        services.AddSingleton<IAcpConnectionSessionEvents>(sp =>
            sp.GetRequiredService<InMemoryAcpConnectionSessionRegistry>());

        // ISettingsAcpConnectionCommands is implemented by ISettingsChatConnection.
        // Use a Lazy wrapper to defer resolution and break the circular dependency:
        //   AcpProfilesViewModel → ISettingsAcpConnectionCommands
        //                        → ISettingsChatConnection
        //                        → ChatViewModel
        //                        → AcpProfilesViewModel  (cycle!).
        // The Lazy<T> is only instantiated when AgentProfileItemViewModel first calls ConnectAsync,
        // by which time the DI graph is fully resolved.
        services.AddSingleton<ISettingsAcpConnectionCommands>(sp =>
        {
            var lazy = new Lazy<ISettingsChatConnection>(
                () => sp.GetRequiredService<ISettingsChatConnection>());
            return new LazySettingsAcpConnectionCommandsAdapter(lazy);
        });

        // ACP connection profiles — use full constructor so ProfileItems gets connection dependencies.
        services.AddSingleton<AcpProfilesViewModel>(sp =>
            new AcpProfilesViewModel(
                sp.GetRequiredService<IConfigurationService>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ILogger<AcpProfilesViewModel>>(),
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionEvents>(),
                sp.GetRequiredService<ISettingsAcpConnectionCommands>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IUiDispatcher>()));


        // ACP connection settings page view model (wraps Chat + Profiles)
        services.AddSingleton<AcpConnectionSettingsViewModel>(sp =>
            new AcpConnectionSettingsViewModel(
                sp.GetRequiredService<ISettingsChatConnection>(),
                sp.GetRequiredService<AcpProfilesViewModel>(),
                sp.GetRequiredService<IAcpConnectionSessionRegistry>(),
                sp.GetRequiredService<IAcpConnectionSessionEvents>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ILogger<AcpConnectionSettingsViewModel>>(),
                sp.GetRequiredService<IUiDispatcher>()));

        // Settings pages (Data/Shortcuts/Diagnostics/About)
        services.AddSingleton<DataStorageSettingsViewModel>();
        services.AddSingleton<ShortcutsSettingsViewModel>();
        services.AddSingleton<LiveLogViewerViewModel>(sp =>
            new LiveLogViewerViewModel(
                sp.GetRequiredService<ILiveLogStreamService>(),
                sp.GetRequiredService<IAppDataService>().LogsDirectoryPath,
                sp.GetRequiredService<ILogger<LiveLogViewerViewModel>>(),
                sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<DiagnosticsSettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        // Shell navigation facade (prevents Settings pages from walking the visual tree)
        services.AddSingleton<IShellNavigationService, ShellNavigationService>();

        // Navigation state service (Single Source of Truth for IsPaneOpen) - Read-only adapter for SSOT
        services.AddSingleton<INavigationPaneState, ShellLayoutNavigationStateAdapter>();
        services.AddSingleton<INavigationStateService, NavigationStateService>();

        // Right panel state service (Single Source of Truth for RightPanelMode)
        services.AddSingleton<IRightPanelService, RightPanelService>();

        // UI interaction helpers (ContentDialog, FolderPicker)
        services.AddSingleton<IUiInteractionService, UiInteractionService>();

        // UI runtime bridge (animations, shell reload)
        services.AddSingleton<IUiRuntimeService, UiRuntimeService>();

        // Mini floating chat window coordinator (Windows-only feature; other targets no-op via capability).
        services.AddSingleton<IMiniWindowCoordinator, MiniWindowCoordinator>();

        // Shell Layout SSOT
        services.AddSingleton<ShellLayoutViewModel>();
        services.AddSingleton<WindowMetricsProvider>();
    }

    private static string GetAppDataPath()
    {
        return SalmonEggPaths.GetAppDataRootPath();
    }

    private static Func<string?> CreateStartCwdResolver(
        MainNavigationViewModel navigationViewModel,
        AppPreferencesViewModel preferences)
    {
        ArgumentNullException.ThrowIfNull(navigationViewModel);
        ArgumentNullException.ThrowIfNull(preferences);

        return () =>
        {
            var pending = navigationViewModel.ConsumePendingProjectRootPath();
            string? lastSelectedRoot = null;

            var projectId = preferences.LastSelectedProjectId;
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                var project = preferences.Projects.FirstOrDefault(p => string.Equals(p.ProjectId, projectId, StringComparison.Ordinal));
                if (project != null && !string.IsNullOrWhiteSpace(project.RootPath))
                {
                    lastSelectedRoot = project.RootPath;
                }
            }

            return SessionCwdResolver.Resolve(pending, lastSelectedRoot);
        };
    }

    private static Func<IChatService, IChatService>? CreateChatServiceDecorator()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(GuiEnabledEnvVar), "1", StringComparison.Ordinal))
        {
            return null;
        }

        var rawDelay = Environment.GetEnvironmentVariable(GuiSlowSessionLoadMsEnvVar);
        if (!int.TryParse(rawDelay, out var delayMs) || delayMs <= 0)
        {
            return null;
        }

        var delay = TimeSpan.FromMilliseconds(delayMs);
        return inner => new DelayedLoadChatService(inner, delay);
    }

    private sealed class AcpChatServiceFactoryAdapter : IAcpChatServiceFactory
    {
        private readonly ChatServiceFactory _chatServiceFactory;

        public AcpChatServiceFactoryAdapter(ChatServiceFactory chatServiceFactory)
        {
            _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        }

        public IChatService CreateChatService(
            TransportType transportType,
            string? command = null,
            string? args = null,
            string? url = null)
            => _chatServiceFactory.CreateChatService(transportType, command, args, url);
    }

}
