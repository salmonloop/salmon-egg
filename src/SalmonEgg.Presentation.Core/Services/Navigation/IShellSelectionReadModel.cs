using System.ComponentModel;
using SalmonEgg.Presentation.Models.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public interface IShellSelectionReadModel : INotifyPropertyChanged
{
    NavigationSelectionState CurrentSelection { get; }
}
