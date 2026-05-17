using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAcpSessionUpdateProjector
{
    AcpSessionUpdateDelta Project(SessionUpdateEventArgs args);

    AcpSessionUpdateDelta ProjectSessionNew(SessionNewResponse response);

    AcpSessionUpdateDelta ProjectSessionLoad(SessionLoadResponse response);
}

public sealed class AcpSessionUpdateProjector : IAcpSessionUpdateProjector
{
    public AcpSessionUpdateDelta Project(SessionUpdateEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Update switch
        {
            PlanUpdate planUpdate => new AcpSessionUpdateDelta(
                PlanEntries: MapPlanEntries(planUpdate.Entries),
                ShowPlanPanel: true,
                PlanTitle: string.IsNullOrWhiteSpace(planUpdate.Title) ? null : planUpdate.Title.Trim()),
            CurrentModeUpdate modeUpdate => new AcpSessionUpdateDelta(
                SelectedModeId: string.IsNullOrWhiteSpace(modeUpdate.CurrentModeId) ? null : modeUpdate.CurrentModeId),
            AvailableCommandsUpdate availableCommandsUpdate => new AcpSessionUpdateDelta(
                AvailableCommands: MapAvailableCommands(availableCommandsUpdate.AvailableCommands)),
            ConfigUpdateUpdate configUpdate => BuildConfigDelta(configUpdate.ConfigOptions),
            ConfigOptionUpdate optionUpdate => BuildConfigDelta(optionUpdate.ConfigOptions),
            SessionInfoUpdate sessionInfoUpdate => new AcpSessionUpdateDelta(
                SessionInfo: MapSessionInfo(sessionInfoUpdate)),
            UsageUpdate usageUpdate => new AcpSessionUpdateDelta(
                Usage: MapUsage(usageUpdate)),
            _ => AcpSessionUpdateDelta.Empty
        };
    }

    public AcpSessionUpdateDelta ProjectSessionNew(SessionNewResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return BuildSessionProjection(response.Modes, response.ConfigOptions);
    }

    public AcpSessionUpdateDelta ProjectSessionLoad(SessionLoadResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Modes == null && response.ConfigOptions == null)
        {
            return AcpSessionUpdateDelta.Empty;
        }

