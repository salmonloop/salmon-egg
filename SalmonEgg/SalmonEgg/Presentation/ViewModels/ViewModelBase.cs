using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace SalmonEgg.Presentation.ViewModels
{
    /// <summary>
    /// ViewModel 基类，提供通用功能和属性
    /// Requirements: 4.1, 4.2, 6.1
    /// </summary>
    public abstract partial class ViewModelBase : ObservableObject
    {
        protected readonly ILogger Logger;

        [ObservableProperty]
        private bool _isBusy;

        // Expose ObservableProperty-generated changes via a virtual hook so derived VMs can react (e.g., refresh commands)
        // without needing to re-declare IsBusy in every ViewModel.
        partial void OnIsBusyChanged(bool value) => OnIsBusyChangedCore(value);

        protected virtual void OnIsBusyChangedCore(bool value)
        {
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _errorMessage = string.Empty;

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        protected ViewModelBase(ILogger logger)
        {
            Logger = logger;
        }

        public void ClearError() => ErrorMessage = string.Empty;
        public void SetError(string error) => ErrorMessage = error;
    }
}
