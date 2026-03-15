using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.ViewModels.Settings;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IPlatformShellService _shell;
    private readonly IAppDataService _paths;
    private readonly IAppDocumentService _documents;
    private readonly IUiInteractionService _ui;

    public string AppName => "SalmonEgg";

    public string AppVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    public string ProtocolVersion => new InitializeParams().ProtocolVersion.ToString();

    public string DocsRootPath => _documents.DocsRootPath;

    public AboutViewModel(
        IPlatformShellService shell,
        IAppDataService paths,
        IAppDocumentService documents,
        IUiInteractionService ui)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    [RelayCommand]
    private Task OpenAppDataFolderAsync()
    {
        return _shell.OpenFolderAsync(_paths.AppDataRootPath);
    }

    [RelayCommand]
    private async Task OpenReleaseNotesAsync()
    {
        var path = _documents.GetReleaseNotesPath();
        if (!File.Exists(path))
        {
            await NotifyMissingDocAsync(path, "更新日志").ConfigureAwait(true);
            return;
        }

        await _shell.OpenFileAsync(path).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        var path = _documents.GetPrivacyPolicyPath();
        if (!File.Exists(path))
        {
            await NotifyMissingDocAsync(path, "隐私政策").ConfigureAwait(true);
            return;
        }

        await _shell.OpenFileAsync(path).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task CopyVersionInfoAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"App: {AppName}");
        sb.AppendLine($"Version: {AppVersion}");
        sb.AppendLine($"Protocol: {ProtocolVersion}");
        await _shell.CopyToClipboardAsync(sb.ToString()).ConfigureAwait(false);
        await _ui.ShowInfoAsync("版本信息已复制到剪贴板。").ConfigureAwait(true);
    }

    private async Task NotifyMissingDocAsync(string path, string title)
    {
        var folder = Path.GetDirectoryName(path);
        var message = folder == null
            ? $"未找到{title}文件。"
            : $"未找到{title}文件。\n请在以下目录创建对应的 Markdown 文件：\n{folder}";

        await _ui.ShowInfoAsync(message).ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            await _shell.OpenFolderAsync(folder).ConfigureAwait(false);
        }
    }
}
