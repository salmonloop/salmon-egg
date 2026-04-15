using System.Collections.Generic;

namespace SalmonEgg.Presentation.Models.Navigation;

public sealed record NavigationViewProjection(
    bool IsSettingsSelected,
    IReadOnlySet<string> ActiveProjectIds,
    IReadOnlySet<string> SelectedSessionIds);
