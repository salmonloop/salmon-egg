using CommunityToolkit.Mvvm.ComponentModel;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed partial class TerminalPanelSessionViewModel : ObservableObject
{
    public TerminalPanelSessionViewModel(string terminalId)
    {
        TerminalId = terminalId ?? string.Empty;
    }

    public string TerminalId { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _lastMethod = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private bool _isTruncated;

    [ObservableProperty]
    private int? _exitCode;

    [ObservableProperty]
    private bool _isReleased;
}
