namespace SalmonEgg.Presentation.Utilities;

public enum TranscriptScrollSettleObservation
{
    NotReadyYet = 0,
    ReadyButNotAtBottom = 1,
    AtBottom = 2,
}

public enum TranscriptScrollAction
{
    None = 0,
    IssueScrollRequest = 1,
    Completed = 2,
    Aborted = 3,
    Exhausted = 4,
}

public readonly record struct TranscriptScrollDecision(TranscriptScrollAction Action, int Generation);

public sealed class TranscriptScrollSettler
{
    private readonly int _maxReadyButNotBottomFailures;
    private TranscriptScrollSettlerState _state = TranscriptScrollSettlerState.Idle;
    private string? _sessionId;
    private int _generation;
    private int _activeRequestGeneration = -1;
    private int _readyButNotBottomFailures;

    public TranscriptScrollSettler(int maxReadyButNotBottomFailures = 8)
    {
        if (maxReadyButNotBottomFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReadyButNotBottomFailures));
        }

        _maxReadyButNotBottomFailures = maxReadyButNotBottomFailures;
    }

    public int Generation => _generation;

    public bool HasPendingWork => _state is TranscriptScrollSettlerState.Pending or TranscriptScrollSettlerState.Settling;

    public TranscriptScrollDecision BeginRound(string? sessionId)
    {
        _sessionId = sessionId;
        _generation++;
        _activeRequestGeneration = -1;
        _readyButNotBottomFailures = 0;
        _state = string.IsNullOrWhiteSpace(sessionId)
            ? TranscriptScrollSettlerState.Idle
            : TranscriptScrollSettlerState.Pending;

        return Decision(TranscriptScrollAction.None);
    }

    public TranscriptScrollDecision TryIssueScrollRequest(string? sessionId, bool hasMessages, bool isReady)
    {
        if (!MatchesSession(sessionId) || _state != TranscriptScrollSettlerState.Pending)
        {
            return Decision(TranscriptScrollAction.None);
        }

        if (!hasMessages)
        {
            TransitionToIdle();
            return Decision(TranscriptScrollAction.Completed);
        }

        if (!isReady)
        {
            return Decision(TranscriptScrollAction.None);
        }

        if (_readyButNotBottomFailures >= _maxReadyButNotBottomFailures)
        {
            TransitionToIdle();
            return Decision(TranscriptScrollAction.Exhausted);
        }

        _state = TranscriptScrollSettlerState.Settling;
        _activeRequestGeneration = _generation;
        return Decision(TranscriptScrollAction.IssueScrollRequest);
    }

    public TranscriptScrollDecision ReportSettled(string? sessionId, int generation, TranscriptScrollSettleObservation observation)
    {
        if (!MatchesSession(sessionId)
            || _state != TranscriptScrollSettlerState.Settling
            || generation != _activeRequestGeneration)
        {
            return Decision(TranscriptScrollAction.None);
        }

        _activeRequestGeneration = -1;

        switch (observation)
        {
            case TranscriptScrollSettleObservation.AtBottom:
                TransitionToIdle();
                return Decision(TranscriptScrollAction.Completed);

            case TranscriptScrollSettleObservation.ReadyButNotAtBottom:
                _readyButNotBottomFailures++;
                _state = TranscriptScrollSettlerState.Pending;
                return Decision(TranscriptScrollAction.None);

            default:
                _state = TranscriptScrollSettlerState.Pending;
                return Decision(TranscriptScrollAction.None);
        }
    }

    public TranscriptScrollDecision AbortForUserInteraction()
    {
        if (_state is not (TranscriptScrollSettlerState.Pending or TranscriptScrollSettlerState.Settling))
        {
            return Decision(TranscriptScrollAction.None);
        }

        TransitionToIdle();
        return Decision(TranscriptScrollAction.Aborted);
    }

    private bool MatchesSession(string? sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId)
            && string.Equals(_sessionId, sessionId, StringComparison.Ordinal);
    }

    private void TransitionToIdle()
    {
        _state = TranscriptScrollSettlerState.Idle;
        _activeRequestGeneration = -1;
    }

    private TranscriptScrollDecision Decision(TranscriptScrollAction action)
    {
        return new TranscriptScrollDecision(action, _generation);
    }

    private enum TranscriptScrollSettlerState
    {
        Idle = 0,
        Pending = 1,
        Settling = 2,
    }
}
