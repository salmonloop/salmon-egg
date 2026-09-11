using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    // Interaction ownership requires a registered connection. Other chat tests intentionally exercise
    // the host's no-registry path, so the shared CreateViewModel must not install an empty registry.
    private static ViewModelFixture CreateInteractionViewModel(
        SynchronizationContext? syncContext = null,
        IAcpConnectionCommands? acpConnectionCommands = null,
        IShellNavigationRuntimeState? shellNavigationRuntimeState = null,
        IStringLocalizer<CoreStrings>? localizer = null,
        IAppLanguageService? languageService = null,
        Func<IChatConnectionStore, IAcpConnectionCoordinator>? acpConnectionCoordinatorFactory = null,
        IExternalUriLauncher? externalUriLauncher = null)
        => CreateViewModel(
            syncContext,
            acpConnectionCommands: acpConnectionCommands,
            shellNavigationRuntimeState: shellNavigationRuntimeState,
            localizer: localizer,
            languageService: languageService,
            acpConnectionCoordinatorFactory: acpConnectionCoordinatorFactory,
            externalUriLauncher: externalUriLauncher,
            connectionSessionRegistry: new InMemoryAcpConnectionSessionRegistry());
}
