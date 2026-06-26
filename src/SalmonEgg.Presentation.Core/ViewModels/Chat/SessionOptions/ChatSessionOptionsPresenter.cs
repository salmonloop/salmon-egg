using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;

public sealed class ChatSessionOptionsPresenter
{
    public ChatSessionOptionsProjection Present(
        IReadOnlyList<ConversationModeOptionSnapshot> availableModes,
        string? selectedModeId,
        IReadOnlyList<ConversationConfigOptionSnapshot> configOptions,
        bool showConfigOptionsPanel)
    {
        ArgumentNullException.ThrowIfNull(availableModes);
        ArgumentNullException.ThrowIfNull(configOptions);

        var projectedModes = availableModes
            .Select(static mode => new SessionModeViewModel
            {
                ModeId = mode.ModeId,
                ModeName = mode.ModeName,
                Description = mode.Description
            })
            .ToList();

        var projectedConfigOptions = configOptions
            .Select(MapConfigOption)
            .ToList();

        var modeConfigSelection = TryResolveModeConfigSelection(projectedModes, projectedConfigOptions);
        var modelConfigSelection = TryResolveModelConfigSelection(projectedConfigOptions);
        var resolvedSelectedModeId = ResolveSelectedModeId(
            projectedModes,
            selectedModeId,
            modeConfigSelection.SelectedModeId);

        return new ChatSessionOptionsProjection(
            projectedModes,
            resolvedSelectedModeId,
            projectedConfigOptions,
            showConfigOptionsPanel,
            modeConfigSelection.ModeConfigId,
            modelConfigSelection.ModelOptions,
            modelConfigSelection.ModelConfigId,
            modelConfigSelection.SelectedModelValue);
    }

