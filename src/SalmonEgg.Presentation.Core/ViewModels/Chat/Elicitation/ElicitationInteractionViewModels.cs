using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Chat.Elicitation;

public sealed partial class ElicitationRequestViewModel : ObservableObject
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private string? _errorResourceKey;

    public ElicitationRequestViewModel(
        object messageId,
        string? sessionId,
        string prompt,
        IEnumerable<ElicitationFieldViewModel> fields,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
        SessionId = sessionId;
        Prompt = prompt ?? string.Empty;
        _localizer = localizer;

        foreach (var field in fields)
        {
            field.Changed += OnFieldChanged;
            Fields.Add(field);
        }
    }

    public object MessageId { get; }

    public string? SessionId { get; }

    public string Prompt { get; }

    public ObservableCollection<ElicitationFieldViewModel> Fields { get; } = new();

    public Func<ElicitationAcceptContent, Task<bool>>? OnAccept { get; set; }

    public Func<Task<bool>>? OnDecline { get; set; }

    public Func<Task<bool>>? OnCancel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private bool _isSubmitting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanSubmit => !IsSubmitting && ValidateFields();

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (OnAccept is null)
        {
            SetLocalizedError("Elicitation_SubmitUnavailable", "This form cannot be submitted right now.");
            return;
        }

        if (!ValidateFields())
        {
            SetLocalizedError("Elicitation_InvalidForm", "Check the highlighted fields before submitting.");
            return;
        }

        var content = new ElicitationAcceptContent();
        foreach (var field in Fields)
        {
            field.Write(content);
        }

        await RespondAsync(() => OnAccept(content)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        if (OnDecline is null)
        {
            SetLocalizedError("Elicitation_ResponseUnavailable", "This request can no longer be answered.");
            return;
        }

        await RespondAsync(OnDecline).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (OnCancel is null)
        {
            SetLocalizedError("Elicitation_ResponseUnavailable", "This request can no longer be answered.");
            return;
        }

        await RespondAsync(OnCancel).ConfigureAwait(true);
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        SubmitCommand.NotifyCanExecuteChanged();
    }

    public void ReprojectLocalizedState()
    {
        foreach (var field in Fields)
        {
            field.ReprojectLocalizedState();
        }

        if (!string.IsNullOrWhiteSpace(_errorResourceKey))
        {
            ErrorMessage = Localize(_errorResourceKey, ErrorMessage);
        }
    }

    private async Task RespondAsync(Func<Task<bool>> respond)
    {
        ClearError();
        IsSubmitting = true;
        try
        {
            if (!await respond().ConfigureAwait(true))
            {
                SetLocalizedError("Elicitation_ResponseFailed", "The response could not be sent. Please try again.");
            }
        }
        catch (Exception ex)
        {
            SetRawError(string.IsNullOrWhiteSpace(ex.Message)
                ? Localize("Elicitation_ResponseFailed", "The response could not be sent. Please try again.")
                : ex.Message);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool ValidateFields()
    {
        var valid = true;
        foreach (var field in Fields)
        {
            valid &= field.Validate();
        }

        return valid;
    }

    private void OnFieldChanged(object? sender, EventArgs e)
    {
        ClearError();
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private void SetLocalizedError(string resourceKey, string fallback)
    {
        _errorResourceKey = resourceKey;
        ErrorMessage = Localize(resourceKey, fallback);
    }

    private void SetRawError(string message)
    {
        _errorResourceKey = null;
        ErrorMessage = message;
    }

    private void ClearError()
    {
        _errorResourceKey = null;
        ErrorMessage = string.Empty;
    }

    private string Localize(string key, string fallback)
    {
        if (_localizer is null)
        {
            return fallback;
        }

        var localized = _localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }
}

public abstract partial class ElicitationFieldViewModel : ObservableObject
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    protected ElicitationFieldViewModel(
        string name,
        string? title,
        string? description,
        bool isRequired,
        IStringLocalizer<CoreStrings>? localizer)
    {
        Name = name;
        Title = string.IsNullOrWhiteSpace(title) ? name : title;
        Description = description ?? string.Empty;
        IsRequired = isRequired;
        _localizer = localizer;
    }

    public string Name { get; }

    public string Title { get; }

    public string Description { get; }

    public bool IsRequired { get; }

    public abstract string FieldInput { get; set; }

    public virtual bool BooleanValue
    {
        get => false;
        set { }
    }

    public virtual ObservableCollection<ElicitationMultiSelectOptionViewModel>? MultiSelectOptions => null;

    public virtual bool IsStringField => false;

    public virtual bool IsIntegerField => false;

    public virtual bool IsNumberField => false;

    public virtual bool IsBooleanField => false;

    public virtual bool IsMultiSelectField => false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public event EventHandler? Changed;

    public abstract bool Validate();

    public abstract void Write(ElicitationAcceptContent content);

    public virtual void ReprojectLocalizedState()
    {
    }

    protected void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected void SetError(string fallback)
    {
        if (_localizer is null)
        {
            ErrorMessage = fallback;
            return;
        }

        var localized = _localizer["Elicitation_InvalidValue"];
        ErrorMessage = localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }

    protected void ClearError()
    {
        ErrorMessage = string.Empty;
    }
}

public sealed partial class ElicitationStringFieldViewModel : ElicitationFieldViewModel
{
    private readonly uint? _minLength;
    private readonly uint? _maxLength;
    private readonly string? _pattern;

    public ElicitationStringFieldViewModel(
        string name,
        StringPropertySchema schema,
        bool isRequired,
        IStringLocalizer<CoreStrings>? localizer)
        : base(name, schema.Title, schema.Description, isRequired, localizer)
    {
        _minLength = schema.MinLength;
        _maxLength = schema.MaxLength;
        _pattern = schema.Pattern;
        Format = schema.Format?.Value ?? string.Empty;
        foreach (var value in schema.Enum ?? new List<string>())
        {
            Options.Add(value);
        }

        foreach (var option in schema.OneOf ?? new List<EnumOption>())
        {
            Options.Add(option.Const);
        }

        Value = schema.Default ?? string.Empty;
    }

    public override bool IsStringField => true;

    public string Format { get; }

    public bool HasOptions => Options.Count > 0;

    public ObservableCollection<string> Options { get; } = new();

    [ObservableProperty]
    private string _value = string.Empty;

    public override string FieldInput
    {
        get => Value;
        set => Value = value;
    }

    partial void OnValueChanged(string value)
    {
        ClearError();
        RaiseChanged();
    }

    public override bool Validate()
    {
        if (IsRequired && string.IsNullOrWhiteSpace(Value))
        {
            SetError("This field is required.");
            return false;
        }

        if (Value.Length == 0)
        {
            ClearError();
            return true;
        }

        if ((_minLength.HasValue && Value.Length < _minLength.Value)
            || (_maxLength.HasValue && Value.Length > _maxLength.Value)
            || (HasOptions && !Options.Contains(Value, StringComparer.Ordinal)))
        {
            SetError("This value is not allowed.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_pattern))
        {
            try
            {
                if (!Regex.IsMatch(Value, _pattern, RegexOptions.CultureInvariant))
                {
                    SetError("This value does not match the required pattern.");
                    return false;
                }
            }
            catch (ArgumentException)
            {
                SetError("The agent supplied an invalid validation pattern.");
                return false;
            }
        }

        ClearError();
        return true;
    }

    public override void Write(ElicitationAcceptContent content)
    {
        if (Value.Length > 0 || IsRequired)
        {
            content.SetString(Name, Value);
        }
    }
}

public sealed partial class ElicitationIntegerFieldViewModel : ElicitationFieldViewModel
{
    private readonly long? _minimum;
    private readonly long? _maximum;

    public ElicitationIntegerFieldViewModel(string name, IntegerPropertySchema schema, bool isRequired, IStringLocalizer<CoreStrings>? localizer)
        : base(name, schema.Title, schema.Description, isRequired, localizer)
    {
        _minimum = schema.Minimum;
        _maximum = schema.Maximum;
        Value = schema.Default?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public override bool IsIntegerField => true;

    [ObservableProperty]
    private string _value = string.Empty;

    public override string FieldInput
    {
        get => Value;
        set => Value = value;
    }

    partial void OnValueChanged(string value)
    {
        ClearError();
        RaiseChanged();
    }

    public override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            if (IsRequired)
            {
                SetError("This field is required.");
                return false;
            }

            ClearError();
            return true;
        }

        if (!long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || (_minimum.HasValue && parsed < _minimum.Value)
            || (_maximum.HasValue && parsed > _maximum.Value))
        {
            SetError("Enter a valid integer in the allowed range.");
            return false;
        }

        ClearError();
        return true;
    }

    public override void Write(ElicitationAcceptContent content)
    {
        if (long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            content.SetInteger(Name, parsed);
        }
    }
}

public sealed partial class ElicitationNumberFieldViewModel : ElicitationFieldViewModel
{
    private readonly double? _minimum;
    private readonly double? _maximum;

    public ElicitationNumberFieldViewModel(string name, NumberPropertySchema schema, bool isRequired, IStringLocalizer<CoreStrings>? localizer)
        : base(name, schema.Title, schema.Description, isRequired, localizer)
    {
        _minimum = schema.Minimum;
        _maximum = schema.Maximum;
        Value = schema.Default?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public override bool IsNumberField => true;

    [ObservableProperty]
    private string _value = string.Empty;

    public override string FieldInput
    {
        get => Value;
        set => Value = value;
    }

    partial void OnValueChanged(string value)
    {
        ClearError();
        RaiseChanged();
    }

    public override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            if (IsRequired)
            {
                SetError("This field is required.");
                return false;
            }

            ClearError();
            return true;
        }

        if (!double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed)
            || (_minimum.HasValue && parsed < _minimum.Value)
            || (_maximum.HasValue && parsed > _maximum.Value))
        {
            SetError("Enter a valid number in the allowed range.");
            return false;
        }

        ClearError();
        return true;
    }

    public override void Write(ElicitationAcceptContent content)
    {
        if (double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            content.SetNumber(Name, parsed);
        }
    }
}

