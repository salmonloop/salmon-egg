using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProfileSelection;

public sealed class ChatAcpProfileSelectionResolver
{
    public ServerConfiguration? ResolveById(
        IReadOnlyList<ServerConfiguration> profiles,
        string? profileId)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.Ordinal));
    }

    public ServerConfiguration? ResolveLoadedProfileSelection(
        IReadOnlyList<ServerConfiguration> profiles,
        ServerConfiguration? profile)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.Id))
        {
            return ResolveById(profiles, profile.Id);
        }

        return profiles.FirstOrDefault(candidate => ReferenceEquals(candidate, profile));
    }
}
