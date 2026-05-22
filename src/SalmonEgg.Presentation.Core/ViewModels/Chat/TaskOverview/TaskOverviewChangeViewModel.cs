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
    private string _path = string.Empty;

    public string KindDisplayName => Kind switch
    {
        TaskOverviewChangeKind.Added => "Added",
        TaskOverviewChangeKind.Modified => "Modified",
        _ => "Changed"
    };
}
