using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Models.Navigation;

public sealed record NavigationViewProjection(
    MainNavItemViewModel? ControlSelectedItem,
    bool IsSettingsSelected);
