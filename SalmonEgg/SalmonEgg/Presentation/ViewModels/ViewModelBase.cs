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

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        protected ViewModelBase(ILogger logger)
        {
            Logger = logger;
        }

        public void ClearError() => ErrorMessage = string.Empty;
        public void SetError(string error) => ErrorMessage = error;
    }
}
