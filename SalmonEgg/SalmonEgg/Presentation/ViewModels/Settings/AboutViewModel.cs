using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IPlatformShellService _shell;
    private readonly IAppDataService _paths;

    public string AppName => "SalmonEgg";

    public string AppVersion => typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

    public string ProtocolVersion => new InitializeParams().ProtocolVersion.ToString();

    public AboutViewModel(IPlatformShellService shell, IAppDataService paths)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync()
    {
        return _shell.OpenFolderAsync(_paths.AppDataRootPath);
    }
}
