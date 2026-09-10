using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Services;

// A separate process drives the product launcher, without acquiring a browser automation bridge.
if (!OperatingSystem.IsLinux() || args.Length != 0)
{
    return 2;
}

var probe = new PlatformRuntimeCapabilityProbe();
if (!string.Equals(probe.ResolveExternalFileOpener(), "/usr/bin/xdg-open", StringComparison.Ordinal))
{
    Console.Error.WriteLine("The gate requires the real /usr/bin/xdg-open.");
    return 3;
}

var launcher = new DesktopElicitationUriLauncher(probe);
if (!launcher.IsSupported
    || !ExternalUriTarget.TryCreate(await Console.In.ReadLineAsync(), out var target))
{
    return 4;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var result = await launcher.OpenAsync(target!, timeout.Token);
Console.WriteLine(result);
return result == ExternalUriOpenResult.Opened ? 0 : 5;
