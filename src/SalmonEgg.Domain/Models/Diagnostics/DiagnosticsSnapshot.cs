using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.Diagnostics;

public sealed class DiagnosticsSnapshot
{
    public string AppVersion { get; set; } = string.Empty;

    public string ProtocolVersion { get; set; } = string.Empty;

    public string OsDescription { get; set; } = string.Empty;

    public string FrameworkDescription { get; set; } = string.Empty;

    public Dictionary<string, string> Properties { get; set; } = new();
}

