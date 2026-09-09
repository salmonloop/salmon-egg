using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>One native sign-in surface; terminal output belongs only to its interactive session.</summary>
public sealed class TerminalAuthenticationViewModel : ObservableObject
{
    private ITerminalAuthenticationSession? _session;

    public TerminalAuthenticationViewModel(string agentName, string methodName, IStringLocalizer<CoreStrings>? localizer = null)
    {
        Title = Text("ChatAuth_TerminalTitle", "Sign in to agent");
        ConsentMessage = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Text("ChatAuth_TerminalConsent", "Open {0}'s interactive sign-in ({1})? This starts a separate agent process. Its output stays in this sign-in window."),
            agentName, methodName);
        RunningMessage = Text("ChatAuth_TerminalRunning", "Complete sign-in below. The agent will reconnect when sign-in succeeds.");
        ContinueButtonText = Text("ChatAuth_TerminalContinue", "Open sign-in");
        CancelButtonText = Text("ChatAuth_TerminalCancel", "Cancel");

        string Text(string key, string fallback)
        {
            var resource = localizer?[key];
            return resource is { ResourceNotFound: false } ? resource.Value : fallback;
        }
    }

    public string Title { get; }

    public string ConsentMessage { get; }

    public string RunningMessage { get; }

    public string ContinueButtonText { get; }

    public string CancelButtonText { get; }

    public ITerminalAuthenticationSession? Session
    {
        get => _session;
        internal set => SetProperty(ref _session, value);
    }
}
