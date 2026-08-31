using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Platforms.Desktop;
using SalmonEgg.Presentation.Core.Resources;

// Drives the real LinuxSystemNotificationService against whatever owns
// org.freedesktop.Notifications on the current session bus, and prints one result per line for the
// gate to assert on. The gate also inspects what the notification server received over the wire.
var service = new LinuxSystemNotificationService(new StubLocalizer());
try
{
    Console.WriteLine($"IsSupported={service.IsSupported}");
    Console.WriteLine($"Permission={await service.RequestPermissionAsync()}");

    const string firstTurnId = "turn:conv-1:turn-1";
    Console.WriteLine($"ShowFirstTurn={await ShowAsync(firstTurnId, "Task completed", "First turn.")}");

    // The same turn again must replace rather than stack.
    Console.WriteLine($"ShowSameTurnAgain={await ShowAsync(firstTurnId, "Task completed", "First turn.")}");

    // A different turn must be its own notification.
    Console.WriteLine($"ShowSecondTurn={await ShowAsync("turn:conv-1:turn-2", "Task completed", "Second turn.")}");

    // A blank title is a malformed request, not an absent capability.
    Console.WriteLine($"ShowBlankTitle={await ShowAsync("turn:conv-1:turn-3", "   ", "Body.")}");
}
finally
{
    service.Dispose();
}

async Task<string> ShowAsync(string notificationId, string title, string body)
{
    var result = await service.ShowAsync(new SystemNotificationRequest(notificationId, title, body));
    return result.ToString();
}

internal sealed class StubLocalizer : IStringLocalizer<CoreStrings>
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] => this[name];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => Array.Empty<LocalizedString>();
}
