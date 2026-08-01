using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Controls;

public sealed partial class ToolCallPill : UserControl, INotifyPropertyChanged
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    private bool _isExpanded;
    private bool _hasManualExpansionOverride;

    public static readonly DependencyProperty ToolTitleProperty =
        DependencyProperty.Register(
            nameof(ToolTitle),
            typeof(string),
            typeof(ToolCallPill),
            new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty ToolKindProperty =
        DependencyProperty.Register(
            nameof(ToolKind),
            typeof(ToolCallKind?),
            typeof(ToolCallPill),
            new PropertyMetadata(null, OnDisplayInputChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(ToolCallStatus?), typeof(ToolCallPill), new PropertyMetadata(null, OnDisplayInputChanged));

    public static readonly DependencyProperty SummaryProperty =
        DependencyProperty.Register(
            nameof(Summary),
            typeof(string),
            typeof(ToolCallPill),
            new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty RawInputProperty =
        DependencyProperty.Register(
            nameof(RawInput),
            typeof(string),
            typeof(ToolCallPill),
            new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty RawOutputProperty =
        DependencyProperty.Register(
            nameof(RawOutput),
            typeof(string),
            typeof(ToolCallPill),
            new PropertyMetadata(string.Empty, OnDisplayInputChanged));

    public static readonly DependencyProperty DetailItemsProperty =
        DependencyProperty.Register(nameof(DetailItems), typeof(IReadOnlyList<ToolCallDetailItem>), typeof(ToolCallPill), new PropertyMetadata(null, OnDisplayInputChanged));

    public static readonly DependencyProperty PendingPermissionRequestProperty =
        DependencyProperty.Register(nameof(PendingPermissionRequest), typeof(PermissionRequestViewModel), typeof(ToolCallPill), new PropertyMetadata(null, OnPermissionInputChanged));

    public static readonly DependencyProperty IsInProgressProperty =
        DependencyProperty.Register(nameof(IsInProgress), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));

    public static readonly DependencyProperty IsCompletedProperty =
        DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));

    public static readonly DependencyProperty IsFailedProperty =
        DependencyProperty.Register(nameof(IsFailed), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));

    public static readonly DependencyProperty IsCancelledProperty =
        DependencyProperty.Register(nameof(IsCancelled), typeof(bool), typeof(ToolCallPill), new PropertyMetadata(false, OnVisualStateInputChanged));

    // Copy/Report are the AI-side message commands, projected in from the ChatMessageViewModel
    // via the message template. The pill owns the right-click menu leaves internally because its
    // selectable text (title/summary/detail/raw) installs its own text-selection command bar and
    // marks ContextRequested handled, so a bubble-level ContextFlyout never fires over the pill
    // body (AGENTS.md §7 leaf-owned ContextFlyout; whole-pill coverage per product intent).
    public static readonly DependencyProperty CopyCommandProperty =
        DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(ToolCallPill), new PropertyMetadata(null));

    public static readonly DependencyProperty CopyCommandParameterProperty =
        DependencyProperty.Register(nameof(CopyCommandParameter), typeof(object), typeof(ToolCallPill), new PropertyMetadata(null));

    public static readonly DependencyProperty ReportCommandProperty =
        DependencyProperty.Register(nameof(ReportCommand), typeof(ICommand), typeof(ToolCallPill), new PropertyMetadata(null));

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ToolTitle
    {
        get => (string)GetValue(ToolTitleProperty);
        set => SetValue(ToolTitleProperty, value);
    }

    public ToolCallKind? ToolKind
    {
        get => (ToolCallKind?)GetValue(ToolKindProperty);
        set => SetValue(ToolKindProperty, value);
    }

    public ToolCallStatus? Status
    {
        get => (ToolCallStatus?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public string RawInput
    {
        get => (string)GetValue(RawInputProperty);
        set => SetValue(RawInputProperty, value);
    }

    public string RawOutput
    {
        get => (string)GetValue(RawOutputProperty);
        set => SetValue(RawOutputProperty, value);
    }

    public IReadOnlyList<ToolCallDetailItem>? DetailItems
    {
        get => (IReadOnlyList<ToolCallDetailItem>?)GetValue(DetailItemsProperty);
        set => SetValue(DetailItemsProperty, value);
    }

    public PermissionRequestViewModel? PendingPermissionRequest
    {
        get => (PermissionRequestViewModel?)GetValue(PendingPermissionRequestProperty);
        set => SetValue(PendingPermissionRequestProperty, value);
    }

    public bool IsInProgress
    {
        get => (bool)GetValue(IsInProgressProperty);
        set => SetValue(IsInProgressProperty, value);
    }

    public bool IsCompleted
    {
        get => (bool)GetValue(IsCompletedProperty);
        set => SetValue(IsCompletedProperty, value);
    }

    public bool IsFailed
    {
        get => (bool)GetValue(IsFailedProperty);
        set => SetValue(IsFailedProperty, value);
    }

    public bool IsCancelled
    {
        get => (bool)GetValue(IsCancelledProperty);
        set => SetValue(IsCancelledProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => (ICommand?)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public object? CopyCommandParameter
    {
        get => GetValue(CopyCommandParameterProperty);
        set => SetValue(CopyCommandParameterProperty, value);
    }

    public ICommand? ReportCommand
    {
        get => (ICommand?)GetValue(ReportCommandProperty);
        set => SetValue(ReportCommandProperty, value);
    }

    public string DisplayToolName => ResolveToolName();

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool HasDisplayItems => DetailItems?.Count > 0;

    public bool HasPendingPermissionRequest => PendingPermissionRequest != null;

    public IReadOnlyList<PermissionOptionViewModel> PermissionOptions
    {
        get
        {
            var options = PendingPermissionRequest?.Options;
            return options is null ? Array.Empty<PermissionOptionViewModel>() : options;
        }
    }

    public bool HasRawInput => !string.IsNullOrWhiteSpace(RawInput);

    public bool HasRawOutput => !string.IsNullOrWhiteSpace(RawOutput);

    public bool HasRawPayload => HasRawInput || HasRawOutput;

    // Permission is decoupled from the payload expander (its own InfoBar sibling), so the
    // expander only governs inline detail/raw payload visibility, not approval visibility.
    public bool HasInlineContent => HasDisplayItems || HasRawPayload;

    public string AutomationName
    {
        get
        {
            var name = DisplayToolName;
            return HasSummary ? $"{name}, {Summary}" : name;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetIsExpanded(value, isUserInitiated: false);
    }

    public ToolCallPill()
    {
        InitializeComponent();
        NotifyDisplayChanged();
        OnPropertyChanged(nameof(HasPendingPermissionRequest));
        OnPropertyChanged(nameof(PermissionOptions));
        DataContextChanged += ToolCallPill_DataContextChanged;
        Loaded += ToolCallPill_Loaded;
    }

    private void ToolCallPill_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDefaultExpansionState();
    }

    private void ToolCallPill_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        _hasManualExpansionOverride = false;
        ApplyDefaultExpansionState();
    }

    private static void OnDisplayInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolCallPill pill)
        {
            pill.NotifyDisplayChanged();
        }
    }

    private static void OnPermissionInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolCallPill pill)
        {
            pill.OnPropertyChanged(nameof(HasPendingPermissionRequest));
            pill.OnPropertyChanged(nameof(PermissionOptions));
        }
    }

    private static void OnVisualStateInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolCallPill pill)
        {
            var propertyName =
                e.Property == IsInProgressProperty ? nameof(IsInProgress) :
                e.Property == IsCompletedProperty ? nameof(IsCompleted) :
                e.Property == IsFailedProperty ? nameof(IsFailed) :
                e.Property == IsCancelledProperty ? nameof(IsCancelled) :
                null;

            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                pill.OnPropertyChanged(propertyName);
                pill.ApplyDefaultExpansionState();
            }
        }
    }

    private void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(DisplayToolName));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(HasDisplayItems));
        OnPropertyChanged(nameof(HasRawInput));
        OnPropertyChanged(nameof(HasRawOutput));
        OnPropertyChanged(nameof(HasRawPayload));
        OnPropertyChanged(nameof(HasInlineContent));
        OnPropertyChanged(nameof(AutomationName));
        ApplyDefaultExpansionState();
    }

    private string ResolveToolName()
    {
        if (!string.IsNullOrWhiteSpace(ToolTitle))
        {
            return ToolTitle;
        }

        // ToolCallKind is an extensible value type (not a compile-time constant), so it
        // cannot appear as a switch pattern; compare against the named members instead
        // to keep the wire values single-sourced in the protocol type.
        var kind = ToolKind;
        if (kind == ToolCallKind.Read) return ResolveResourceString("ToolCallPillKindRead", "Read file");
        if (kind == ToolCallKind.Edit) return ResolveResourceString("ToolCallPillKindEdit", "Edit file");
        if (kind == ToolCallKind.Delete) return ResolveResourceString("ToolCallPillKindDelete", "Delete file");
        if (kind == ToolCallKind.Move) return ResolveResourceString("ToolCallPillKindMove", "Move file");
        if (kind == ToolCallKind.Search) return ResolveResourceString("ToolCallPillKindSearch", "Search code");
        if (kind == ToolCallKind.Execute) return ResolveResourceString("ToolCallPillKindExecute", "Run command");
        if (kind == ToolCallKind.SwitchMode) return ResolveResourceString("ToolCallPillKindSwitchMode", "Switch mode");
        if (kind == ToolCallKind.Think) return ResolveResourceString("ToolCallPillKindThink", "Thinking");
        if (kind == ToolCallKind.Fetch) return ResolveResourceString("ToolCallPillKindFetch", "Fetch data");
        return ResolveResourceString("ToolCallPillKindDefault", "Tool call");
    }

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RootExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        SetIsExpanded(true, isUserInitiated: true);
    }

    private void RootExpander_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        SetIsExpanded(false, isUserInitiated: true);
    }

    private void ApplyDefaultExpansionState()
    {
        SetIsExpanded(
            ToolCallPillExpansionPolicy.ResolveEffectiveExpanded(
                IsExpanded,
                IsCompleted,
                _hasManualExpansionOverride),
            isUserInitiated: false);
    }

    private void SetIsExpanded(bool value, bool isUserInitiated)
    {
        value = ToolCallPillExpansionPolicy.ShouldShowInlineContent(HasInlineContent, value);

        if (isUserInitiated)
        {
            _hasManualExpansionOverride = true;
        }

        if (_isExpanded == value)
        {
            return;
        }

        _isExpanded = value;
        OnPropertyChanged(nameof(IsExpanded));
    }
}
