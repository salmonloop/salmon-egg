using System.Collections.Generic;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;

public sealed record ChatSessionOptionsProjection(
    IReadOnlyList<SessionModeViewModel> AvailableModes,
    string? SelectedModeId,
    IReadOnlyList<ConfigOptionViewModel> ConfigOptions,
    bool ShowConfigOptionsPanel,
    string? ModeConfigId);
