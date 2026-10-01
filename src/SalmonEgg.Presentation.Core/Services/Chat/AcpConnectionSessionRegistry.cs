using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record AcpConnectionSession(
    string ProfileId,
    AcpChatServiceAdapter Service,
    InitializeResponse InitializeResponse,
    AcpConnectionReuseKey ConnectionReuseKey,
    string? ConnectionInstanceId = null)
{
    public DateTime LastUsedUtc { get; init; } = DateTime.UtcNow;

    public AcpSessionEventSource EventSource => new(ProfileId, ConnectionInstanceId, Service);
}

public readonly record struct AcpSessionEventSource(
    string? ProfileId,
    string? ConnectionInstanceId,
    IChatService Service)
{
    public bool Matches(AcpSessionEventSource other)
        => ReferenceEquals(Service, other.Service)
            && string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
            && string.Equals(ConnectionInstanceId, other.ConnectionInstanceId, StringComparison.Ordinal);

    public bool Matches(AcpConnectionSession session)
        => ReferenceEquals(Service, session.Service)
            && string.Equals(ProfileId, session.ProfileId, StringComparison.Ordinal)
            && string.Equals(ConnectionInstanceId, session.ConnectionInstanceId, StringComparison.Ordinal);
}

public enum AcpConnectionRetirementReason
{
    Disconnected,
    Replaced,
    TransportLost,
    Evicted,
    Shutdown
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

    event Action<AcpConnectionSession>? ConnectionRegistered;

    event Action<AcpConnectionSession, AcpConnectionRetirementReason>? ConnectionRetired;
}

public interface IAcpConnectionSessionRegistry
{
    bool TryGetByProfile(string profileId, out AcpConnectionSession session);

    bool TryGetProfileId(IChatService service, out string profileId);

    AcpConnectionSession? Upsert(AcpConnectionSession session);

    bool RemoveByProfile(string profileId, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected);

    bool RemoveByService(IChatService service, out string profileId, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected);

    IReadOnlyList<AcpConnectionSession> RemoveWhere(Func<AcpConnectionSession, bool> predicate, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected);

    bool Touch(string profileId, DateTime? usedAtUtc = null);

    bool TryAcquireUsage(AcpSessionEventSource source, out IDisposable? lease);

    bool TryEvict(AcpConnectionSession expectedSession);

    IReadOnlyList<AcpConnectionSession> GetSnapshot();
}

