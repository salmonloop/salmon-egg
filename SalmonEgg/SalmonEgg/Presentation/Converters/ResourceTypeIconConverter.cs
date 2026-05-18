using System;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

/// <summary>
/// 将资源的 MIME 类型转换为对应的颜色，用于 UI 中资源类型的视觉区分。
/// </summary>
public class ResourceTypeIconConverter : IValueConverter
{
    /// <summary>
    /// 将 MIME 类型转换为显示颜色
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string mimeType || string.IsNullOrEmpty(mimeType))
        {
            return ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush");
        }

        // 根据 MIME 类型返回不同的颜色
        return mimeType.ToLowerInvariant() switch
        {
            // 文本类型
            "text/plain" or
            "text/html" or
            "text/css" or
            "text/javascript" or
            "application/json" or
            "application/xml" => ThemeBrushConverter.Resolve("AccentBrush"),

            // 代码文件
            "application/x-sh" or
            "application/x-python" or
            "text/x-python" or
            "text/x-csharp" or
            "text/x-java" or
            "text/x-c++" or
            "text/x-c" => ThemeBrushConverter.Resolve("SystemFillColorSuccessBrush"),

            // 图片类型
            "image/png" or
            "image/jpeg" or
            "image/gif" or
            "image/svg+xml" or
            "image/webp" => ThemeBrushConverter.Resolve("SystemFillColorCautionBrush", "AccentBrush"),

            // 音频类型
            "audio/mpeg" or
            "audio/wav" or
            "audio/ogg" or
            "audio/mp4" => ThemeBrushConverter.Resolve("SystemFillColorCriticalBrush"),

            // 视频类型
            "video/mp4" or
            "video/webm" or
            "video/ogg" => ThemeBrushConverter.Resolve("SystemFillColorCriticalBrush"),

            // 文档类型
            "application/pdf" or
            "application/msword" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
            "application/zip" or
            "application/x-rar-compressed" => ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush"),

            // 默认颜色
            _ => ThemeBrushConverter.Resolve("TextFillColorSecondaryBrush")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
