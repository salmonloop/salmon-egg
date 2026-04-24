using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed class LocalTerminalPanelSessionViewModel : ObservableObject
{
    private const string DefaultDisplayTitle = "Terminal";

    private string _displayTitle;
    private string _currentWorkingDirectory;
    private string _outputText;
    private bool _canAcceptInput;

    public LocalTerminalPanelSessionViewModel(string conversationId, ILocalTerminalSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        Session = session ?? throw new ArgumentNullException(nameof(session));

        ConversationId = conversationId;
        _currentWorkingDirectory = NormalizeWorkingDirectory(session.CurrentWorkingDirectory);
        _outputText = string.Empty;
        _canAcceptInput = session.CanAcceptInput;
        _displayTitle = CreateDisplayTitle(_currentWorkingDirectory);
    }

    public string ConversationId { get; }

    public ILocalTerminalSession Session { get; }

    public string DisplayTitle
    {
        get => _displayTitle;
        private set => SetProperty(ref _displayTitle, value);
    }

    public string CurrentWorkingDirectory
    {
        get => _currentWorkingDirectory;
        private set => SetProperty(ref _currentWorkingDirectory, value);
    }

    public bool CanAcceptInput
    {
        get => _canAcceptInput;
        private set => SetProperty(ref _canAcceptInput, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public void RefreshFromSession()
    {
        var currentWorkingDirectory = NormalizeWorkingDirectory(Session.CurrentWorkingDirectory);
        CurrentWorkingDirectory = currentWorkingDirectory;
        CanAcceptInput = Session.CanAcceptInput;
        DisplayTitle = CreateDisplayTitle(currentWorkingDirectory);
    }

    public void AppendOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        OutputText = string.Concat(OutputText, output);
    }

    private static string NormalizeWorkingDirectory(string? currentWorkingDirectory)
        => string.IsNullOrWhiteSpace(currentWorkingDirectory)
            ? string.Empty
            : currentWorkingDirectory.Trim();

    private static string CreateDisplayTitle(string currentWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(currentWorkingDirectory))
        {
            return DefaultDisplayTitle;
        }

        var normalizedPath = currentWorkingDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (normalizedPath.Length == 0)
        {
            return DefaultDisplayTitle;
        }

        var fileName = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(fileName)
            ? normalizedPath
            : fileName;
    }
}
