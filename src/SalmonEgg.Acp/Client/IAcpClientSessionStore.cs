using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SalmonEgg.Acp.Client
{
    public interface IAcpClientSessionStore
    {
        bool ContainsSession(string sessionId);

        Task CreateSessionAsync(string sessionId, string? cwd = null);

        bool RemoveSession(string sessionId);

        bool UpdateCurrentMode(string sessionId, string modeId);

        Task<bool> CancelSessionAsync(string sessionId, string? reason = null);
    }

    public sealed class InMemoryAcpClientSessionStore : IAcpClientSessionStore
    {
        private readonly ConcurrentDictionary<string, string?> _sessions = new();
        private readonly ConcurrentDictionary<string, string> _currentModes = new();

        public bool ContainsSession(string sessionId)
            => !string.IsNullOrWhiteSpace(sessionId) && _sessions.ContainsKey(sessionId);

        public Task CreateSessionAsync(string sessionId, string? cwd = null)
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessions.TryAdd(sessionId, cwd);
            }

            return Task.CompletedTask;
        }

        public bool RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            _currentModes.TryRemove(sessionId, out _);
            return _sessions.TryRemove(sessionId, out _);
        }

        public bool UpdateCurrentMode(string sessionId, string modeId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(modeId))
            {
                return false;
            }

            _currentModes[sessionId] = modeId;
            return _sessions.ContainsKey(sessionId);
        }

        public Task<bool> CancelSessionAsync(string sessionId, string? reason = null)
            => Task.FromResult(ContainsSession(sessionId));
    }
}
