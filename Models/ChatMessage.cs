namespace wish_drom.Models
{
    /// <summary>
    /// 聊天消息模型
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; } = "user"; // user, assistant, system
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsStreaming { get; set; } = false;
    }
}
