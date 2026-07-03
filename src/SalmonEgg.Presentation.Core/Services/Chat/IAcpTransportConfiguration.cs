using System.Collections.Generic;
using System.ComponentModel;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Transport configuration contract that the coordinator can mutate from a selected ACP profile.
/// A future adapter can wrap TransportConfigViewModel without exposing it directly.
/// </summary>
public interface IAcpTransportConfiguration : INotifyPropertyChanged
{
    TransportType SelectedTransportType { get; set; }

    string StdioCommand { get; set; }

    IReadOnlyList<string> StdioArguments { get; set; }

    string RemoteUrl { get; set; }

    (bool IsValid, string? ErrorMessage) Validate();
}
