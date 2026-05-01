using System.Collections.Generic;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;

public sealed record ChatProjectAffinityCorrectionInput(
    string? ConversationId,
    string? RemoteSessionId,
    string? BoundProfileId,
    string? RemoteCwd,
    string? OverrideProjectId,
    string? SelectedOverrideProjectId,
    IReadOnlyList<ProjectDefinition> Projects,
    IReadOnlyList<ProjectPathMapping> PathMappings);
