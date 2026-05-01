using System.Collections.Generic;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

public sealed record ChatProjectAffinityCorrectionState(
    IReadOnlyList<ProjectAffinityOverrideOptionViewModel> Options,
    bool IsVisible,
    bool HasOverride,
    string? EffectiveProjectId,
    ProjectAffinitySource EffectiveSource,
    string Message,
    string? SelectedOverrideProjectId);