    public bool ModeCollectionMatches(
        IReadOnlyList<SessionModeViewModel> current,
        IReadOnlyList<SessionModeViewModel> projected)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(projected);

        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].ModeId, projected[i].ModeId, StringComparison.Ordinal)
                || !string.Equals(current[i].ModeName, projected[i].ModeName, StringComparison.Ordinal)
                || !string.Equals(current[i].Description, projected[i].Description, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public bool ConfigOptionCollectionMatches(
        IReadOnlyList<ConfigOptionViewModel> current,
        IReadOnlyList<ConfigOptionViewModel> projected)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(projected);

        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = projected[i];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal)
                || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || !string.Equals(left.Description, right.Description, StringComparison.Ordinal)
                || !string.Equals(left.Category, right.Category, StringComparison.Ordinal)
                || !string.Equals(left.ValueType, right.ValueType ?? "string", StringComparison.Ordinal)
                || !string.Equals(left.Value?.ToString(), right.Value?.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            if (left.Options.Count != right.Options.Count)
            {
                return false;
            }

            for (var optionIndex = 0; optionIndex < left.Options.Count; optionIndex++)
            {
                var leftOption = left.Options[optionIndex];
                var rightOption = right.Options[optionIndex];
                if (!string.Equals(leftOption.Value, rightOption.Value, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Name, rightOption.Name, StringComparison.Ordinal)
                    || !string.Equals(leftOption.Description, rightOption.Description, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool OptionCollectionMatches(
        IReadOnlyList<OptionValueViewModel> current,
        IReadOnlyList<OptionValueViewModel> projected)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(projected);

        if (current.Count != projected.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].Value, projected[i].Value, StringComparison.Ordinal)
                || !string.Equals(current[i].Name, projected[i].Name, StringComparison.Ordinal)
                || !string.Equals(current[i].Description, projected[i].Description, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public SessionModeViewModel? ResolveSelectedMode(
        IReadOnlyList<SessionModeViewModel> availableModes,
        string? selectedModeId)
    {
        ArgumentNullException.ThrowIfNull(availableModes);

        if (availableModes.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedModeId))
        {
            return availableModes[0];
        }

        return availableModes.FirstOrDefault(mode =>
                   string.Equals(mode.ModeId, selectedModeId, StringComparison.Ordinal))
               ?? availableModes[0];
    }

    public OptionValueViewModel? ResolveSelectedModelOption(
        IReadOnlyList<OptionValueViewModel> modelOptions,
        string? selectedModelValue)
    {
        ArgumentNullException.ThrowIfNull(modelOptions);

        if (modelOptions.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedModelValue))
        {
            return modelOptions[0];
        }

        return modelOptions.FirstOrDefault(option =>
                   string.Equals(option.Value, selectedModelValue, StringComparison.Ordinal))
               ?? modelOptions[0];
    }

    private static ConfigOptionViewModel MapConfigOption(ConversationConfigOptionSnapshot option)
    {
        var viewModel = new ConfigOptionViewModel
        {
            Id = option.Id,
            Name = option.Name,
            Description = option.Description,
            Category = option.Category,
            ValueType = option.ValueType ?? "string",
            IsRequired = true,
            Value = option.SelectedValue ?? string.Empty,
            TextValue = option.SelectedValue ?? string.Empty
        };

        if (option.Options.Count > 0)
        {
            viewModel.Options = new ObservableCollection<OptionValueViewModel>(
                option.Options.Select(static item => new OptionValueViewModel
                {
                    Value = item.Value,
                    Name = item.Name,
                    Description = item.Description
                }));
            viewModel.SelectedOption = viewModel.Options.FirstOrDefault(item => item.Value == option.SelectedValue);
        }

        return viewModel;
    }

    private static (string? ModeConfigId, string? SelectedModeId) TryResolveModeConfigSelection(
        List<SessionModeViewModel> projectedModes,
        IReadOnlyList<ConfigOptionViewModel> projectedConfigOptions)
    {
        var modeOption = projectedConfigOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "mode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Id, "mode", StringComparison.OrdinalIgnoreCase));

        if (modeOption is null || modeOption.Options.Count == 0)
        {
            return (null, null);
        }

        var projectedModesFromConfig = false;
        if (projectedModes.Count == 0)
        {
            projectedModesFromConfig = true;
            foreach (var option in modeOption.Options)
            {
                projectedModes.Add(new SessionModeViewModel
                {
                    ModeId = option.Value,
                    ModeName = option.Name,
                    Description = option.Description ?? string.Empty
                });
            }
        }

        var selectedModeId = projectedModesFromConfig
            ? null
            : modeOption.SelectedOption?.Value ?? modeOption.TextValue;
        return (modeOption.Id, selectedModeId);
    }

    private static (string? ModelConfigId, IReadOnlyList<OptionValueViewModel> ModelOptions, string? SelectedModelValue) TryResolveModelConfigSelection(
        IReadOnlyList<ConfigOptionViewModel> projectedConfigOptions)
    {
        var modelOption = projectedConfigOptions.FirstOrDefault(option =>
            string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase));

        if (modelOption is null || modelOption.Options.Count == 0)
        {
            return (null, Array.Empty<OptionValueViewModel>(), null);
        }

        var modelOptions = modelOption.Options
            .Select(static option => new OptionValueViewModel
            {
                Value = option.Value,
                Name = option.Name,
                Description = option.Description
            })
            .ToArray();

        var selectedValue = modelOption.SelectedOption?.Value ?? modelOption.TextValue;
        return (modelOption.Id, modelOptions, selectedValue);
    }

    private static string? ResolveSelectedModeId(
        IReadOnlyList<SessionModeViewModel> projectedModes,
        string? selectedModeId,
        string? configSelectedModeId)
    {
        if (projectedModes.Count == 0)
        {
            return null;
        }

        var preferredModeId = !string.IsNullOrWhiteSpace(selectedModeId)
            ? selectedModeId
            : configSelectedModeId;

        if (string.IsNullOrWhiteSpace(preferredModeId))
        {
            return projectedModes[0].ModeId;
        }

        return projectedModes.Any(mode => string.Equals(mode.ModeId, preferredModeId, StringComparison.Ordinal))
            ? preferredModeId
            : projectedModes[0].ModeId;
    }
}