public sealed class InMemoryAcpConnectionSessionRegistry : IAcpConnectionSessionRegistry, IAcpConnectionSessionEvents
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AcpConnectionSession> _sessionsByProfile = new(StringComparer.Ordinal);
    private readonly Dictionary<IChatService, string> _profileIdByService = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AcpSessionEventSource, int> _usageCounts = new();

    /// <inheritdoc />
    public event Action<string, bool>? ProfileConnectionChanged;

    public event Action<AcpConnectionSession>? ConnectionRegistered;

    public event Action<AcpConnectionSession, AcpConnectionRetirementReason>? ConnectionRetired;

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

    public AcpConnectionSession? Upsert(AcpConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(session.Service, nameof(AcpConnectionSession.Service));
        AcpConnectionSession? replaced;
        AcpConnectionSession? displacedSession = null;
        AcpConnectionSession storedSession;
        IDisposable registrationLease;
        lock (_gate)
        {
            _sessionsByProfile.TryGetValue(session.ProfileId, out replaced);
            if (replaced is not null && !ReferenceEquals(replaced.Service, session.Service))
            {
                _profileIdByService.Remove(replaced.Service);
            }

            if (_profileIdByService.TryGetValue(session.Service, out var existingProfileId)
                && !string.Equals(existingProfileId, session.ProfileId, StringComparison.Ordinal))
            {
                _sessionsByProfile.Remove(existingProfileId, out displacedSession);
            }

            storedSession = session with
            {
                LastUsedUtc = session.LastUsedUtc == default ? DateTime.UtcNow : session.LastUsedUtc
            };
            _sessionsByProfile[session.ProfileId] = storedSession;
            _profileIdByService[storedSession.Service] = storedSession.ProfileId;
            _usageCounts.TryGetValue(storedSession.EventSource, out var usageCount);
            _usageCounts[storedSession.EventSource] = checked(usageCount + 1);
            registrationLease = new UsageLease(this, storedSession.EventSource);
        }

        // Raise outside the lock to avoid potential deadlocks from re-entrant subscribers.
        using (registrationLease)
        {
            if (displacedSession is not null)
            {
                ConnectionRetired?.Invoke(displacedSession, AcpConnectionRetirementReason.Replaced);
                ProfileConnectionChanged?.Invoke(displacedSession.ProfileId, false);
            }

            if (replaced is not null && !storedSession.EventSource.Matches(replaced))
            {
                ConnectionRetired?.Invoke(replaced, AcpConnectionRetirementReason.Replaced);
            }

            ConnectionRegistered?.Invoke(storedSession);
            ProfileConnectionChanged?.Invoke(session.ProfileId, true);
        }
        return replaced;
    }

    public bool RemoveByProfile(string profileId, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected)
    {
        AcpConnectionSession session;
        lock (_gate)
        {
            if (!_sessionsByProfile.Remove(profileId, out session!))
            {
                return false;
            }

            _profileIdByService.Remove(session.Service);
        }

        ConnectionRetired?.Invoke(session, reason);
        ProfileConnectionChanged?.Invoke(profileId, false);

        return true;
    }

    public bool RemoveByService(IChatService service, out string profileId, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected)
    {
        ArgumentNullException.ThrowIfNull(service);
        AcpConnectionSession? removed;

        lock (_gate)
        {
            if (!_profileIdByService.Remove(service, out var foundProfileId))
            {
                profileId = string.Empty;
                return false;
            }

            profileId = foundProfileId;
            _sessionsByProfile.Remove(profileId, out removed);
        }

        if (removed is not null)
        {
            ConnectionRetired?.Invoke(removed, reason);
            ProfileConnectionChanged?.Invoke(profileId, false);
        }

        return removed is not null;
    }

    public IReadOnlyList<AcpConnectionSession> RemoveWhere(Func<AcpConnectionSession, bool> predicate, AcpConnectionRetirementReason reason = AcpConnectionRetirementReason.Disconnected)
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
                var session = _sessionsByProfile[key];
                _sessionsByProfile.Remove(key);
                _profileIdByService.Remove(session.Service);
            }
        }

        // Raise after all mutations are complete so subscribers see a consistent view.
        foreach (var session in removed)
        {
            ConnectionRetired?.Invoke(session, reason);
            ProfileConnectionChanged?.Invoke(session.ProfileId, false);
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

    public bool TryAcquireUsage(AcpSessionEventSource source, out IDisposable? lease)
    {
        lock (_gate)
        {
            if (source.ProfileId is null || !_sessionsByProfile.TryGetValue(source.ProfileId, out var current)
                || !source.Matches(current))
            {
                lease = null;
                return false;
            }

            _usageCounts.TryGetValue(source, out var count);
            _usageCounts[source] = checked(count + 1);
            lease = new UsageLease(this, source);
            return true;
        }
    }

    public bool TryEvict(AcpConnectionSession expectedSession)
    {
        lock (_gate)
        {
            if (!_sessionsByProfile.TryGetValue(expectedSession.ProfileId, out var current)
                || !expectedSession.EventSource.Matches(current)
                || _usageCounts.ContainsKey(expectedSession.EventSource)) return false;
            _sessionsByProfile.Remove(expectedSession.ProfileId);
            _profileIdByService.Remove(expectedSession.Service);
        }

        ConnectionRetired?.Invoke(expectedSession, AcpConnectionRetirementReason.Evicted);
        ProfileConnectionChanged?.Invoke(expectedSession.ProfileId, false);
        return true;
    }

    private void ReleaseUsage(AcpSessionEventSource source)
    {
        lock (_gate)
        {
            if (!_usageCounts.TryGetValue(source, out var count)) return;
            if (count == 1) _usageCounts.Remove(source);
            else _usageCounts[source] = count - 1;
        }
    }

    private sealed class UsageLease(InMemoryAcpConnectionSessionRegistry owner, AcpSessionEventSource source) : IDisposable
    {
        private InMemoryAcpConnectionSessionRegistry? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseUsage(source);
    }
}
