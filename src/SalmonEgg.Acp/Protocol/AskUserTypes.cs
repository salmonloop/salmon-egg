using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SalmonEgg.Acp.Protocol;

/// <summary>
/// Agent-to-client ask_user request payload.
/// Mirrors the question-based interaction contract used by built-in interaction tools.
/// </summary>
public sealed class AskUserRequest
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("questions")]
    public List<AskUserQuestion> Questions { get; init; } = new();
}

public sealed class AskUserQuestion
{
    [JsonPropertyName("header")]
    public string Header { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("options")]
    public List<AskUserOption> Options { get; init; } = new();

    [JsonPropertyName("multiSelect")]
    public bool MultiSelect { get; init; }
}

public sealed class AskUserOption
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

public sealed class AskUserResponse
{
    [JsonPropertyName("questions")]
    public List<AskUserQuestion> Questions { get; init; } = new();

    [JsonPropertyName("answers")]
    public Dictionary<string, string> Answers { get; init; } = new(StringComparer.Ordinal);

    public AskUserResponse()
    {
    }

    public AskUserResponse(IReadOnlyList<AskUserQuestion> questions, IReadOnlyDictionary<string, string> answers)
    {
        if (questions == null)
        {
            throw new ArgumentNullException(nameof(questions));
        }

        if (answers == null)
        {
            throw new ArgumentNullException(nameof(answers));
        }

        for (var index = 0; index < questions.Count; index++)
        {
            Questions.Add(questions[index]);
        }

        foreach (var pair in answers)
        {
            Answers[pair.Key] = pair.Value;
        }
    }
}

public static class AskUserContract
{
    public static void ValidateRequest(AskUserRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new InvalidOperationException("Missing sessionId.");
        }

        if (request.Questions.Count < 1 || request.Questions.Count > 4)
        {
            throw new InvalidOperationException("questions must contain between 1 and 4 items.");
        }

        var seenQuestions = new HashSet<string>(StringComparer.Ordinal);
        for (var questionIndex = 0; questionIndex < request.Questions.Count; questionIndex++)
        {
            var question = request.Questions[questionIndex];
            if (question is null)
            {
                throw new InvalidOperationException($"Question at index {questionIndex} is missing.");
            }

            if (string.IsNullOrWhiteSpace(question.Header))
            {
                throw new InvalidOperationException($"Question header at index {questionIndex} is required.");
            }

            if (string.IsNullOrWhiteSpace(question.Question))
            {
                throw new InvalidOperationException($"Question text at index {questionIndex} is required.");
            }

            if (!seenQuestions.Add(question.Question))
            {
                throw new InvalidOperationException($"Duplicate question: {question.Question}");
            }

            if (question.Options.Count < 2 || question.Options.Count > 4)
            {
                throw new InvalidOperationException($"Question '{question.Question}' must contain between 2 and 4 options.");
            }

            for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
            {
                var option = question.Options[optionIndex];
                if (option is null)
                {
                    throw new InvalidOperationException($"Option at index {optionIndex} for question '{question.Question}' is missing.");
                }

                if (string.IsNullOrWhiteSpace(option.Label))
                {
                    throw new InvalidOperationException($"Option label at index {optionIndex} for question '{question.Question}' is required.");
                }

                if (string.IsNullOrWhiteSpace(option.Description))
                {
                    throw new InvalidOperationException($"Option description at index {optionIndex} for question '{question.Question}' is required.");
                }
            }
        }
    }

    public static void ValidateAnswers(AskUserRequest request, IReadOnlyDictionary<string, string> answers)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (answers == null)
        {
            throw new ArgumentNullException(nameof(answers));
        }
        ValidateRequest(request);

        var questionMap = new Dictionary<string, AskUserQuestion>(StringComparer.Ordinal);
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var question = request.Questions[index];
            questionMap[question.Question] = question;
        }

        foreach (var key in answers.Keys)
        {
            if (!questionMap.ContainsKey(key))
            {
                throw new InvalidOperationException($"Unknown question answer key: {key}");
            }
        }

        foreach (var question in request.Questions)
        {
            if (!answers.TryGetValue(question.Question, out var rawAnswer))
            {
                throw new InvalidOperationException($"Missing answer for question: {question.Question}");
            }

            var parts = SplitAnswer(rawAnswer);
            if (parts.Count == 0)
            {
                throw new InvalidOperationException($"Empty answer for question: {question.Question}");
            }

            if (!question.MultiSelect && parts.Count != 1)
            {
                throw new InvalidOperationException($"Question '{question.Question}' only allows a single answer.");
            }

            var allowedLabels = new HashSet<string>(StringComparer.Ordinal);
            for (var optionIndex = 0; optionIndex < question.Options.Count; optionIndex++)
            {
                allowedLabels.Add(question.Options[optionIndex].Label);
            }

            var seenAnswers = new HashSet<string>(StringComparer.Ordinal);
            for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                var answer = parts[partIndex];
                if (!allowedLabels.Contains(answer))
                {
                    throw new InvalidOperationException($"Invalid answer '{answer}' for question: {question.Question}");
                }

                if (!seenAnswers.Add(answer))
                {
                    throw new InvalidOperationException($"Duplicate answer '{answer}' for question: {question.Question}");
                }
            }
        }
    }

    public static string BuildPrompt(IReadOnlyList<AskUserQuestion> questions)
    {
        if (questions is null || questions.Count == 0)
        {
            return "The agent needs additional input.";
        }

        var first = questions[0];
        var header = first.Header?.Trim();
        var question = first.Question?.Trim();

        if (!string.IsNullOrWhiteSpace(header) && !string.IsNullOrWhiteSpace(question))
        {
            return $"{header}: {question}";
        }

        if (!string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        if (!string.IsNullOrWhiteSpace(question))
        {
            return question;
        }

        return "The agent needs additional input.";
    }

    private static List<string> SplitAnswer(string? rawAnswer)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(rawAnswer))
        {
            return parts;
        }

        var slices = rawAnswer.Split(',');
        for (var index = 0; index < slices.Length; index++)
        {
            var value = slices[index].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }

        return parts;
    }
}

public sealed class AskUserRequestEventArgs : EventArgs
{
    public object MessageId { get; }

    public AskUserRequest Request { get; }

    public string SessionId => Request.SessionId;

    public Func<IReadOnlyDictionary<string, string>, Task<bool>> Respond { get; }

    public AskUserRequestEventArgs(
        object messageId,
        AskUserRequest request,
        Func<IReadOnlyDictionary<string, string>, Task<bool>> respond)
    {
        MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Respond = respond ?? throw new ArgumentNullException(nameof(respond));
    }
}
