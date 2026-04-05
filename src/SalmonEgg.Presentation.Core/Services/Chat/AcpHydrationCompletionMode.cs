namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Controls when a remote hydration operation is considered complete.
/// </summary>
public enum AcpHydrationCompletionMode
{
    /// <summary>
    /// Wait for replay projection and transcript growth grace conditions.
    /// </summary>
    StrictReplay = 0,

    /// <summary>
    /// Complete once session/load response is projected.
    /// </summary>
    LoadResponse = 1
}
