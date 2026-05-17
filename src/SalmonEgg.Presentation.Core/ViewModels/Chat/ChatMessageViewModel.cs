using System;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Tool;

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
        private DateTime _timestamp;

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
        [NotifyPropertyChangedFor(nameof(ToolCallKindDisplayName))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private Domain.Models.Tool.ToolCallKind? _toolCallKind;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolCallStatusDisplayName))]
        [NotifyPropertyChangedFor(nameof(ShouldShowToolCallPill))]
        private Domain.Models.Tool.ToolCallStatus? _toolCallStatus;

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


        // 计划条目
       [ObservableProperty]
       private PlanEntryViewModel? _planEntry;

       // 模式切换
       [ObservableProperty]
       private string? _modeId;

       // 资源内容
       [ObservableProperty]
       private ResourceViewModel? _resourceViewModel;

        public ChatMessageViewModel()
        {
            Timestamp = DateTime.Now;
            RefreshMarkdownPresentation();
        }

        public static ChatMessageViewModel CreateFromTextContent(string id, ContentBlock content, bool isOutgoing = false)
        {
            var viewModel = new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "text",
                Timestamp = DateTime.Now
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
                ContentType = "image",
                Timestamp = DateTime.Now
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
                ContentType = "audio",
                Timestamp = DateTime.Now
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
                ToolCallRawOutputJson = rawOutput,
                Timestamp = DateTime.Now
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
                Timestamp = DateTime.Now,
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
               Title = title ?? "Mode Changed",
               Timestamp = DateTime.Now
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
              Timestamp = DateTime.Now,
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
               Timestamp = DateTime.Now,
               ResourceViewModel = ResourceViewModel.CreateFromLink(block)
           };
       }


        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public bool HasTextContent => !string.IsNullOrEmpty(TextContent);
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


       public string ToolCallStatusDisplayName => ToolCallStatus switch
       {
           Domain.Models.Tool.ToolCallStatus.Pending => "待处理",
           Domain.Models.Tool.ToolCallStatus.InProgress => "进行中",
           Domain.Models.Tool.ToolCallStatus.Completed => "已完成",
           Domain.Models.Tool.ToolCallStatus.Failed => "失败",
           Domain.Models.Tool.ToolCallStatus.Cancelled => "已取消",
           _ => "未知"
       };

       public string ToolCallKindDisplayName => ToolCallKind switch
       {
           Domain.Models.Tool.ToolCallKind.Read => "读取",
           Domain.Models.Tool.ToolCallKind.Edit => "编辑",
           Domain.Models.Tool.ToolCallKind.Delete => "删除",
           Domain.Models.Tool.ToolCallKind.Move => "移动",
           Domain.Models.Tool.ToolCallKind.Search => "搜索",
           Domain.Models.Tool.ToolCallKind.Execute => "执行",
           Domain.Models.Tool.ToolCallKind.SwitchMode => "切换模式",
           Domain.Models.Tool.ToolCallKind.Think => "思考",
           _ => "工具"
       };

       public bool HasToolCallJson => !string.IsNullOrWhiteSpace(ToolCallJson);
       public bool HasToolCallRawInput => !string.IsNullOrWhiteSpace(ToolCallRawInputJson);
       public bool HasToolCallRawOutput => !string.IsNullOrWhiteSpace(ToolCallRawOutputJson);
       public bool HasToolCallDetails => ToolCallDetailItems.Count > 0;

       public void MarkMarkdownRenderFailed()
       {
            IsMarkdownFallbackSticky = true;
            RefreshMarkdownPresentation();
       }

       public void MarkRenderFailed() => MarkMarkdownRenderFailed();

       partial void OnIsOutgoingChanged(bool value) => RefreshMarkdownPresentation();

       partial void OnContentTypeChanged(string value) => RefreshMarkdownPresentation();

       partial void OnTextContentChanged(string value)
       {
            RefreshMarkdownPresentation();
       }

       partial void OnIsMarkdownFallbackStickyChanged(bool value) => RefreshMarkdownPresentation();

       private ChatMarkdownPresentationState _markdownPresentation = ChatMarkdownPresentationState.PlainStreaming;

       private void RefreshMarkdownPresentation()
       {
            var renderMode = ChatMarkdownRenderPolicy.Resolve(
                ContentType,
                IsOutgoing,
                TextContent,
                IsMarkdownFallbackSticky);
            MarkdownPresentation = ChatMarkdownPresentationState.Create(renderMode, TextContent);
       }

        private void UpdateToolCallState()
        {
            IsToolCallInProgress = ToolCallStatus is Domain.Models.Tool.ToolCallStatus.InProgress or Domain.Models.Tool.ToolCallStatus.Pending;
            IsToolCallCompleted = ToolCallStatus == Domain.Models.Tool.ToolCallStatus.Completed;
            IsToolCallFailed = ToolCallStatus == Domain.Models.Tool.ToolCallStatus.Failed;
            IsToolCallCancelled = ToolCallStatus == Domain.Models.Tool.ToolCallStatus.Cancelled;
        }

        private void RefreshToolCallDetails()
        {
            ToolCallDetailItems = ToolCallDetailProjector.Project(
                ToolCallRawInputJson,
                ToolCallRawOutputJson,
                ToolCallContent,
                ToolCallLocations,
                ToolCallJson);
        }

        partial void OnToolCallJsonChanged(string? value) => RefreshToolCallDetails();
        partial void OnToolCallRawInputJsonChanged(string? value) => RefreshToolCallDetails();
        partial void OnToolCallRawOutputJsonChanged(string? value) => RefreshToolCallDetails();
        partial void OnToolCallContentChanged(IReadOnlyList<ToolCallContent>? value) => RefreshToolCallDetails();
        partial void OnToolCallLocationsChanged(IReadOnlyList<ToolCallLocation>? value) => RefreshToolCallDetails();
        partial void OnToolCallStatusChanged(Domain.Models.Tool.ToolCallStatus? value) => UpdateToolCallState();
    }

    public sealed record ToolCallDetailItem(string? Label, string Value, ToolCallDetailKind Kind = ToolCallDetailKind.Text)
    {
        public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

        public string DisplayText => HasLabel ? $"{Label}: {Value}" : Value;
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
        public static IReadOnlyList<ToolCallDetailItem> Project(
            string? rawInputJson,
            string? rawOutputJson,
            IReadOnlyList<ToolCallContent>? content,
            IReadOnlyList<ToolCallLocation>? locations,
            string? legacyPayloadJson = null)
        {
            var items = new List<ToolCallDetailItem>();

            AppendJson(items, rawInputJson ?? legacyPayloadJson, prefix: null);
            AppendJson(items, rawOutputJson, prefix: "output");
            AppendContent(items, content);
            AppendLocations(items, locations);

            return items;
        }

        private static void AppendJson(List<ToolCallDetailItem> items, string? json, string? prefix)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                AppendJsonElement(items, document.RootElement, prefix);
            }
            catch (JsonException)
            {
                items.Add(new ToolCallDetailItem(prefix, json.Trim()));
            }
        }

        private static void AppendJsonElement(List<ToolCallDetailItem> items, JsonElement element, string? prefix)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var label = string.IsNullOrWhiteSpace(prefix)
                            ? property.Name
                            : $"{prefix}.{property.Name}";
                        AppendJsonElement(items, property.Value, label);
                    }

                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        AppendJsonElement(items, item, $"{prefix ?? "item"}[{index}]");
                        index++;
                    }

                    break;
                case JsonValueKind.String:
                    items.Add(new ToolCallDetailItem(prefix, element.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                default:
                    items.Add(new ToolCallDetailItem(prefix, element.GetRawText()));
                    break;
            }
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
                        items.Add(new ToolCallDetailItem(null, textBlock.Text.Trim()));
                        break;
                    case ContentToolCallContent { Content: ResourceLinkContentBlock resourceLink } when !string.IsNullOrWhiteSpace(resourceLink.Uri):
                        items.Add(new ToolCallDetailItem("resource", resourceLink.Uri, ToolCallDetailKind.Location));
                        break;
                    case DiffToolCallContent diff:
                        if (!string.IsNullOrWhiteSpace(diff.Path))
                        {
                            items.Add(new ToolCallDetailItem("path", diff.Path, ToolCallDetailKind.Diff));
                        }

                        if (!string.IsNullOrWhiteSpace(diff.OldText))
                        {
                            items.Add(new ToolCallDetailItem("oldText", diff.OldText, ToolCallDetailKind.Diff));
                        }

                        if (!string.IsNullOrWhiteSpace(diff.NewText))
                        {
                            items.Add(new ToolCallDetailItem("newText", diff.NewText, ToolCallDetailKind.Diff));
                        }

                        break;
                    case TerminalToolCallContent terminal when !string.IsNullOrWhiteSpace(terminal.TerminalId):
                        items.Add(new ToolCallDetailItem("terminalId", terminal.TerminalId, ToolCallDetailKind.Terminal));
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

                var value = location.Line is null ? location.Path : $"{location.Path}:{location.Line}";
                items.Add(new ToolCallDetailItem("location", value, ToolCallDetailKind.Location));
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
        private Domain.Models.Plan.PlanEntryStatus _status;

        [ObservableProperty]
        private Domain.Models.Plan.PlanEntryPriority _priority;

        public string StatusDisplayName => Status switch
        {
            Domain.Models.Plan.PlanEntryStatus.Pending => "待处理",
            Domain.Models.Plan.PlanEntryStatus.InProgress => "进行中",
            Domain.Models.Plan.PlanEntryStatus.Completed => "已完成",
            _ => "未知"
        };

        public string PriorityDisplayName => Priority switch
        {
            Domain.Models.Plan.PlanEntryPriority.High => "高",
            Domain.Models.Plan.PlanEntryPriority.Medium => "中",
            Domain.Models.Plan.PlanEntryPriority.Low => "低",
            _ => "未知"
        };
    }
}
