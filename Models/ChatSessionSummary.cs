namespace wish_drom.Models
{
    /// <summary>
    /// 聊天会话摘要模型
    /// </summary>
    public class ChatSessionSummary
    {
        /// <summary>
        /// 会话ID
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 会话标题（基于第一条用户消息）
        /// </summary>
        public string Title { get; set; } = "新对话";

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 消息数量
        /// </summary>
        public int MessageCount { get; set; }
    }
}