public sealed partial class ElicitationBooleanFieldViewModel : ElicitationFieldViewModel
{
    public ElicitationBooleanFieldViewModel(string name, BooleanPropertySchema schema, bool isRequired, IStringLocalizer<CoreStrings>? localizer)
        : base(name, schema.Title, schema.Description, isRequired, localizer)
    {
        Value = schema.Default ?? false;
    }

    public override bool IsBooleanField => true;

    [ObservableProperty]
    private bool _value;

    public override string FieldInput
    {
        get => Value.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (bool.TryParse(value, out var parsed))
            {
                Value = parsed;
            }
        }
    }

    public override bool BooleanValue
    {
        get => Value;
        set => Value = value;
    }

    partial void OnValueChanged(bool value)
    {
        ClearError();
        RaiseChanged();
    }

    public override bool Validate()
    {
        ClearError();
        return true;
    }

    public override void Write(ElicitationAcceptContent content)
    {
        content.SetBoolean(Name, Value);
    }
}

public sealed partial class ElicitationMultiSelectFieldViewModel : ElicitationFieldViewModel
{
    private readonly uint? _minItems;
    private readonly uint? _maxItems;

    public ElicitationMultiSelectFieldViewModel(string name, MultiSelectPropertySchema schema, bool isRequired, IStringLocalizer<CoreStrings>? localizer)
        : base(name, schema.Title, schema.Description, isRequired, localizer)
    {
        _minItems = schema.MinItems;
        _maxItems = schema.MaxItems;
        var defaults = new HashSet<string>(schema.Default ?? new List<string>(), StringComparer.Ordinal);
        var values = schema.Items switch
        {
            StringMultiSelectItems strings => strings.Enum,
            TitledMultiSelectItems titled => titled.AnyOf.Select(static item => item.Const).ToList(),
            _ => new List<string>()
        };

        foreach (var value in values)
        {
            var option = new ElicitationMultiSelectOptionViewModel(value, defaults.Contains(value));
            option.Changed += (_, _) => RaiseChanged();
            Options.Add(option);
        }
    }

