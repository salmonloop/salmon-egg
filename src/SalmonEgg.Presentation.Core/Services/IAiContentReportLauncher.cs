using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Application-owned launcher for reporting inappropriate AI content through the
/// shared support email path used by About and Chat.
/// </summary>
public interface IAiContentReportLauncher
{
    bool CanReport { get; }

    Task<bool> TryOpenReportAsync(
        string appName,
        string appVersion,
        string protocolVersion,
        string? contentExcerpt = null);
}
