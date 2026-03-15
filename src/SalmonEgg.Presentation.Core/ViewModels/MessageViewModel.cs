using System;

namespace SalmonEgg.Presentation.ViewModels
{
    /// <summary>
    /// 消息视图模型，用于在界面上显示消息历史
    /// </summary>
    public class MessageViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsOutgoing { get; set; }
        public string Label => IsOutgoing ? "Sent" : "Received";
    }
}
