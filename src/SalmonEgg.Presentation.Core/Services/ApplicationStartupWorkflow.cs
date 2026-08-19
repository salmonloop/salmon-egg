using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ApplicationStartupWorkflow : IApplicationStartupWorkflow
{
    private readonly IShellStartupNavigationService _shellStartupNavigation;
    private readonly IChatRuntimeInitialization _chatRuntimeInitialization;
    private readonly IConfigurationRecoveryService? _configurationRecoveryService;
    private readonly object _runtimeInitializationSync = new();
    private Task<bool>? _configurationRecoveryTask;
    private Task<bool>? _profileInitializationTask;
    private Task<bool>? _conversationRestoreTask;
    private bool _configurationRecoveryCompleted;
    private bool _profileInitializationCompleted;
    private bool _conversationRestoreCompleted;

    public ApplicationStartupWorkflow(
        IShellStartupNavigationService shellStartupNavigation,
        IChatRuntimeInitialization chatRuntimeInitialization)
        : this(shellStartupNavigation, chatRuntimeInitialization, null)
    {
    }

    public ApplicationStartupWorkflow(
        IShellStartupNavigationService shellStartupNavigation,
        IChatRuntimeInitialization chatRuntimeInitialization,
        IConfigurationRecoveryService? configurationRecoveryService)
    {
        _shellStartupNavigation = shellStartupNavigation ?? throw new ArgumentNullException(nameof(shellStartupNavigation));
        _chatRuntimeInitialization = chatRuntimeInitialization ?? throw new ArgumentNullException(nameof(chatRuntimeInitialization));
        _configurationRecoveryService = configurationRecoveryService;
    }

    public Task ActivateShellAsync()
        => _shellStartupNavigation.ActivateInitialContentAsync();

    public async Task InitializeRuntimeAsync()
    {
        await EnsureConfigurationRecoveredAsync().ConfigureAwait(false);

        var profileInitialization = EnsureProfilesInitializedAsync();
        var conversationRestore = EnsureConversationsRestoredAsync();
        await Task.WhenAll(profileInitialization, conversationRestore).ConfigureAwait(false);
    }

    private Task<bool> EnsureConfigurationRecoveredAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_configurationRecoveryCompleted || _configurationRecoveryService is null)
            {
                return Task.FromResult(true);
            }

            if (_configurationRecoveryTask is null || _configurationRecoveryTask.IsCompleted)
            {
                _configurationRecoveryTask = RecoverConfigurationCoreAsync();
            }

            return _configurationRecoveryTask;
        }
    }

    private async Task<bool> RecoverConfigurationCoreAsync()
    {
        await _configurationRecoveryService!.RecoverPendingTransactionsAsync().ConfigureAwait(false);
        lock (_runtimeInitializationSync)
        {
            _configurationRecoveryCompleted = true;
        }

        return true;
    }

    private Task<bool> EnsureProfilesInitializedAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_profileInitializationCompleted)
            {
                return Task.FromResult(true);
            }

            if (_profileInitializationTask is null || _profileInitializationTask.IsCompleted)
            {
                _profileInitializationTask = InitializeProfilesCoreAsync();
            }

            return _profileInitializationTask;
        }
    }

    private async Task<bool> InitializeProfilesCoreAsync()
    {
        var initialized = await _chatRuntimeInitialization.InitializeAcpProfilesAsync().ConfigureAwait(false);
        if (initialized)
        {
            lock (_runtimeInitializationSync)
            {
                _profileInitializationCompleted = true;
            }
        }

        return initialized;
    }

    private Task<bool> EnsureConversationsRestoredAsync()
    {
        lock (_runtimeInitializationSync)
        {
            if (_conversationRestoreCompleted)
            {
                return Task.FromResult(true);
            }

            if (_conversationRestoreTask is null || _conversationRestoreTask.IsCompleted)
            {
                _conversationRestoreTask = RestoreConversationsCoreAsync();
            }

            return _conversationRestoreTask;
        }
    }

    private async Task<bool> RestoreConversationsCoreAsync()
    {
        var restored = await _chatRuntimeInitialization.RestoreConversationsAsync().ConfigureAwait(false);
        if (restored)
        {
            lock (_runtimeInitializationSync)
            {
                _conversationRestoreCompleted = true;
            }
        }

        return restored;
    }
}
