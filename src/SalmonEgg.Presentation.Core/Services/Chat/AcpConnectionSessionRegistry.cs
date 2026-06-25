using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record AcpConnectionSession(
    string ProfileId,
    AcpChatServiceAdapter Service,
    InitializeResponse InitializeResponse,
    AcpConnectionReuseKey ConnectionReuseKey,
    string? ConnectionInstanceId = null)
{
    public DateTime LastUsedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Publishes fine-grained connection lifecycle events keyed by profileId.
/// Subscribers (e.g. AgentProfileItemViewModel) can react without polling the registry.
/// NOTE: Events may be raised on any thread; subscribers must dispatch to the UI thread themselves.
/// </summary>
public interface IAcpConnectionSessionEvents
{
    /// <summary>
    /// Raised after a session is upserted (isConnected=true) or removed (isConnected=false).
    /// Parameters: (profileId, isConnected).
    /// </summary>
    event Action<string, bool>? ProfileConnectionChanged;
}

public interface IAcpConnectionSessionRegistry
{
    bool TryGetByProfile(string profileId, out AcpConnectionSession session);

    bool TryGetProfileId(IChatService service, out string profileId);

    void Upsert(AcpConnectionSession session);

    bool RemoveByProfile(string profileId);

    bool RemoveByService(IChatService service, out string profileId);

    IReadOnlyList<AcpConnectionSession> RemoveWhere(Func<AcpConnectionSession, bool> predicate);

    bool Touch(string profileId, DateTime? usedAtUtc = null);

    IReadOnlyList<AcpConnectionSession> GetSnapshot();
}

public sealed class InMemoryAcpConnectionSessionRegistry : IAcpConnectionSessionRegistry, IAcpConnectionSessionEvents
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AcpConnectionSession> _sessionsByProfile = new(StringComparer.Ordinal);
    private readonly Dictionary<IChatService, string> _profileIdByService = new();

    /// <inheritdoc />
    public event Action<string, bool>? ProfileConnectionChanged;

    public bool TryGetByProfile(string profileId, out AcpConnectionSession session)
    {
        lock (_gate)
        {
            return _sessionsByProfile.TryGetValue(profileId, out session!);
        }
    }

    public bool TryGetProfileId(IChatService service, out string profileId)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_gate)
        {
            if (_profileIdByService.TryGetValue(service, out var foundProfileId))
            {
                profileId = foundProfileId;
                return true;
            }
        }

        profileId = string.Empty;
        return false;
    }

    public void Upsert(AcpConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_gate)
        {
            if (_sessionsByProfile.TryGetValue(session.ProfileId, out var existingSession) && !ReferenceEquals(existingSession.Service, session.Service))
            {
                _profileIdByService.Remove(existingSession.Service);
            }

            _sessionsByProfile[session.ProfileId] = session with
            {
                LastUsedUtc = session.LastUsedUtc == default ? DateTime.UtcNow : session.LastUsedUtc
            };
            _profileIdByService[session.Service] = session.ProfileId;
        }

        // Raise outside the lock to avoid potential deadlocks from re-entrant subscribers.
        ProfileConnectionChanged?.Invoke(session.ProfileId, true);
    }

    public bool RemoveByProfile(string profileId)
    {
        bool removed;
        lock (_gate)
        {
            if (_sessionsByProfile.TryGetValue(profileId, out var session))
            {
                _profileIdByService.Remove(session.Service);
                removed = _sessionsByProfile.Remove(profileId);
            }
            else
            {
                removed = false;
            }
        }

        if (removed)
        {
            ProfileConnectionChanged?.Invoke(profileId, false);
        }

        return removed;
    }

    public bool RemoveByService(IChatService service, out string profileId)
    {
        ArgumentNullException.ThrowIfNull(service);
        bool removed = false;
        profileId = string.Empty;

        lock (_gate)
        {
            if (_profileIdByService.TryGetValue(service, out var foundProfileId))
            {
                profileId = foundProfileId;
                _profileIdByService.Remove(service);
                _sessionsByProfile.Remove(profileId);
                removed = true;
            }
        }

        if (removed)
        {
            ProfileConnectionChanged?.Invoke(profileId, false);
        }

        return removed;
    }

    public IReadOnlyList<AcpConnectionSession> RemoveWhere(Func<AcpConnectionSession, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var removed = new List<AcpConnectionSession>();
        lock (_gate)
        {
            var keysToRemove = new List<string>();
            foreach (var pair in _sessionsByProfile)
            {
                if (!predicate(pair.Value))
                {
                    continue;
                }

                keysToRemove.Add(pair.Key);
                removed.Add(pair.Value);
            }

            foreach (var key in keysToRemove)
            {
                if (_sessionsByProfile.TryGetValue(key, out var session))
                {
                    _profileIdByService.Remove(session.Service);
                    _sessionsByProfile.Remove(key);
                }
            }
        }

        // Raise after all mutations are complete so subscribers see a consistent view.
        var handler = ProfileConnectionChanged;
        if (handler != null)
        {
            foreach (var session in removed)
            {
                handler(session.ProfileId, false);
            }
        }

        return removed;
    }

    public bool Touch(string profileId, DateTime? usedAtUtc = null)
    {
        lock (_gate)
        {
            if (!_sessionsByProfile.TryGetValue(profileId, out var session))
            {
                return false;
            }

            _sessionsByProfile[profileId] = session with { LastUsedUtc = usedAtUtc ?? DateTime.UtcNow };
            return true;
        }
    }

    public IReadOnlyList<AcpConnectionSession> GetSnapshot()
    {
        lock (_gate)
        {
            return _sessionsByProfile.Values.ToArray();
        }
    }
}
