#if WINDOWS || __ANDROID__ || __IOS__
using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Platforms;

public sealed class NativeElicitationUriLauncher : IExternalUriLauncher
{
    public bool IsSupported => true;

    public async Task<ExternalUriOpenResult> OpenAsync(ExternalUriTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (cancellationToken.IsCancellationRequested)
        {
            return ExternalUriOpenResult.Cancelled;
        }

        try
        {
            // Invoke the native launcher before the first await so consent owns the navigation.
            // The system browser is external to the app and exposes no page bridge to the agent.
            return await global::Windows.System.Launcher.LaunchUriAsync(target.Uri)
                ? ExternalUriOpenResult.Dispatched : ExternalUriOpenResult.Blocked;
        }
        catch
        {
            // Native failures can contain the URL. Return only the bounded outcome.
            return ExternalUriOpenResult.Failed;
        }
    }
}
#endif