        return BuildSessionProjection(response.Modes, response.ConfigOptions);
    }

    private static AcpSessionUpdateDelta BuildConfigDelta(IReadOnlyList<ConfigOption>? configOptions)
    {
        var projectedOptions = configOptions?
            .Where(static option => option is not null)
            .Where(static option => IsSupportedConfigOptionType(option.Type))
            .Select(MapConfigOption)
            .ToArray() ?? Array.Empty<AcpConfigOptionSnapshot>();

        var modeOption = projectedOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "mode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Id, "mode", StringComparison.OrdinalIgnoreCase));

        var availableModes = modeOption?.Options?
            .Select(static option => new AcpModeOption(option.Value, option.Name, option.Description ?? string.Empty))
            .ToArray() ?? Array.Empty<AcpModeOption>();

        return new AcpSessionUpdateDelta(
            AvailableModes: availableModes,
            SelectedModeId: modeOption?.SelectedValue,
            ConfigOptions: projectedOptions,
            ShowConfigOptionsPanel: projectedOptions.Length > 0);
    }

    private static AcpSessionUpdateDelta BuildSessionProjection(
        SessionModesState? modes,
        IReadOnlyList<ConfigOption>? configOptions)
    {
        var configDelta = BuildConfigDelta(configOptions);
        var usesConfigOptions = configOptions is not null;
        var availableModes = usesConfigOptions
            ? configDelta.AvailableModes?.ToArray() ?? Array.Empty<AcpModeOption>()
            : modes?.AvailableModes?
                .Where(static mode => mode is not null)
                .Select(mode => new AcpModeOption(
                    mode!.Id ?? string.Empty,
                    mode.Name ?? string.Empty,
                    mode.Description ?? string.Empty))
                .ToArray() ?? Array.Empty<AcpModeOption>();

        var selectedModeId = usesConfigOptions
            ? configDelta.SelectedModeId ?? availableModes.FirstOrDefault()?.ModeId
            : string.IsNullOrWhiteSpace(modes?.CurrentModeId)
                ? configDelta.SelectedModeId ?? availableModes.FirstOrDefault()?.ModeId
                : modes!.CurrentModeId;

        return configDelta with
        {
            AvailableModes = availableModes,
            SelectedModeId = selectedModeId
        };
    }

    private static bool IsSupportedConfigOptionType(string? valueType)
        => string.Equals(valueType, "select", StringComparison.Ordinal);

    private static IReadOnlyList<ConversationPlanEntrySnapshot> MapPlanEntries(IReadOnlyList<PlanEntry>? entries)
        => entries?.Select(static entry => new ConversationPlanEntrySnapshot
        {
            Content = entry.Content ?? string.Empty,
            Status = entry.Status,
            Priority = entry.Priority
        }).ToArray() ?? Array.Empty<ConversationPlanEntrySnapshot>();

    private static IReadOnlyList<AcpAvailableCommandSnapshot> MapAvailableCommands(
        IReadOnlyList<AvailableCommand>? commands)
        => commands?
            .Where(static command => command is not null)
            .Select(static command => new AcpAvailableCommandSnapshot(
                command.Name ?? string.Empty,
                command.Description ?? string.Empty,
                command.Input?.Hint))
            .ToArray() ?? Array.Empty<AcpAvailableCommandSnapshot>();

    private static AcpSessionInfoSnapshot MapSessionInfo(SessionInfoUpdate update)
        => new(
            Title: update.Title,
            HasTitle: update.HasTitle,
            Description: null,
            // ACP session_info_update does not redefine cwd; cwd is established during
            // session/new or session/load and must remain immutable for the session.
            Cwd: null,
            UpdatedAt: update.UpdatedAt,
            HasUpdatedAt: update.HasUpdatedAt,
            Meta: update.Meta is null
                ? null
                : new ReadOnlyDictionary<string, object?>(
                    new Dictionary<string, object?>(update.Meta, StringComparer.Ordinal)));

    private static AcpUsageSnapshot MapUsage(UsageUpdate update)
        => new(
            Used: update.Used,
            Size: update.Size,
            Cost: update.Cost is null
                ? null
                : new AcpUsageCostSnapshot(update.Cost.Amount, update.Cost.Currency));

    private static AcpConfigOptionSnapshot MapConfigOption(ConfigOption option)
    {
        var projectedOptions = option.Options?
            .Select(static item => new AcpConfigOptionChoice(
                item.Value ?? string.Empty,
                string.IsNullOrWhiteSpace(item.Name) ? item.Value ?? string.Empty : item.Name,
                item.Description))
            .ToArray() ?? Array.Empty<AcpConfigOptionChoice>();

        return new AcpConfigOptionSnapshot(
            option.Id ?? string.Empty,
            option.Name ?? string.Empty,
            option.Description,
            option.Category,
            option.Type,
            option.CurrentValue,
            projectedOptions);
    }
}

public sealed record AcpSessionUpdateDelta(
    IReadOnlyList<AcpModeOption>? AvailableModes = null,
    string? SelectedModeId = null,
    IReadOnlyList<AcpConfigOptionSnapshot>? ConfigOptions = null,
    bool? ShowConfigOptionsPanel = null,
    IReadOnlyList<ConversationPlanEntrySnapshot>? PlanEntries = null,
    bool? ShowPlanPanel = null,
    string? PlanTitle = null,
    IReadOnlyList<AcpAvailableCommandSnapshot>? AvailableCommands = null,
    AcpSessionInfoSnapshot? SessionInfo = null,
    AcpUsageSnapshot? Usage = null)
{
    public static AcpSessionUpdateDelta Empty { get; } = new();
}

public sealed record AcpModeOption(string ModeId, string ModeName, string Description);

public sealed record AcpAvailableCommandSnapshot(string Name, string Description, string? InputHint);

public sealed record AcpConfigOptionChoice(string Value, string Name, string? Description);

public sealed partial record AcpConfigOptionSnapshot(
    string Id,
    string Name,
    string? Description,
    string? Category,
    string? ValueType,
    string? SelectedValue,
    IReadOnlyList<AcpConfigOptionChoice> Options);

public sealed record AcpSessionInfoSnapshot(
    string? Title,
    bool HasTitle,
    string? Description,
    string? Cwd,
    string? UpdatedAt,
    bool HasUpdatedAt,
    IReadOnlyDictionary<string, object?>? Meta);

public sealed record AcpUsageSnapshot(
    int? Used,
    int? Size,
    AcpUsageCostSnapshot? Cost);

public sealed record AcpUsageCostSnapshot(
    decimal? Amount,
    string? Currency);
