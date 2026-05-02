using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.ViewModels.Chat.Interactions;

public static class ChatInteractionDialogFactory
{
    public static PermissionRequestViewModel CreatePermissionRequestViewModel(
        PermissionRequestEventArgs request,
        Func<object, string, string?, Task<bool>> respondAsync,
        Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(respondAsync);
        ArgumentNullException.ThrowIfNull(dismiss);

        var viewModel = new PermissionRequestViewModel
        {
            MessageId = request.MessageId,
            SessionId = request.SessionId,
            ToolCallJson = request.ToolCall?.ToString() ?? string.Empty,
            Options = new ObservableCollection<PermissionOptionViewModel>(
                request.Options.Select(opt => new PermissionOptionViewModel
                {
                    OptionId = opt.OptionId,
                    Name = opt.Name,
                    Kind = opt.Kind
                }))
        };

        viewModel.OnRespond = async (outcome, optionId) =>
        {
            var succeeded = await respondAsync(request.MessageId, outcome, optionId).ConfigureAwait(true);
            if (succeeded)
            {
                dismiss();
            }
        };

        return viewModel;
    }

    public static FileSystemRequestViewModel CreateFileSystemRequestViewModel(
        FileSystemRequestEventArgs request,
        Func<object, bool, string?, string?, Task> respondAsync,
        Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(respondAsync);
        ArgumentNullException.ThrowIfNull(dismiss);

        var viewModel = new FileSystemRequestViewModel
        {
            MessageId = request.MessageId,
            SessionId = request.SessionId,
            Operation = request.Operation,
            Path = request.Path,
            Encoding = request.Encoding,
            Content = request.Content
        };

        viewModel.OnRespond = async (success, content, message) =>
        {
            await respondAsync(request.MessageId, success, content, message).ConfigureAwait(true);
            dismiss();
        };

        return viewModel;
    }
}
