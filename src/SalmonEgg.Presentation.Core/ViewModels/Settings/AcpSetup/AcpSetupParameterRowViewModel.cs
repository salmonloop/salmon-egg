using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Settings.AcpSetup;

/// <summary>
/// One editable launch parameter. The wizard renders these generically from the adapter's declared
/// parameters, so adding an agent to the catalog never requires new form code.
/// </summary>
public sealed partial class AcpSetupParameterRowViewModel : ObservableObject
{
    private readonly Action? _onValueChanged;
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public AcpSetupParameterRowViewModel(
        AcpSetupParameterDefinition definition,
        Action? onValueChanged = null,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _onValueChanged = onValueChanged;
        _localizer = localizer;
        _value = definition.DefaultValue;
        AllowedValues = new ReadOnlyCollection<string>(new List<string>(definition.AllowedValues));
    }

    public AcpSetupParameterDefinition Definition { get; }

    /// <summary>Stable key used to correlate this row with the definition that produced it.</summary>
    public string Key => Definition.Key;

    public string DisplayName => Definition.DisplayName;

    /// <summary>
    /// The parameter's explanation, already localized. The definition carries a resource key, so
    /// resolution happens here rather than in the view; see
    /// <see cref="AcpSetupAgentRowViewModel.Description"/> for why the view cannot resolve it.
    /// </summary>
    public string Description
        => CoreStringResolver.Resolve(_localizer, Definition.Description, Definition.Description);

    public string Example => Definition.Example;

    public bool IsRequired => Definition.IsRequired;

    /// <summary>True when the parameter offers a closed set the UI can render as a picker.</summary>
    public bool HasAllowedValues => AllowedValues.Count > 0;

    public IReadOnlyList<string> AllowedValues { get; }

    /// <summary>
    /// The user's current value. Trimming is deliberately left to validation and plan building so the
    /// text box does not fight the user mid-edit.
    /// </summary>
    [ObservableProperty]
    private string _value;

    /// <summary>Localized validation message for this row, empty when the row is accepted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    partial void OnValueChanged(string value) => _onValueChanged?.Invoke();
}
