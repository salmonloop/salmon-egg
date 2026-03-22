namespace SalmonEgg.Presentation.Utilities;

public sealed class InitialScrollGate
{
    private bool _pending = true;
    private bool _inFlight;
    private int _generation;

    public bool HasPending => _pending;
    public int Generation => _generation;

    public bool TrySchedule(int itemCount)
    {
        if (!_pending || _inFlight || itemCount <= 0)
        {
            return false;
        }

        _inFlight = true;
        return true;
    }

    public bool TryComplete(bool reachedBottom)
    {
        _inFlight = false;

        if (!_pending || !reachedBottom)
        {
            return false;
        }

        _pending = false;
        return true;
    }

    public void MarkPending()
    {
        _pending = true;
        _generation++;
    }

    public void ClearPending()
    {
        _pending = false;
        _inFlight = false;
        _generation++;
    }

    public void CancelInFlight()
    {
        _inFlight = false;
    }
}
