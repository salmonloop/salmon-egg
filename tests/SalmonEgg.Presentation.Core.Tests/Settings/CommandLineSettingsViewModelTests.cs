using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Domain.Models.Cli;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Models.Cli;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

/// <summary>
/// The projection from one observed registration onto what the page shows and offers.
/// </summary>
/// <remarks>
/// The localizer returns each key verbatim, so these assertions name resource keys rather than translated
/// text: the question is which message a state selects, not how it reads in a given language.
/// </remarks>
public sealed class CommandLineSettingsViewModelTests
{
    [Theory]
    [InlineData(CliCommandRegistrationState.Registered, "CommandLine_Status_Registered", CliCommandStatusSeverity.Success)]
    [InlineData(CliCommandRegistrationState.NotRegistered, "CommandLine_Status_NotRegistered", CliCommandStatusSeverity.Warning)]
    [InlineData(CliCommandRegistrationState.VersionMismatch, "CommandLine_Status_VersionMismatch", CliCommandStatusSeverity.Warning)]
    [InlineData(CliCommandRegistrationState.Unreadable, "CommandLine_Status_Unreadable", CliCommandStatusSeverity.Error)]
    [InlineData(CliCommandRegistrationState.Unsupported, "CommandLine_Status_Unsupported", CliCommandStatusSeverity.Informational)]
    public async Task EachStateSelectsItsOwnTitleAndSeverity(
        CliCommandRegistrationState state,
        string expectedTitleKey,
        CliCommandStatusSeverity expectedSeverity)
    {
        var viewModel = Create(out var inspector, out _, registration: RegistrationFor(state));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, inspector.Inspections);
        Assert.Equal(expectedTitleKey, viewModel.StatusTitle);
        Assert.Equal(expectedSeverity, viewModel.Severity);
    }

    [Fact]
    public async Task BeforeAnyInspectionThereIsNoResultToShow()
    {
        var viewModel = Create(out _, out _);

        Assert.False(viewModel.HasResult);
        Assert.Equal("CommandLine_Status_Unknown", viewModel.StatusTitle);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasResult);
    }

    [Fact]
    public async Task AnAbsentCommandReadsDifferentlyDependingOnWhoOwnsTheRegistration()
    {
        // The same state, two different next steps: on macOS the user can fix it here, and everywhere else
        // the honest answer is that the installer owns it.
        var canLink = Create(out _, out _, registration: RegistrationFor(CliCommandRegistrationState.NotRegistered), canLink: true);
        var installerOwned = Create(out _, out _, registration: RegistrationFor(CliCommandRegistrationState.NotRegistered));

        await canLink.RefreshCommand.ExecuteAsync(null);
        await installerOwned.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("CommandLine_Message_NotRegistered_CanLink", canLink.StatusMessage);
        Assert.Equal("CommandLine_Message_NotRegistered_Installer", installerOwned.StatusMessage);
        Assert.True(canLink.CanManageLink);
        Assert.False(canLink.IsInstallerOwned);
        Assert.True(installerOwned.IsInstallerOwned);
    }

    [Fact]
    public async Task AnUnreadableCommandCarriesTheDetailVerbatim()
    {
        // The detail is what the executable or the OS said. It is the text a user has to search for, so it is
        // appended rather than replaced by a translated sentence.
        var registration = CliCommandRegistration.Unreadable(
            "/usr/local/bin/salmon-egg", null, "1.4.3", "the command exited with code 134");
        var viewModel = Create(out _, out _, registration: registration);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("the command exited with code 134", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkingReportsSuccessAndReInspects()
    {
        var viewModel = Create(out var inspector, out var linkService, canLink: true);
        linkService.NextResult = CliCommandLinkResult.Linked();

        await viewModel.LinkCommand.ExecuteAsync(null);

        Assert.Equal(1, linkService.LinkCalls);
        // The status must come from a fresh observation, not from the operation's own claim of success.
        Assert.Equal(1, inspector.Inspections);
        Assert.Equal(CliCommandStatusSeverity.Success, viewModel.ActionSeverity);
        Assert.Equal("CommandLine_Action_Linked", viewModel.ActionMessage);
    }

    [Fact]
    public async Task ACancelledAuthorizationChangesNothingAndDoesNotReInspect()
    {
        // Dismissing the password prompt is a decision, not a failure: nothing changed, so nothing needs
        // re-reading and the message must not read as an error.
        var viewModel = Create(out var inspector, out var linkService, canLink: true);
        linkService.NextResult = CliCommandLinkResult.Cancelled();

        await viewModel.LinkCommand.ExecuteAsync(null);

        Assert.Equal(0, inspector.Inspections);
        Assert.Equal(CliCommandStatusSeverity.Informational, viewModel.ActionSeverity);
        Assert.Equal("CommandLine_Action_Cancelled", viewModel.ActionMessage);
    }

    [Fact]
    public async Task AFailedLinkSurfacesTheDetail()
    {
        var viewModel = Create(out _, out var linkService, canLink: true);
        linkService.NextResult = CliCommandLinkResult.Failed("Operation not permitted");

        await viewModel.LinkCommand.ExecuteAsync(null);

        Assert.Equal(CliCommandStatusSeverity.Error, viewModel.ActionSeverity);
        Assert.Contains("Operation not permitted", viewModel.ActionMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlinkingReportsRemovalAndReInspects()
    {
        var viewModel = Create(out var inspector, out var linkService, canLink: true);
        linkService.NextResult = CliCommandLinkResult.Unlinked();

        await viewModel.UnlinkCommand.ExecuteAsync(null);

        Assert.Equal(1, linkService.UnlinkCalls);
        Assert.Equal(1, inspector.Inspections);
        Assert.Equal("CommandLine_Action_Unlinked", viewModel.ActionMessage);
    }

    [Fact]
    public void WhereTheInstallerOwnsTheEntryTheLinkCommandsRefuseToRun()
    {
        // The buttons are hidden on these platforms, but CanExecute is the load-bearing guard: a hidden
        // command that would still run if invoked is a second owner waiting to happen.
        var viewModel = Create(out _, out _);

        Assert.False(viewModel.CanManageLink);
        Assert.False(viewModel.LinkCommand.CanExecute(null));
        Assert.False(viewModel.UnlinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnInspectionFailureIsReportedRatherThanThrown()
    {
        var viewModel = Create(out var inspector, out _);
        inspector.Throw = true;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasResult);
        Assert.Equal(CliCommandStatusSeverity.Error, viewModel.ActionSeverity);
        Assert.Equal("CommandLine_Action_InspectFailed", viewModel.ActionMessage);
        Assert.False(viewModel.IsBusy);
    }

    private static CliCommandRegistration RegistrationFor(CliCommandRegistrationState state) => state switch
    {
        CliCommandRegistrationState.Registered =>
            CliCommandRegistration.Resolved("/usr/local/bin/salmon-egg", null, "1.4.3", "1.4.3.0"),
        CliCommandRegistrationState.VersionMismatch =>
            CliCommandRegistration.Resolved("/usr/local/bin/salmon-egg", null, "1.0.0", "1.4.3.0"),
        CliCommandRegistrationState.Unreadable =>
            CliCommandRegistration.Unreadable("/usr/local/bin/salmon-egg", null, "1.4.3.0", "no version"),
        CliCommandRegistrationState.Unsupported => CliCommandRegistration.Unsupported("1.4.3.0"),
        _ => CliCommandRegistration.NotRegistered("1.4.3.0"),
    };

    private static CommandLineSettingsViewModel Create(
        out FakeInspector inspector,
        out FakeLinkService linkService,
        CliCommandRegistration? registration = null,
        bool canLink = false)
    {
        inspector = new FakeInspector
        {
            Registration = registration ?? CliCommandRegistration.NotRegistered("1.4.3.0"),
        };
        linkService = new FakeLinkService { IsSupported = canLink };

        return new CommandLineSettingsViewModel(
            inspector,
            linkService,
            new FakeCapabilities(),
            new KeyLocalizer(),
            NullLogger<CommandLineSettingsViewModel>.Instance);
    }

    private sealed class FakeInspector : ICliCommandRegistrationInspector
    {
        public CliCommandRegistration Registration { get; set; } = CliCommandRegistration.NotRegistered("1.4.3.0");

        public bool Throw { get; set; }

        public int Inspections { get; private set; }

        public Task<CliCommandRegistration> InspectAsync(CancellationToken cancellationToken = default)
        {
            Inspections++;
            return Throw
                ? Task.FromException<CliCommandRegistration>(new InvalidOperationException("probe failed"))
                : Task.FromResult(Registration);
        }
    }

    private sealed class FakeLinkService : ICliCommandLinkService
    {
        public bool IsSupported { get; set; }

        public CliCommandLinkResult NextResult { get; set; } = CliCommandLinkResult.Unsupported();

        public int LinkCalls { get; private set; }

        public int UnlinkCalls { get; private set; }

        public Task<CliCommandLinkResult> LinkAsync(CancellationToken cancellationToken = default)
        {
            LinkCalls++;
            return Task.FromResult(NextResult);
        }

        public Task<CliCommandLinkResult> UnlinkAsync(CancellationToken cancellationToken = default)
        {
            UnlinkCalls++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeCapabilities : IPlatformCapabilityService
    {
        public bool SupportsLaunchOnStartup => false;

        public bool SupportsTray => false;

        public bool SupportsLanguageOverride => true;

        public bool SupportsMiniWindow => false;

        public bool SupportsExternalFileOpen => false;

        public bool SupportsLocalFileExport => false;

        public bool SupportsStdioTransport => false;

        public bool SupportsInteractiveTerminalSurface => false;

        public bool SupportsLocalTerminal => false;

        public bool SupportsGamepadInput => false;

        public bool SupportsCliCommandInspection => true;

        public bool SupportsCliCommandLinking => false;
    }

    /// <summary>Returns every key verbatim, so assertions name keys instead of translations.</summary>
    private sealed class KeyLocalizer : IStringLocalizer<CoreStrings>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
