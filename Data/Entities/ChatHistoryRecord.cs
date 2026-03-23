namespace wish_drom.Data.Entities
{
    /// <summary>
    /// 聊天记录实体
    /// </summary>
    public class ChatHistoryRecord
    {
        public int Id { get; set; }

        /// <summary>
        /// 会话ID (用于分组对话)
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 角色 (user/assistant/system)
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// 消息内容
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 消息时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Token使用量 (可选)
        /// </summary>
        public int? TokenCount { get; set; }

        /// <summary>
        /// 关联的插件调用 (如有)
        /// </summary>
        public string? PluginName { get; set; }
    }
}
