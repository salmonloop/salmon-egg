using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed partial class AskUserRequestViewModel : ObservableObject
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;
    private string? _errorResourceKey;

    public AskUserRequestViewModel(
        object messageId,
        string sessionId,
        string prompt,
        IEnumerable<AskUserQuestionViewModel> questions,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        Prompt = prompt ?? string.Empty;
        _localizer = localizer;

        foreach (var question in questions ?? Array.Empty<AskUserQuestionViewModel>())
        {
            question.SelectionChanged += OnQuestionSelectionChanged;
            Questions.Add(question);
        }
    }

    public object MessageId { get; }

    public string SessionId { get; }

    public string Prompt { get; }

    public ObservableCollection<AskUserQuestionViewModel> Questions { get; } = new();

    public Func<IReadOnlyDictionary<string, string>, Task<bool>>? OnSubmit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private bool _isSubmitting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanSubmit => !IsSubmitting && AreAllQuestionsAnswered();

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (OnSubmit is null)
        {
            SetLocalizedError("AskUser_SubmitUnavailable", "Answers cannot be submitted right now.");
            return;
        }

        var answers = BuildAnswers();
        if (answers.Count == 0 || !AreAllQuestionsAnswered())
        {
            SetLocalizedError("AskUser_AnswerAllRequired", "Answer all questions before submitting.");
            return;
        }

        ClearError();
        IsSubmitting = true;

        try
        {
            var succeeded = await OnSubmit(answers).ConfigureAwait(true);
            if (!succeeded)
            {
                SetLocalizedError("AskUser_SubmitFailed", "Failed to submit answers. Please try again.");
            }
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(ex.Message))
            {
                SetLocalizedError("AskUser_SubmitFailed", "Failed to submit answers. Please try again.");
            }
            else
            {
                SetRawError(ex.Message);
            }
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private void OnQuestionSelectionChanged(object? sender, EventArgs e)
    {
        ClearError();
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    private bool AreAllQuestionsAnswered()
    {
        if (Questions.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < Questions.Count; index++)
        {
            if (!Questions[index].HasSelection)
            {
                return false;
            }
        }

        return true;
    }

    private Dictionary<string, string> BuildAnswers()
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < Questions.Count; index++)
        {
            var question = Questions[index];
            var answer = question.BuildAnswer();
            if (!string.IsNullOrWhiteSpace(answer))
            {
                answers[question.QuestionText] = answer;
            }
        }

        return answers;
    }

    public void ReprojectLocalizedState()
    {
        for (var index = 0; index < Questions.Count; index++)
        {
            Questions[index].ReprojectLocalizedState();
        }

        if (string.IsNullOrWhiteSpace(_errorResourceKey))
        {
            return;
        }

        ErrorMessage = Localize(_errorResourceKey, ErrorMessage);
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

public sealed partial class AskUserQuestionViewModel : ObservableObject
{
    private readonly IStringLocalizer<CoreStrings>? _localizer;

    public AskUserQuestionViewModel(
        string header,
        string questionText,
        bool isMultiSelect,
        IEnumerable<AskUserOptionViewModel> options,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        Header = header ?? string.Empty;
        QuestionText = questionText ?? string.Empty;
        IsMultiSelect = isMultiSelect;
        _localizer = localizer;
        SelectionHint = ResolveSelectionHint();

        foreach (var option in options ?? Array.Empty<AskUserOptionViewModel>())
        {
            option.OnToggleRequested = ToggleOption;
            Options.Add(option);
        }
    }

    public string Header { get; }

    public string QuestionText { get; }

    public bool IsMultiSelect { get; }

    [ObservableProperty]
    private string _selectionHint = string.Empty;

    public ObservableCollection<AskUserOptionViewModel> Options { get; } = new();

    public bool HasSelection
    {
        get
        {
            for (var index = 0; index < Options.Count; index++)
            {
                if (Options[index].IsSelected)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public event EventHandler? SelectionChanged;

    public string BuildAnswer()
    {
        var selected = new List<string>();
        for (var index = 0; index < Options.Count; index++)
        {
            if (Options[index].IsSelected)
            {
                selected.Add(Options[index].Label);
            }
        }

        return string.Join(", ", selected);
    }

    private void ToggleOption(AskUserOptionViewModel option)
    {
        if (IsMultiSelect)
        {
            option.IsSelected = !option.IsSelected;
        }
        else
        {
            for (var index = 0; index < Options.Count; index++)
            {
                Options[index].IsSelected = ReferenceEquals(Options[index], option);
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReprojectLocalizedState()
    {
        var next = ResolveSelectionHint();
        if (!string.Equals(SelectionHint, next, StringComparison.Ordinal))
        {
            SelectionHint = next;
        }
    }

    private string ResolveSelectionHint()
        => IsMultiSelect
            ? Localize(_localizer, "AskUser_MultipleChoice", "Multiple choice")
            : Localize(_localizer, "AskUser_SingleChoice", "Single choice");

    private static string Localize(IStringLocalizer<CoreStrings>? localizer, string key, string fallback)
    {
        if (localizer is null)
        {
            return fallback;
        }

        var localized = localizer[key];
        return localized.ResourceNotFound || string.IsNullOrWhiteSpace(localized.Value)
            ? fallback
            : localized.Value;
    }
}

public sealed partial class AskUserOptionViewModel : ObservableObject
{
    public AskUserOptionViewModel(string label, string description)
    {
        Label = label ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public string Label { get; }

    public string Description { get; }

    public Action<AskUserOptionViewModel>? OnToggleRequested { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void ToggleSelected()
    {
        OnToggleRequested?.Invoke(this);
    }
}

public static class AskUserInteractionViewModelFactory
{
    public static AskUserRequestViewModel Create(
        AskUserRequest request,
        object messageId,
        Func<IReadOnlyDictionary<string, string>, Task<bool>> onSubmit,
        IStringLocalizer<CoreStrings>? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onSubmit);

        var questionViewModels = new List<AskUserQuestionViewModel>();
        for (var questionIndex = 0; questionIndex < request.Questions.Count; questionIndex++)
        {
            var question = request.Questions[questionIndex];
            var optionViewModels = new List<AskUserOptionViewModel>();
            for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
            {
                var option = question.Options[optionIndex];
                optionViewModels.Add(new AskUserOptionViewModel(option.Label, option.Description));
            }

            questionViewModels.Add(
                new AskUserQuestionViewModel(
                    question.Header,
                    question.Question,
                    question.MultiSelect,
                    optionViewModels,
                    localizer));
        }

        return new AskUserRequestViewModel(
            messageId,
            request.SessionId,
            AskUserContract.BuildPrompt(request.Questions),
            questionViewModels,
            localizer)
        {
            OnSubmit = onSubmit
        };
    }
}
