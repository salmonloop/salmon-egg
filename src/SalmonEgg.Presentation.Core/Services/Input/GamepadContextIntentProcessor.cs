using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Input;

public sealed class GamepadContextIntentProcessor
{
    private readonly HashSet<GamepadContextIntent> _pressedIntents = [];

    public IReadOnlyCollection<GamepadContextIntent> Process(GamepadInputReading reading)
    {
        var activeIntents = GamepadContextIntentProjector.GetActiveIntents(reading);
        var raisedIntents = new List<GamepadContextIntent>();

        foreach (var intent in activeIntents)
        {
            if (_pressedIntents.Add(intent))
            {
                raisedIntents.Add(intent);
            }
        }

        RemoveReleasedIntents(activeIntents);
        return raisedIntents;
    }

    public void Reset()
    {
        _pressedIntents.Clear();
    }

    private void RemoveReleasedIntents(IReadOnlyCollection<GamepadContextIntent> activeIntents)
    {
        if (_pressedIntents.Count == 0)
        {
            return;
        }

        var releasedIntents = new List<GamepadContextIntent>();
        foreach (var intent in _pressedIntents)
        {
            if (!activeIntents.Contains(intent))
            {
                releasedIntents.Add(intent);
            }
        }

        foreach (var intent in releasedIntents)
        {
            _pressedIntents.Remove(intent);
        }
    }
}
