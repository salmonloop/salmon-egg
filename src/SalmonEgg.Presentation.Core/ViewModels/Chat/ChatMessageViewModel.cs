using System;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Acp.Content;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat
{
    /// <summary>
    /// Chat 消息 ViewModel，用于在 UI 中显示各种类型的内容
    /// </summary>
    public partial class ChatMessageViewModel : ObservableObject, IRenderFailureSink
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _projectionItemKey = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasTimestamp))]
        private DateTime? _timestamp;

        [ObservableProperty]
        private bool _isOutgoing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string _contentType = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string _title = string.Empty;

        // 文本内容
        [ObservableProperty]
        private string _textContent = string.Empty;

        [ObservableProperty]
        private bool _isMarkdownFallbackSticky;

        /// <summary>
        /// Authoritative ACP protocol message id when the application Id is absent or secondary.
        /// Carried on the VM so patch matching and restore keys share one fact owner.
        /// </summary>
        [ObservableProperty]
        private string? _protocolMessageId;

        // 图片内容
        [ObservableProperty]
        private string _imageData = string.Empty;

        [ObservableProperty]
        private string _imageMimeType = string.Empty;

        // 音频内容
        [ObservableProperty]
        private string _audioData = string.Empty;

        [ObservableProperty]
        private string _audioMimeType = string.Empty;

        // 工具调用
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string? _toolCallId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private SalmonEgg.Acp.Tool.ToolCallKind? _toolCallKind;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private SalmonEgg.Acp.Tool.ToolCallStatus? _toolCallStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallJson))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string? _toolCallJson;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallRawInput))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string? _toolCallRawInputJson;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallRawOutput))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private string? _toolCallRawOutputJson;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallDetails))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private IReadOnlyList<ToolCallDetailItem> _toolCallDetailItems = Array.Empty<ToolCallDetailItem>();

        [ObservableProperty]
        private string? _toolCallSummary;

        [ObservableProperty]
        private IReadOnlyList<ToolCallContent>? _toolCallContent;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallLocations))]
        private IReadOnlyList<ToolCallLocation>? _toolCallLocations;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPendingPermissionRequest))]
        private PermissionRequestViewModel? _pendingPermissionRequest;

        [ObservableProperty]
        private bool _isToolCallInProgress;

        [ObservableProperty]
        private bool _isToolCallCompleted;

        [ObservableProperty]
        private bool _isToolCallFailed;

        private bool _isToolCallCancelled;

        private bool _applyingSnapshot;

        // 计划条目
        [ObservableProperty]
        private PlanEntryViewModel? _planEntry;

        // 模式切换
        [ObservableProperty]
        private string? _modeId;

        // 资源内容
        [ObservableProperty]
        private ResourceViewModel? _resourceViewModel;

        private ChatMarkdownPresentationState _markdownPresentation = ChatMarkdownPresentationState.PlainStreaming;
        private Func<string, Task<bool>>? _copyTextAsync;
        private Func<Uri, Task<bool>>? _openUriAsync;
        private Func<ChatMessageViewModel, Task>? _reportContentAsync;

        public ChatMessageViewModel()
        {
            CopyTextCommand = new AsyncRelayCommand<string?>(CopyTextAsync, CanCopyText);
            OpenMarkdownLinkCommand = new AsyncRelayCommand<string?>(OpenMarkdownLinkAsync, CanOpenMarkdownLink);
            ReportContentCommand = new AsyncRelayCommand(ReportContentAsync, CanReportContent);
            RefreshMarkdownPresentation();
        }

        public IAsyncRelayCommand<string?> CopyTextCommand { get; }

        public IAsyncRelayCommand<string?> OpenMarkdownLinkCommand { get; }

        public IAsyncRelayCommand ReportContentCommand { get; }

        public static ChatMessageViewModel CreateFromTextContent(string id, ContentBlock content, bool isOutgoing = false)
        {
            var viewModel = new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "text"
            };

            if (content is TextContentBlock textContent)
            {
                viewModel.TextContent = textContent.Text ?? string.Empty;
            }

            return viewModel;
        }

        public static ChatMessageViewModel CreateFromImageContent(string id, ContentBlock content, bool isOutgoing = false)
        {
            var viewModel = new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "image"
            };

            if (content is ImageContentBlock imageContent)
            {
                viewModel.ImageData = imageContent.Data ?? string.Empty;
                viewModel.ImageMimeType = imageContent.MimeType ?? "image/png";
            }

            return viewModel;
        }

        public static ChatMessageViewModel CreateFromAudioContent(string id, ContentBlock content, bool isOutgoing = false)
        {
            var viewModel = new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "audio"
            };

            if (content is AudioContentBlock audioContent)
            {
                viewModel.AudioData = audioContent.Data ?? string.Empty;
                viewModel.AudioMimeType = audioContent.MimeType ?? "audio/mp3";
            }

            return viewModel;
        }

        public static ChatMessageViewModel CreateFromToolCall(string id, string? toolCallId, string? rawInput, string? rawOutput, ToolCallKind? kind, ToolCallStatus? status, string? title, bool isOutgoing = false)
        {
            var toolCallJson = !string.IsNullOrEmpty(rawInput) ? rawInput : (!string.IsNullOrEmpty(rawOutput) ? rawOutput : string.Empty);

            var viewModel = new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "tool_call",
                Title = title ?? string.Empty,
                ToolCallId = toolCallId,
                ToolCallKind = kind,
                ToolCallStatus = status,
                ToolCallJson = toolCallJson,
                ToolCallRawInputJson = rawInput,
                ToolCallRawOutputJson = rawOutput
            };

            viewModel.RefreshToolCallDetails();
            viewModel.UpdateToolCallState();
            return viewModel;
        }

        public static ChatMessageViewModel CreateFromPlanEntry(string id, PlanEntry entry, bool isOutgoing = false)
        {
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "plan_entry",
                Title = entry.Content ?? string.Empty,
                PlanEntry = new PlanEntryViewModel
                {
                    Content = entry.Content ?? string.Empty,
                    Status = entry.Status,
                    Priority = entry.Priority
                }
            };
        }

        public static ChatMessageViewModel CreateFromModeChange(string id, string? modeId, string? title, bool isOutgoing = false)
        {
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "mode_change",
                ModeId = modeId,
                Title = title ?? "Mode Changed"
            };
        }

        public static ChatMessageViewModel CreateFromResourceContent(string id, ResourceContentBlock block, bool isOutgoing = false)
        {
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "resource_content",
                Title = "Resource Content",
                ResourceViewModel = ResourceViewModel.CreateFromContent(block)
            };
        }

        public static ChatMessageViewModel CreateFromResourceLink(string id, ResourceLinkContentBlock block, bool isOutgoing = false)
        {
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "resource_link",
                Title = block.Title ?? block.Name ?? "Resource Link",
                ResourceViewModel = ResourceViewModel.CreateFromLink(block)
            };
        }


        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public bool HasTextContent => !string.IsNullOrEmpty(TextContent);

        /// <summary>
        /// Visible body text for directional message templates. Prefer protocol text; when
        /// a content type only carries a title (mode_change/plan_entry), surface that title.
        /// Image/audio without dedicated templates project a plain mime fallback so ListView
        /// rows never materialize as blank bubbles under Skia/WinUI (including old persisted
        /// snapshots that never wrote TextContent).
        /// </summary>
        public string DisplayBodyText
        {
            get
            {
                if (!string.IsNullOrEmpty(TextContent))
                {
                    return TextContent;
                }

                if (!string.IsNullOrEmpty(Title))
                {
                    return Title;
                }

                if (string.Equals(ContentType, "image", StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(ImageMimeType)
                        ? "[image]"
                        : $"[image: {ImageMimeType}]";
                }

                if (string.Equals(ContentType, "audio", StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(AudioMimeType)
                        ? "[audio]"
                        : $"[audio: {AudioMimeType}]";
                }

                return string.Empty;
            }
        }

        public bool HasDisplayBody => !string.IsNullOrEmpty(DisplayBodyText);
        public bool HasTimestamp => Timestamp.HasValue;
        public ChatMarkdownPresentationState MarkdownPresentation
        {
            get => _markdownPresentation;
            private set
            {
                if (SetProperty(ref _markdownPresentation, value))
                {
                    OnPropertyChanged(nameof(MarkdownRenderMode));
                    OnPropertyChanged(nameof(ShouldRenderMarkdown));
                    OnPropertyChanged(nameof(ShouldRenderPlainText));
                    OnPropertyChanged(nameof(CopyableMarkdownCodeBlockText));
                    OnPropertyChanged(nameof(HasCopyableMarkdownCodeBlock));
                }
            }
        }

        public ChatMarkdownRenderMode MarkdownRenderMode => MarkdownPresentation.RenderMode;
        public bool ShouldRenderMarkdown => MarkdownPresentation.ShouldRenderMarkdown;
        public bool ShouldRenderPlainText => MarkdownPresentation.ShouldRenderPlainText;
        public string CopyableMarkdownCodeBlockText => MarkdownPresentation.CopyableCodeBlockText;
        public bool HasCopyableMarkdownCodeBlock => MarkdownPresentation.HasCopyableCodeBlock;
        public bool HasImageContent => !string.IsNullOrEmpty(ImageData);
        public bool HasAudioContent => !string.IsNullOrEmpty(AudioData);
        public bool HasToolCall => !string.IsNullOrEmpty(ToolCallId);
        public bool HasPlanEntry => PlanEntry != null;
        public bool HasModeChange => !string.IsNullOrEmpty(ModeId);
        public bool HasResourceContent => ResourceViewModel?.IsResourceContent == true;
        public bool HasResourceLink => ResourceViewModel?.IsResourceLink == true;
        public bool IsToolCallCancelled
        {
            get => _isToolCallCancelled;
            set => SetProperty(ref _isToolCallCancelled, value);
        }
        public bool HasToolCallLocations => ToolCallLocations?.Count > 0;
        public bool HasPendingPermissionRequest => PendingPermissionRequest != null;
        public bool ShouldShowToolCallPill =>
            string.Equals(ContentType, "tool_call", StringComparison.Ordinal)
            && (HasToolCall
                || HasToolCallJson
                || ToolCallKind is not null
                || ToolCallStatus is not null
                || HasToolCallDetails
                || HasTitle);


        public bool HasToolCallJson => !string.IsNullOrWhiteSpace(ToolCallJson);
        public bool HasToolCallRawInput => !string.IsNullOrWhiteSpace(ToolCallRawInputJson);
        public bool HasToolCallRawOutput => !string.IsNullOrWhiteSpace(ToolCallRawOutputJson);
        public bool HasToolCallDetails => ToolCallDetailItems.Count > 0;

        public void ConfigureShellActions(
             Func<string, Task<bool>> copyTextAsync,
             Func<Uri, Task<bool>> openUriAsync,
             Func<ChatMessageViewModel, Task>? reportContentAsync = null)
        {
            _copyTextAsync = copyTextAsync ?? throw new ArgumentNullException(nameof(copyTextAsync));
            _openUriAsync = openUriAsync ?? throw new ArgumentNullException(nameof(openUriAsync));
            _reportContentAsync = reportContentAsync;
            CopyTextCommand.NotifyCanExecuteChanged();
            OpenMarkdownLinkCommand.NotifyCanExecuteChanged();
            ReportContentCommand.NotifyCanExecuteChanged();
        }

        public void MarkMarkdownRenderFailed()
        {
            IsMarkdownFallbackSticky = true;
            RefreshMarkdownPresentation();
        }

        public void MarkRenderFailed() => MarkMarkdownRenderFailed();

        public static bool HasSameTemplateShape(ChatMessageViewModel vm, ConversationMessageSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(vm);
            ArgumentNullException.ThrowIfNull(snapshot);

            return vm.IsOutgoing == snapshot.IsOutgoing
                && string.Equals(vm.ContentType ?? string.Empty, snapshot.ContentType ?? string.Empty, StringComparison.Ordinal);
        }

        public void ApplySnapshot(ConversationMessageSnapshot snapshot, int projectionIndex)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            _applyingSnapshot = true;
            try
            {
                // Sticky markdown failure is a UI attempt state, not protocol fact. Authoritative
                // rehydrate always starts a fresh presentation attempt for the snapshot body.
                IsMarkdownFallbackSticky = false;

                Id = string.IsNullOrWhiteSpace(snapshot.Id)
                    ? string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString() : Id
                    : snapshot.Id;
                ProjectionItemKey = TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(snapshot, projectionIndex);
                // The snapshot owns the authoritative time (UTC), or none. We localize it for
                // display only when present; absent time stays null and the UI hides the clock.
                Timestamp = ConversationMessageTimestamp.ToDisplayLocal(snapshot.Timestamp);
                IsOutgoing = snapshot.IsOutgoing;
                ContentType = snapshot.ContentType ?? string.Empty;
                Title = snapshot.Title ?? string.Empty;
                TextContent = snapshot.TextContent ?? string.Empty;
                ProtocolMessageId = snapshot.ProtocolMessageId;
                ImageData = snapshot.ImageData ?? string.Empty;
                ImageMimeType = snapshot.ImageMimeType ?? string.Empty;
                AudioData = snapshot.AudioData ?? string.Empty;
                AudioMimeType = snapshot.AudioMimeType ?? string.Empty;
                ToolCallId = snapshot.ToolCallId;
                ToolCallKind = ToolCallContentSnapshots.ParseKind(snapshot.ToolCallKind);
                ToolCallStatus = ToolCallContentSnapshots.ParseStatus(snapshot.ToolCallStatus);
                ToolCallJson = snapshot.ToolCallJson;
                ToolCallRawInputJson = snapshot.ToolCallRawInputJson;
                ToolCallRawOutputJson = snapshot.ToolCallRawOutputJson;
                ToolCallContent = ToolCallContentSnapshots.FromDomainContent(snapshot.ToolCallContent);
                ToolCallLocations = ToolCallContentSnapshots.FromDomainLocations(snapshot.ToolCallLocations);
                ModeId = snapshot.ModeId;

                if (snapshot.PlanEntry is not null)
                {
                    if (PlanEntry is null)
                    {
                        PlanEntry = new PlanEntryViewModel
                        {
                            Content = snapshot.PlanEntry.Content ?? string.Empty,
                            Status = ConversationPlanWire.ParseStatus(snapshot.PlanEntry.Status),
                            Priority = ConversationPlanWire.ParsePriority(snapshot.PlanEntry.Priority)
                        };
                    }
                    else
                    {
                        PlanEntry.Content = snapshot.PlanEntry.Content ?? string.Empty;
                        PlanEntry.Status = ConversationPlanWire.ParseStatus(snapshot.PlanEntry.Status);
                        PlanEntry.Priority = ConversationPlanWire.ParsePriority(snapshot.PlanEntry.Priority);
                    }
                }
                else
                {
                    PlanEntry = null;
                }
            }
            finally
            {
                _applyingSnapshot = false;
                OnPropertyChanged(nameof(HasTextContent));
                NotifyDisplayBodyChanged();
                CopyTextCommand.NotifyCanExecuteChanged();
                ReportContentCommand.NotifyCanExecuteChanged();
                RefreshMarkdownPresentation();
                RefreshToolCallDetails();
                UpdateToolCallState();
            }
        }

        private void NotifyDisplayBodyChanged()
        {
            OnPropertyChanged(nameof(DisplayBodyText));
            OnPropertyChanged(nameof(HasDisplayBody));
        }

        partial void OnIsOutgoingChanged(bool value)
        {
            ReportContentCommand.NotifyCanExecuteChanged();
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshMarkdownPresentation();
        }

        partial void OnContentTypeChanged(string value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            NotifyDisplayBodyChanged();
            RefreshMarkdownPresentation();
        }

        partial void OnTextContentChanged(string value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            OnPropertyChanged(nameof(HasTextContent));
            NotifyDisplayBodyChanged();
            CopyTextCommand.NotifyCanExecuteChanged();
            RefreshMarkdownPresentation();
        }

        partial void OnTitleChanged(string value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            NotifyDisplayBodyChanged();
        }

        partial void OnImageMimeTypeChanged(string value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            NotifyDisplayBodyChanged();
        }

        partial void OnAudioMimeTypeChanged(string value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            NotifyDisplayBodyChanged();
        }

        partial void OnIsMarkdownFallbackStickyChanged(bool value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshMarkdownPresentation();
        }

        private void RefreshMarkdownPresentation()
        {
            var renderMode = ChatMarkdownRenderPolicy.Resolve(
                ContentType,
                IsOutgoing,
                TextContent,
                IsMarkdownFallbackSticky);
            MarkdownPresentation = ChatMarkdownPresentationState.Create(renderMode, TextContent);
        }

        private bool CanCopyText(string? text)
             => _copyTextAsync is not null && !string.IsNullOrWhiteSpace(text);

        private async Task CopyTextAsync(string? text)
        {
            if (!CanCopyText(text))
            {
                return;
            }

            _ = await _copyTextAsync!(text!).ConfigureAwait(true);
        }

        private bool CanOpenMarkdownLink(string? rawLink)
             => _openUriAsync is not null
                && ChatMarkdownLinkPolicy.TryResolveLaunchUri(rawLink, out _);

        private async Task OpenMarkdownLinkAsync(string? rawLink)
        {
            if (!CanOpenMarkdownLink(rawLink))
            {
                return;
            }

            _ = ChatMarkdownLinkPolicy.TryResolveLaunchUri(rawLink, out var uri);
            _ = await _openUriAsync!(uri!).ConfigureAwait(true);
        }

        private bool CanReportContent()
            => !IsOutgoing && _reportContentAsync is not null;

        private async Task ReportContentAsync()
        {
            if (!CanReportContent())
            {
                return;
            }

            await _reportContentAsync!(this).ConfigureAwait(true);
        }

        private void UpdateToolCallState()
        {
            IsToolCallInProgress = ToolCallStatus == SalmonEgg.Acp.Tool.ToolCallStatus.InProgress || ToolCallStatus == SalmonEgg.Acp.Tool.ToolCallStatus.Pending;
            IsToolCallCompleted = ToolCallStatus == SalmonEgg.Acp.Tool.ToolCallStatus.Completed;
            IsToolCallFailed = ToolCallStatus == SalmonEgg.Acp.Tool.ToolCallStatus.Failed;
            IsToolCallCancelled = ToolCallStatus == SalmonEgg.Acp.Tool.ToolCallStatus.Cancelled;
        }

        private void RefreshToolCallDetails()
        {
            ToolCallDetailItems = ToolCallDetailProjector.Project(ToolCallContent, ToolCallLocations);
            ToolCallSummary = ToolCallDetailProjector.ProjectSummary(
                ToolCallKind, ToolCallRawInputJson, ToolCallContent, ToolCallLocations);
        }

        partial void OnToolCallRawInputJsonChanged(string? value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshToolCallDetails();
        }

        partial void OnToolCallRawOutputJsonChanged(string? value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshToolCallDetails();
        }

        partial void OnToolCallContentChanged(IReadOnlyList<ToolCallContent>? value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshToolCallDetails();
        }

        partial void OnToolCallLocationsChanged(IReadOnlyList<ToolCallLocation>? value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            RefreshToolCallDetails();
        }

        partial void OnToolCallStatusChanged(SalmonEgg.Acp.Tool.ToolCallStatus? value)
        {
            if (_applyingSnapshot)
            {
                return;
            }

            UpdateToolCallState();
        }
    }

    public sealed record ToolCallDetailItem(ToolCallDetailKind Kind)
    {
        public string? Text { get; init; }

        public string? Path { get; init; }

        public uint? Line { get; init; }

        public string? DiffOldText { get; init; }

        public string? DiffNewText { get; init; }

        public string? TerminalId { get; init; }

        public string DisplayText => Kind switch
        {
            ToolCallDetailKind.Location => Line is null ? (Path ?? string.Empty) : $"{Path}:{Line}",
            ToolCallDetailKind.Terminal => TerminalId ?? string.Empty,
            ToolCallDetailKind.Diff => Path ?? string.Empty,
            _ => Text ?? string.Empty
        };

        public bool HasPath => !string.IsNullOrWhiteSpace(Path);

        public bool HasDiffNewText => !string.IsNullOrWhiteSpace(DiffNewText);
    }

    public enum ToolCallDetailKind
    {
        Text,
        Diff,
        Terminal,
        Location
    }

    internal static class ToolCallDetailProjector
    {
        private const int SummaryMaxLength = 200;

        public static IReadOnlyList<ToolCallDetailItem> Project(
            IReadOnlyList<ToolCallContent>? content,
            IReadOnlyList<ToolCallLocation>? locations)
        {
            var items = new List<ToolCallDetailItem>();
            AppendContent(items, content);
            AppendLocations(items, locations);
            return items;
        }

        public static string ProjectSummary(
            ToolCallKind? kind,
            string? rawInputJson,
            IReadOnlyList<ToolCallContent>? content,
            IReadOnlyList<ToolCallLocation>? locations)
        {
            var fromInput = SummarizeInput(kind, rawInputJson);
            if (!string.IsNullOrWhiteSpace(fromInput))
            {
                return Cap(fromInput);
            }

            if (content is not null)
            {
                foreach (var item in content)
                {
                    if (item is DiffToolCallContent diff && !string.IsNullOrWhiteSpace(diff.Path))
                    {
                        return Cap(diff.Path);
                    }
                }
            }

            if (locations is not null)
            {
                foreach (var location in locations)
                {
                    if (!string.IsNullOrWhiteSpace(location.Path))
                    {
                        return Cap(location.Line is null ? location.Path : $"{location.Path}:{location.Line}");
                    }
                }
            }

            return string.Empty;
        }

        private static string SummarizeInput(ToolCallKind? kind, string? rawInputJson)
        {
            if (string.IsNullOrWhiteSpace(rawInputJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(rawInputJson);
                var root = document.RootElement;
                return root.ValueKind switch
                {
                    JsonValueKind.Object => SummarizeInputObject(kind, root),
                    JsonValueKind.Array => SummarizeInputArray(root),
                    _ => string.Empty
                };
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string SummarizeInputObject(ToolCallKind? kind, JsonElement root)
        {
            if (kind == ToolCallKind.Search)
            {
                return FirstNonEmpty(
                    TryGetString(root, "query", "Query"),
                    TryGetString(root, "path", "Path", "SearchPath", "searchPath"));
            }

            if (kind == ToolCallKind.Execute)
            {
                return BuildCommand(
                    TryGetString(root, "CommandLine", "commandLine", "command", "Command", "cmd"),
                    TryGetString(root, "Arguments", "arguments", "Args", "args"));
            }

            if (kind == ToolCallKind.Fetch)
            {
                return FirstNonEmpty(
                    TryGetString(root, "query", "Query", "url", "Url", "uri", "Uri"));
            }

            return FirstNonEmpty(
                TryGetString(root, "path", "Path", "SearchPath", "searchPath", "TargetFile", "targetFile"),
                TryGetString(root, "query", "Query"),
                BuildCommand(
                    TryGetString(root, "CommandLine", "commandLine", "command", "Command", "cmd"),
                    TryGetString(root, "Arguments", "arguments", "Args", "args")));
        }

        private static string SummarizeInputArray(JsonElement root)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryGetString(item, "type") is "diff")
                {
                    var path = TryGetString(item, "path");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
            }

            return string.Empty;
        }

        private static string BuildCommand(string? command, string? arguments)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return arguments ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments}";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value!;
                }
            }

            return string.Empty;
        }

        private static string Cap(string value)
            => value.Length > SummaryMaxLength
                ? string.Concat(value.AsSpan(0, SummaryMaxLength - 1), "…")
                : value;

        private static string? TryGetString(JsonElement root, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (root.TryGetProperty(propertyName, out var property))
                {
                    return property.ValueKind == JsonValueKind.String
                        ? property.GetString()
                        : property.GetRawText();
                }
            }

            return null;
        }

        private static void AppendContent(List<ToolCallDetailItem> items, IReadOnlyList<ToolCallContent>? content)
        {
            if (content is null)
            {
                return;
            }

            foreach (var item in content)
            {
                switch (item)
                {
                    case ContentToolCallContent { Content: TextContentBlock textBlock } when !string.IsNullOrWhiteSpace(textBlock.Text):
                        items.Add(new ToolCallDetailItem(ToolCallDetailKind.Text) { Text = textBlock.Text.Trim() });
                        break;
                    case ContentToolCallContent { Content: ResourceLinkContentBlock resourceLink } when !string.IsNullOrWhiteSpace(resourceLink.Uri):
                        items.Add(new ToolCallDetailItem(ToolCallDetailKind.Location) { Path = resourceLink.Uri });
                        break;
                    case DiffToolCallContent diff:
                        items.Add(new ToolCallDetailItem(ToolCallDetailKind.Diff)
                        {
                            Path = diff.Path,
                            DiffOldText = diff.OldText,
                            DiffNewText = diff.NewText
                        });
                        break;
                    case TerminalToolCallContent terminal when !string.IsNullOrWhiteSpace(terminal.TerminalId):
                        items.Add(new ToolCallDetailItem(ToolCallDetailKind.Terminal) { TerminalId = terminal.TerminalId });
                        break;
                }
            }
        }

        private static void AppendLocations(List<ToolCallDetailItem> items, IReadOnlyList<ToolCallLocation>? locations)
        {
            if (locations is null)
            {
                return;
            }

            foreach (var location in locations)
            {
                if (string.IsNullOrWhiteSpace(location.Path))
                {
                    continue;
                }

                items.Add(new ToolCallDetailItem(ToolCallDetailKind.Location) { Path = location.Path, Line = location.Line });
            }
        }
    }

    /// <summary>
    /// 计划条目 ViewModel
    /// </summary>
    public partial class PlanEntryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private SalmonEgg.Acp.Plan.PlanEntryStatus _status = SalmonEgg.Acp.Plan.PlanEntryStatus.Pending;

        [ObservableProperty]
        private SalmonEgg.Acp.Plan.PlanEntryPriority _priority = SalmonEgg.Acp.Plan.PlanEntryPriority.Low;
    }
}
