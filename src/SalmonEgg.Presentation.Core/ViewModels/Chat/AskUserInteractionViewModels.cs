using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public sealed partial class AskUserRequestViewModel : ObservableObject
{
    public AskUserRequestViewModel(
        object messageId,
        string sessionId,
        string prompt,
        IEnumerable<AskUserQuestionViewModel> questions)
    {
        MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        Prompt = prompt ?? string.Empty;

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
            ErrorMessage = "Answers cannot be submitted right now.";
            return;
        }

        var answers = BuildAnswers();
        if (answers.Count == 0 || !AreAllQuestionsAnswered())
        {
            ErrorMessage = "Answer all questions before submitting.";
            return;
        }

        ErrorMessage = string.Empty;
        IsSubmitting = true;

        try
        {
            var succeeded = await OnSubmit(answers).ConfigureAwait(true);
            if (!succeeded)
            {
                ErrorMessage = "Failed to submit answers. Please try again.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Failed to submit answers. Please try again."
                : ex.Message;
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
        ErrorMessage = string.Empty;
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
}

public sealed class AskUserQuestionViewModel
{
    public AskUserQuestionViewModel(string header, string questionText, bool isMultiSelect, IEnumerable<AskUserOptionViewModel> options)
    {
        Header = header ?? string.Empty;
        QuestionText = questionText ?? string.Empty;
        IsMultiSelect = isMultiSelect;
        SelectionHint = isMultiSelect ? "Multiple choice" : "Single choice";

        foreach (var option in options ?? Array.Empty<AskUserOptionViewModel>())
        {
            option.OnToggleRequested = ToggleOption;
            Options.Add(option);
        }
    }

    public string Header { get; }

    public string QuestionText { get; }

    public bool IsMultiSelect { get; }

    public string SelectionHint { get; }

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
        Func<IReadOnlyDictionary<string, string>, Task<bool>> onSubmit)
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
                    optionViewModels));
        }

        return new AskUserRequestViewModel(
        messageId,
        request.SessionId,
        AskUserContract.BuildPrompt(request.Questions),
        questionViewModels)
        {
            OnSubmit = onSubmit
        };
    }
}