    public override bool IsMultiSelectField => true;

    public override string FieldInput
    {
        get => string.Join(", ", Options.Where(static option => option.IsSelected).Select(static option => option.Value));
        set
        {
            var selected = new HashSet<string>(
                (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            foreach (var option in Options)
            {
                option.IsSelected = selected.Contains(option.Value);
            }
        }
    }

    public ObservableCollection<ElicitationMultiSelectOptionViewModel> Options { get; } = new();

    public override ObservableCollection<ElicitationMultiSelectOptionViewModel>? MultiSelectOptions => Options;

    public override bool Validate()
    {
        var selected = Options.Count(static option => option.IsSelected);
        if ((IsRequired && selected == 0)
            || (_minItems.HasValue && selected < _minItems.Value)
            || (_maxItems.HasValue && selected > _maxItems.Value))
        {
            SetError("Choose an allowed number of options.");
            return false;
        }

        ClearError();
        return true;
    }

    public override void Write(ElicitationAcceptContent content)
    {
        content.SetStringArray(Name, Options.Where(static option => option.IsSelected).Select(static option => option.Value));
    }
}

public sealed partial class ElicitationMultiSelectOptionViewModel : ObservableObject
{
    public ElicitationMultiSelectOptionViewModel(string value, bool isSelected)
    {
        Value = value;
        IsSelected = isSelected;
    }

    public string Value { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event EventHandler? Changed;

    partial void OnIsSelectedChanged(bool value)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public static class ElicitationInteractionViewModelFactory
{
    public static ElicitationRequestViewModel Create(
        ElicitationRequestEventArgs args,
        Func<Task> clearPendingRequestAsync,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(clearPendingRequestAsync);

        var form = args.Request as FormElicitationRequest
            ?? throw new ArgumentException("Only form elicitation requests can be rendered.", nameof(args));
        var required = new HashSet<string>(form.RequestedSchema.Required ?? new List<string>(), StringComparer.Ordinal);
        var fields = new List<ElicitationFieldViewModel>();
        foreach (var property in form.RequestedSchema.Properties)
        {
            var field = CreateField(property.Key, property.Value, required.Contains(property.Key), localizer);
            if (field is not null)
            {
                fields.Add(field);
            }
        }

        var viewModel = new ElicitationRequestViewModel(args.MessageId, args.SessionId, args.Request.Message, fields, localizer);
        viewModel.OnAccept = async content => await RespondAndClearAsync(() => args.Accept(content), clearPendingRequestAsync).ConfigureAwait(true);
        viewModel.OnDecline = async () => await RespondAndClearAsync(args.Decline, clearPendingRequestAsync).ConfigureAwait(true);
        viewModel.OnCancel = async () => await RespondAndClearAsync(args.Cancel, clearPendingRequestAsync).ConfigureAwait(true);
        return viewModel;
    }

    private static async Task<bool> RespondAndClearAsync(Func<Task<bool>> respond, Func<Task> clear)
    {
        if (!await respond().ConfigureAwait(true))
        {
            return false;
        }

        await clear().ConfigureAwait(true);
        return true;
    }

    private static ElicitationFieldViewModel? CreateField(
        string name,
        ElicitationPropertySchema schema,
        bool isRequired,
        IStringLocalizer<CoreStrings>? localizer)
        => schema switch
        {
            StringPropertySchema stringSchema => new ElicitationStringFieldViewModel(name, stringSchema, isRequired, localizer),
            IntegerPropertySchema integerSchema => new ElicitationIntegerFieldViewModel(name, integerSchema, isRequired, localizer),
            NumberPropertySchema numberSchema => new ElicitationNumberFieldViewModel(name, numberSchema, isRequired, localizer),
            BooleanPropertySchema booleanSchema => new ElicitationBooleanFieldViewModel(name, booleanSchema, isRequired, localizer),
            MultiSelectPropertySchema multiSelectSchema => new ElicitationMultiSelectFieldViewModel(name, multiSelectSchema, isRequired, localizer),
            // The SDK preserves unknown schema variants for forward round-tripping; the UI must never
            // invent a control for one it does not understand.
            _ => null
        };
}
