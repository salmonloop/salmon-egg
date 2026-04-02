using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class AcpConnectionEvictionOptions
{
    public bool EnablePolicyEviction { get; set; }

    public TimeSpan? IdleTtl { get; set; }

    public int? MaxWarmProfiles { get; set; }
}

public readonly record struct AcpConnectionEvictionContext(
    DateTime UtcNow,
    int ActiveCount);

public interface IAcpConnectionEvictionPolicy
{
    IReadOnlyList<string> GetProfilesToEvict(
        IReadOnlyList<AcpConnectionSession> candidates,
        AcpConnectionEvictionContext context);
}

public sealed class ConservativeAcpConnectionEvictionPolicy : IAcpConnectionEvictionPolicy
{
    private readonly AcpConnectionEvictionOptions _options;

    public ConservativeAcpConnectionEvictionPolicy(AcpConnectionEvictionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<string> GetProfilesToEvict(
        IReadOnlyList<AcpConnectionSession> candidates,
        AcpConnectionEvictionContext context)
    {
        if (!_options.EnablePolicyEviction || candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ordered = candidates
            .OrderBy(session => session.LastUsedUtc)
            .ToList();
        var evictProfiles = new HashSet<string>(StringComparer.Ordinal);

        if (_options.IdleTtl is { } idleTtl && idleTtl > TimeSpan.Zero)
        {
            var idleCutoff = context.UtcNow - idleTtl;
            foreach (var session in ordered)
            {
                if (session.LastUsedUtc > idleCutoff)
                {
                    continue;
                }

                evictProfiles.Add(session.ProfileId);
            }
        }

        if (_options.MaxWarmProfiles is { } maxWarmProfiles && maxWarmProfiles >= 0)
        {
            var remaining = ordered
                .Where(session => !evictProfiles.Contains(session.ProfileId))
                .ToList();
            var overflow = remaining.Count - maxWarmProfiles;
            if (overflow > 0)
            {
                foreach (var session in remaining.Take(overflow))
                {
                    evictProfiles.Add(session.ProfileId);
                }
            }
        }

        return evictProfiles.ToArray();
    }
}

