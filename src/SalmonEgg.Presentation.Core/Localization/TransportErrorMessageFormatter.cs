using Microsoft.Extensions.Localization;
using SalmonEgg.Application.Services.Acp;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Localization;

public sealed class TransportErrorMessageFormatter : ITransportErrorMessageFormatter
{
    public const string CommandNotFoundOnPathResourceKey = "AcpConnection_CommandNotFoundOnPath";
    public const string CommandPathMissingResourceKey = "AcpConnection_CommandPathMissing";

    private readonly IStringLocalizer<CoreStrings> _localizer;

    public TransportErrorMessageFormatter(IStringLocalizer<CoreStrings> localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public string Format(TransportErrorEventArgs error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.CommandResolutionFailure is not { } failure)
        {
            return error.ErrorMessage;
        }

        var resourceKey = failure.SearchedOnPath
            ? CommandNotFoundOnPathResourceKey
            : CommandPathMissingResourceKey;
        // The original diagnostic already contains the command. It is not a format string: paths
        // may contain braces, so only the resource template is formatted before falling back.
        var message = CoreStringResolver.ResolveFormat(_localizer, resourceKey, string.Empty, failure.Command);
        return string.IsNullOrWhiteSpace(message) ? error.ErrorMessage : message;
    }
}
