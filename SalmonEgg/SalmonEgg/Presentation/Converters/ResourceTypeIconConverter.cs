using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

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
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
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
            "application/xml" => new SolidColorBrush(Microsoft.UI.Colors.LightBlue),

            // 代码文件
            "application/x-sh" or
            "application/x-python" or
            "text/x-python" or
            "text/x-csharp" or
            "text/x-java" or
            "text/x-c++" or
            "text/x-c" => new SolidColorBrush(Microsoft.UI.Colors.LightGreen),

            // 图片类型
            "image/png" or
            "image/jpeg" or
            "image/gif" or
            "image/svg+xml" or
            "image/webp" => new SolidColorBrush(Microsoft.UI.Colors.LightYellow),

            // 音频类型
            "audio/mpeg" or
            "audio/wav" or
            "audio/ogg" or
            "audio/mp4" => new SolidColorBrush(Microsoft.UI.Colors.LightCoral),

            // 视频类型
            "video/mp4" or
            "video/webm" or
            "video/ogg" => new SolidColorBrush(Microsoft.UI.Colors.LightPink),

            // 文档类型
            "application/pdf" or
            "application/msword" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
            "application/zip" or
            "application/x-rar-compressed" => new SolidColorBrush(Microsoft.UI.Colors.LightSlateGray),

            // 默认颜色
            _ => new SolidColorBrush(Microsoft.UI.Colors.LightGray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
