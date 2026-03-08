using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// 消息模板选择器，根据消息类型选择相应的模板
/// </summary>
public class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextMessageTemplate { get; set; }
    public DataTemplate? ImageMessageTemplate { get; set; }
    public DataTemplate? AudioMessageTemplate { get; set; }
    public DataTemplate? ToolCallTemplate { get; set; }
    public DataTemplate? PlanEntryTemplate { get; set; }
    public DataTemplate? ModeChangeTemplate { get; set; }
    public DataTemplate? ResourceContentTemplate { get; set; }
    public DataTemplate? ResourceLinkTemplate { get; set; }
    public DataTemplate? DefaultTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        if (item is not ChatMessageViewModel viewModel)
        {
            return DefaultTemplate ?? new DataTemplate();
        }

        return viewModel.ContentType switch
        {
            "text" => TextMessageTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "image" => ImageMessageTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "audio" => AudioMessageTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "tool_call" => ToolCallTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "plan_entry" => PlanEntryTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "mode_change" => ModeChangeTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "resource_content" => ResourceContentTemplate ?? DefaultTemplate ?? new DataTemplate(),
            "resource_link" => ResourceLinkTemplate ?? DefaultTemplate ?? new DataTemplate(),
            _ => DefaultTemplate ?? new DataTemplate()
        };
    }
}
