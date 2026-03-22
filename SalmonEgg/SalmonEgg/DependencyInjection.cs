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
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg;

/// <summary>
    /// Dependency injection container configuration
/// Requirements: 7.5
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Configures all services and dependencies
    /// </summary>
    public static IServiceCollection AddSalmonEgg(this IServiceCollection services)
    {
        ConfigureLogging(services);
        RegisterDomainServices(services);
        RegisterInfrastructureServices(services);
        RegisterApplicationServices(services);
        RegisterViewModels(services);
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

        // Capability Manager
        services.AddSingleton<ICapabilityManager, Infrastructure.Services.CapabilityManager>();

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
        // Secure Storage
        services.AddSingleton<ISecureStorage, SecureStorage>();

        // App settings (config/app.yaml)
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAppDataService, AppDataService>();
        services.AddSingleton<IAppMaintenanceService, AppMaintenanceService>();
        services.AddSingleton<IAppDocumentService, AppDocumentService>();
        services.AddSingleton<IConversationStore, ConversationStore>();
        services.AddSingleton<IPlatformCapabilityService, PlatformCapabilityService>();
        services.AddSingleton<IAppStartupService, AppStartupService>();
        services.AddSingleton<IAppLanguageService, AppLanguageService>();

        // Configuration Manager
        services.AddSingleton<IConfigurationService, ConfigurationManager>();

        // Validator
        // Validator
        services.AddSingleton<IValidator<ServerConfiguration>, ServerConfigurationValidator>();

        // Transport Layer Factory
        services.AddSingleton<SalmonEgg.Domain.Interfaces.ITransportFactory, TransportFactory>();


        // Diagnostics & platform shell
        services.AddSingleton<IDiagnosticsBundleService, SalmonEgg.Infrastructure.Services.DiagnosticsBundleService>();
        services.AddSingleton<ILiveLogStreamService, SalmonEgg.Infrastructure.Services.LiveLogStreamService>();
        services.AddSingleton<IPlatformShellService, SalmonEgg.Infrastructure.Services.PlatformShellService>();

        }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        // MVUX Chat Store
        services.AddSingleton<IState<ChatState>>(sp => State.Value(sp, () => ChatState.Empty));
        services.AddSingleton<IChatStore, ChatStore>();
        services.AddSingleton<IState<ChatConnectionState>>(sp => State.Value(sp, () => ChatConnectionState.Empty));
        services.AddSingleton<IChatConnectionStore, ChatConnectionStore>();
        services.AddSingleton<IAcpConnectionCoordinator>(sp =>
            new AcpConnectionCoordinator(
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<AcpConnectionCoordinator>>()));

        // MVUX Shell Layout Store
        services.AddSingleton<IShellLayoutStore>(sp =>
        {
            var initialState = ShellLayoutState.Default;
            var initialSnapshot = ShellLayoutPolicy.Compute(initialState);
            var state = State.Value(sp, () => initialState);
            var snapshot = State.Value(sp, () => initialSnapshot);
            return new ShellLayoutStore(state, snapshot, initialState, initialSnapshot);
        });
        services.AddSingleton<IShellLayoutMetricsSink, ShellLayoutMetricsSink>();

        // Use Cases
        services.AddTransient<ConnectToServerUseCase>();
        services.AddTransient<DisconnectUseCase>();
        services.AddTransient<SendMessageUseCase>();

        // Application Services
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IMessageService, MessageService>();

        // Chat Service Factory (supporting dynamic creation)
        services.AddSingleton<ChatServiceFactory>(sp =>
        {
            var transportFactory = sp.GetRequiredService<ITransportFactory>();
            var parser = sp.GetRequiredService<IMessageParser>();
            var validator = sp.GetRequiredService<IMessageValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            var capabilityManager = sp.GetRequiredService<ICapabilityManager>();
            var sessionManager = sp.GetRequiredService<ISessionManager>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChatServiceFactory(transportFactory, parser, validator, errorLogger, capabilityManager, sessionManager, logger);
        });
        services.AddSingleton<IAcpChatServiceFactory>(sp =>
            new AcpChatServiceFactoryAdapter(sp.GetRequiredService<ChatServiceFactory>()));
        services.AddSingleton<IAcpConnectionCommands, AcpChatCoordinator>();

        // Chat Service (default instance using default transport)
        services.AddSingleton<IChatService>(sp =>
        {
            var factory = sp.GetRequiredService<ChatServiceFactory>();
            return factory.CreateDefaultChatService();
        });

        // Error Recovery Service
        services.AddSingleton<IErrorRecoveryService>(sp =>
        {
            var chatService = sp.GetRequiredService<IChatService>();
            var pathValidator = sp.GetRequiredService<IPathValidator>();
            var errorLogger = sp.GetRequiredService<IErrorLogger>();
            return new ErrorRecoveryService(chatService, pathValidator, errorLogger);
        });
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // Existing ViewModels (Legacy)
        services.AddTransient<MainViewModel>();
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
        services.AddSingleton<INavigationProjectPreferences>(sp =>
            new NavigationProjectPreferencesAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<INavigationProjectSelectionStore>(sp =>
            new NavigationProjectSelectionStoreAdapter(sp.GetRequiredService<AppPreferencesViewModel>()));
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ChatShellViewModel>();
        services.AddSingleton<ConversationCatalogFacade>();
        services.AddSingleton<IConversationCatalog>(sp => sp.GetRequiredService<ConversationCatalogFacade>());
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
        services.AddSingleton<IWorkspaceWriter>(sp =>
            new WorkspaceWriter(sp.GetRequiredService<ChatConversationWorkspace>()));
        services.AddSingleton<Func<Action<SessionUpdateEventArgs>, SynchronizationContext, Action?, AcpEventAdapter>>(sp =>
            (handler, syncContext, resyncRequired) => new AcpEventAdapter(
                handler,
                syncContext,
                resyncRequired: resyncRequired,
                logger: sp.GetService<ILogger<AcpEventAdapter>>()));
        services.AddSingleton<IConversationActivationCoordinator>(sp =>
            new ConversationActivationCoordinator(
                sp.GetRequiredService<ChatConversationWorkspace>(),
                sp.GetRequiredService<IConversationBindingCommands>(),
                sp.GetRequiredService<IChatStore>(),
                sp.GetRequiredService<IChatConnectionStore>(),
                sp.GetRequiredService<ILogger<ConversationActivationCoordinator>>()));

        // Main shell navigation (Start + Projects -> Sessions tree)
        services.AddSingleton<NavigationSelectionProjector>();
        services.AddSingleton<ShellSelectionStateStore>();
        services.AddSingleton<IShellSelectionReadModel>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<IShellSelectionMutationSink>(sp => sp.GetRequiredService<ShellSelectionStateStore>());
        services.AddSingleton<MainNavigationViewModel>(sp =>
            new MainNavigationViewModel(
                sp.GetRequiredService<IConversationCatalog>(),
                sp.GetRequiredService<INavigationProjectPreferences>(),
                sp.GetRequiredService<IUiInteractionService>(),
                sp.GetRequiredService<IShellNavigationService>(),
                sp.GetRequiredService<INavigationCoordinator>(),
                sp.GetRequiredService<ILogger<MainNavigationViewModel>>(),
                sp.GetRequiredService<INavigationPaneState>(),
                sp.GetRequiredService<IShellLayoutMetricsSink>(),
                sp.GetRequiredService<NavigationSelectionProjector>(),
                sp.GetRequiredService<IShellSelectionReadModel>(),
                sp.GetRequiredService<IConversationCatalogReadModel>()));
        services.AddSingleton<INavigationCoordinator>(sp =>
            new NavigationCoordinator(
                sp.GetRequiredService<IShellSelectionMutationSink>(),
                sp.GetRequiredService<IConversationActivationCoordinator>(),
                sp.GetRequiredService<INavigationProjectSelectionStore>(),
                sp.GetRequiredService<IShellNavigationService>()));

        // Global search
        services.AddSingleton<GlobalSearchViewModel>();

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
                sp.GetRequiredService<ILogger<ChatLaunchWorkflow>>()));

        // App preferences used by General/Appearance settings and window behaviors.
        services.AddSingleton<AppPreferencesViewModel>();

        // General settings
        services.AddSingleton<GeneralSettingsViewModel>();

        // ACP connection profiles (server presets)
        services.AddSingleton<AcpProfilesViewModel>();

        // ACP connection settings page view model (wraps Chat + Profiles)
        services.AddSingleton<AcpConnectionSettingsViewModel>(sp =>
            new AcpConnectionSettingsViewModel(
                sp.GetRequiredService<ISettingsChatConnection>(),
                sp.GetRequiredService<AcpProfilesViewModel>(),
                sp.GetRequiredService<AppPreferencesViewModel>(),
                sp.GetRequiredService<ILogger<AcpConnectionSettingsViewModel>>()));

        // Settings pages (Data/Shortcuts/Diagnostics/About)
        services.AddSingleton<DataStorageSettingsViewModel>();
        services.AddSingleton<ShortcutsSettingsViewModel>();
        services.AddSingleton<LiveLogViewerViewModel>(sp =>
            new LiveLogViewerViewModel(
                sp.GetRequiredService<ILiveLogStreamService>(),
                sp.GetRequiredService<IAppDataService>().LogsDirectoryPath,
                sp.GetRequiredService<ILogger<LiveLogViewerViewModel>>()));
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
