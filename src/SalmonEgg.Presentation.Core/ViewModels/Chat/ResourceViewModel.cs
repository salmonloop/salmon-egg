using CommunityToolkit.Mvvm.ComponentModel;
using SalmonEgg.Domain.Models.Content;

namespace SalmonEgg.Presentation.ViewModels.Chat;

/// <summary>
/// 资源内容 ViewModel，用于在 UI 中展示资源内容块和资源链接。
/// 封装了 ResourceContentBlock 和 ResourceLinkContentBlock 的显示逻辑。
/// </summary>
public partial class ResourceViewModel : ObservableObject
{
    /// <summary>
    /// 资源的 URI
    /// </summary>
    [ObservableProperty]
    private string _uri = string.Empty;

    /// <summary>
    /// 资源名称（如果未提供，则使用 URI 显示）
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// MIME 类型
    /// </summary>
    [ObservableProperty]
    private string _mimeType = string.Empty;

    /// <summary>
    /// 嵌入的资源内容（仅适用于 ResourceContentBlock）
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// 资源链接的显示文本（仅适用于 ResourceLinkContentBlock）
    /// </summary>
    [ObservableProperty]
    private string _linkText = string.Empty;

    /// <summary>
    /// 资源标题
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// 资源描述
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// 资源大小（字节）
    /// </summary>
    [ObservableProperty]
    private long? _size;

    /// <summary>
    /// 是否为文本类型资源
    /// </summary>
    [ObservableProperty]
    private bool _isTextResource;

    /// <summary>
    /// 是否为二进制类型资源
    /// </summary>
    [ObservableProperty]
    private bool _isBinaryResource;

    /// <summary>
    /// 获取资源的显示标题
    /// </summary>
    public string DisplayTitle => !string.IsNullOrEmpty(Title) ? Title : (!string.IsNullOrEmpty(Name) ? Name : Uri);

    /// <summary>
    /// 判断是否为嵌入的资源内容
    /// </summary>
    public bool IsResourceContent => !string.IsNullOrEmpty(Content);

    /// <summary>
    /// 判断是否为资源链接
    /// </summary>
    public bool IsResourceLink => !string.IsNullOrEmpty(LinkText);

    /// <summary>
    /// 获取显示用的文本内容
    /// </summary>
    public string GetDisplayContent() => IsResourceContent ? Content : LinkText;

    /// <summary>
    /// 获取资源大小的显示文本
    /// </summary>
    public string SizeDisplay => Size.HasValue ? FormatSize(Size.Value) : string.Empty;

    /// <summary>
    /// 获取资源类型的图标
    /// </summary>
    public string TypeIcon => MimeType switch
    {
        var m when m.StartsWith("image/") => "🖼️",
        var m when m.StartsWith("video/") => "🎬",
        var m when m.StartsWith("audio/") => "🎵",
        var m when m.StartsWith("text/") => "📄",
        var m when m.Contains("json") => "📋",
        var m when m.Contains("pdf") => "📕",
        _ => "📎"
    };

    /// <summary>
    /// 格式化文件大小
    /// </summary>
    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {suffixes[i]}";
    }

   /// <summary>
   /// 从 ResourceContentBlock 创建 ResourceViewModel
   /// </summary>
   public static ResourceViewModel CreateFromContent(ResourceContentBlock block)
   {
       var resource = block.Resource;
       var isText = resource.IsText;
       var content = resource.Text ?? resource.Blob ?? string.Empty;

       return new ResourceViewModel
       {
           Uri = resource.Uri,
           Name = ExtractNameFromUri(resource.Uri),
           MimeType = resource.MimeType ?? "text/plain",
           Content = content,
           Title = "资源内容",
           Description = null,
           IsTextResource = isText,
           IsBinaryResource = resource.IsBinary,
           Size = content.Length
       };
   }

   /// <summary>
   /// 从 URI 中提取名称
   /// </summary>
   private static string ExtractNameFromUri(string uri)
   {
       if (string.IsNullOrEmpty(uri)) return "Unknown Resource";

       try
       {
           var lastSlash = uri.LastIndexOf('/');
           if (lastSlash >= 0 && lastSlash < uri.Length - 1)
           {
               return uri.Substring(lastSlash + 1);
           }
           return uri;
       }
       catch
       {
           return "Unknown Resource";
       }
   }

   /// <summary>
   /// 从 ResourceLinkContentBlock 创建 ResourceViewModel
   /// </summary>
   public static ResourceViewModel CreateFromLink(ResourceLinkContentBlock block)
   {
       return new ResourceViewModel
       {
           Uri = block.Uri,
           Name = block.Name ?? ExtractNameFromUri(block.Uri),
           MimeType = block.MimeType ?? string.Empty,
           LinkText = block.Name ?? block.Uri,
           Title = block.Title ?? "资源链接",
           Description = block.Description,
           Size = block.Size,
           IsTextResource = false,
           IsBinaryResource = false
       };
   }
}
