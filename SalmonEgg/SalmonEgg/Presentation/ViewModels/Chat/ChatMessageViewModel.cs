using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Tool;
using ToolCallKindType = SalmonEgg.Domain.Models.Tool.ToolCallKind;
using ToolCallStatusType = SalmonEgg.Domain.Models.Tool.ToolCallStatus;

namespace SalmonEgg.Presentation.ViewModels.Chat
{
    /// <summary>
    /// Chat 消息 ViewModel，用于在 UI 中显示各种类型的内容
    /// </summary>
    public partial class ChatMessageViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private bool _isOutgoing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsThinkingPlaceholder))]
        private string _contentType = string.Empty;

        [ObservableProperty]
        private string _title = string.Empty;

        // 文本内容
        [ObservableProperty]
        private string _textContent = string.Empty;

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
        private string? _toolCallId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolCallKindDisplayName))]
        private ToolCallKind? _toolCallKind;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ToolCallStatusDisplayName))]
        private ToolCallStatus? _toolCallStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasToolCallJson))]
        private string? _toolCallJson;

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
            
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = isOutgoing,
                ContentType = "tool_call",
                Title = title ?? string.Empty,
                ToolCallId = toolCallId,
                ToolCallKind = kind,
                ToolCallStatus = status,
                ToolCallJson = toolCallJson,
                Timestamp = DateTime.Now
            };
        }

        public static ChatMessageViewModel CreateThinkingPlaceholder(string id)
        {
            return new ChatMessageViewModel
            {
                Id = id,
                IsOutgoing = false,
                ContentType = "thinking",
                Title = "思考中",
                TextContent = string.Empty,
                Timestamp = DateTime.Now
            };
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
       public bool HasImageContent => !string.IsNullOrEmpty(ImageData);
       public bool HasAudioContent => !string.IsNullOrEmpty(AudioData);
       public bool HasToolCall => !string.IsNullOrEmpty(ToolCallId);
       public bool HasPlanEntry => PlanEntry != null;
       public bool HasModeChange => !string.IsNullOrEmpty(ModeId);
       public bool HasResourceContent => ResourceViewModel?.IsResourceContent == true;
       public bool HasResourceLink => ResourceViewModel?.IsResourceLink == true;

       public bool IsThinkingPlaceholder => string.Equals(ContentType, "thinking", StringComparison.Ordinal);

       public string ToolCallStatusDisplayName => ToolCallStatus switch
       {
           ToolCallStatusType.Pending => "待处理",
           ToolCallStatusType.InProgress => "进行中",
           ToolCallStatusType.Completed => "已完成",
           ToolCallStatusType.Failed => "失败",
           _ => "未知"
       };

       public string ToolCallKindDisplayName => ToolCallKind switch
       {
           ToolCallKindType.Read => "读取",
           ToolCallKindType.Edit => "编辑",
           ToolCallKindType.Delete => "删除",
           ToolCallKindType.Move => "移动",
           ToolCallKindType.Search => "搜索",
           ToolCallKindType.Execute => "执行",
           ToolCallKindType.Think => "思考",
           _ => "工具"
       };

       public bool HasToolCallJson => !string.IsNullOrWhiteSpace(ToolCallJson);
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
            Domain.Models.Plan.PlanEntryStatus.InProgress => "处理中",
            Domain.Models.Plan.PlanEntryStatus.Completed => "完成",
            Domain.Models.Plan.PlanEntryStatus.Failed => "失败",
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
