namespace SalmonEgg.Presentation.Logic;

public class SearchInteractionLogic
{
    public bool ShouldOpen(bool isFocused, string query)
    {
        return isFocused || !string.IsNullOrEmpty(query);
    }

    public bool ShouldClose(bool isFocused, bool isPopupFocused, string query)
    {
        return !isFocused && !isPopupFocused && string.IsNullOrEmpty(query);
    }
}
