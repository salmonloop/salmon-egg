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
    public DataTemplate? IncomingTemplate { get; set; }
    public DataTemplate? OutgoingTemplate { get; set; }
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

        return ResolveContentTemplate(viewModel.ContentType)
            ?? ResolveDirectionalTemplate(viewModel)
            ?? DefaultTemplate
            ?? new DataTemplate();
    }

    private DataTemplate? ResolveDirectionalTemplate(ChatMessageViewModel viewModel)
    {
        return viewModel.IsOutgoing
            ? OutgoingTemplate
            : IncomingTemplate;
    }

    private DataTemplate? ResolveContentTemplate(string? contentType)
    {
        return contentType switch
        {
            "text" => TextMessageTemplate,
            "image" => ImageMessageTemplate,
            "audio" => AudioMessageTemplate,
            "tool_call" => ToolCallTemplate,
            "plan_entry" => PlanEntryTemplate,
            "mode_change" => ModeChangeTemplate,
            "resource_content" => ResourceContentTemplate,
            "resource_link" => ResourceLinkTemplate,
            _ => null
        };
    }
}
