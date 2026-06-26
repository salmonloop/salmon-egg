namespace SalmonEgg.Presentation.Core.ViewModels.Composer;

public sealed record ComposerSelectorSlotsPresentation(
    ComposerSelectorSlotPresentation Agent,
    ComposerSelectorSlotPresentation Mode,
    ComposerSelectorSlotPresentation Project,
    ComposerSelectorSlotPresentation Model)
{
    public static ComposerSelectorSlotsPresentation Empty { get; } = new(
        Agent: ComposerSelectorSlotPresentation.Hidden(),
        Mode: ComposerSelectorSlotPresentation.Hidden(),
        Project: ComposerSelectorSlotPresentation.Hidden(),
        Model: ComposerSelectorSlotPresentation.Hidden());
}
