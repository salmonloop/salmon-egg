namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Func<Action, bool> _tryEnqueue;

        public DispatcherQueue(Func<Action, bool> tryEnqueue)
        {
            _tryEnqueue = tryEnqueue ?? throw new ArgumentNullException(nameof(tryEnqueue));
        }

        public bool TryEnqueue(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return _tryEnqueue(callback);
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum FocusState
    {
        Unfocused = 0,
        Pointer = 1,
        Keyboard = 2,
        Programmatic = 3,
    }
}
