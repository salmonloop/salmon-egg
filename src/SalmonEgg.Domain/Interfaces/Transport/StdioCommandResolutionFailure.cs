namespace SalmonEgg.Domain.Interfaces.Transport;

/// <summary>
/// Facts from the launcher's command resolution, retained independently of diagnostic wording.
/// </summary>
public sealed record StdioCommandResolutionFailure(string Command, bool SearchedOnPath);
