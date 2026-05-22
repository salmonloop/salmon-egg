using CommunityToolkit.Mvvm.ComponentModel;
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;

public sealed partial class TaskOverviewChangeViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDisplayName))]
    private TaskOverviewChangeKind _kind = TaskOverviewChangeKind.Changed;

    [ObservableProperty]
    private string _lineSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryPath))]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string _path = string.Empty;

    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string FileName
    {
        get
        {
            var fileName = System.IO.Path.GetFileName(Path);
            return string.IsNullOrWhiteSpace(fileName) ? Path : fileName;
        }
    }

    public string KindDisplayName => Kind switch
    {
        TaskOverviewChangeKind.Added => "Added",
        TaskOverviewChangeKind.Modified => "Modified",
        _ => "Changed"
    };
}
