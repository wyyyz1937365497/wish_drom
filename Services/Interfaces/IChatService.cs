using wish_drom.Models;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 聊天服务接口
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// 发送消息并获取流式响应
        /// </summary>
        /// <param name="message">用户消息</param>
        /// <param name="sessionId">会话ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>流式响应</returns>
        IAsyncEnumerable<string> SendMessageAsync(
            string message,
            string sessionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始新会话
        /// </summary>
        /// <returns>新会话ID</returns>
        string StartNewSession();

        /// <summary>
        /// 获取会话历史
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>历史消息列表</returns>
        Task<List<(string Role, string Content)>> GetSessionHistoryAsync(
            string sessionId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查服务是否已配置 (API Key等)
        /// </summary>
        Task<bool> IsConfiguredAsync();

        /// <summary>
        /// 配置 API 连接
        /// </summary>
        /// <param name="baseUrl">API Base URL (如 https://api.openai.com)</param>
        /// <param name="apiKey">OpenAI API Key</param>
        /// <param name="modelId">模型ID (如 gpt-4o-mini)</param>
        Task ConfigureAsync(string baseUrl, string apiKey, string modelId);

        /// <summary>
        /// 测试 API 连接
        /// </summary>
        /// <param name="baseUrl">API Base URL</param>
        /// <param name="apiKey">API Key</param>
        /// <param name="modelId">模型ID</param>
        Task TestConnectionAsync(string baseUrl, string apiKey, string modelId);

        /// <summary>
        /// 读取当前配置
        /// </summary>
        Task<(string? BaseUrl, string? ApiKey, string? ModelId)> GetConfigAsync();

        /// <summary>
        /// 清除 API 配置
        /// </summary>
        Task ClearConfigAsync();

        /// <summary>
        /// 清除当前会话上下文
        /// </summary>
        Task ClearSessionAsync(string sessionId);

        /// <summary>
        /// 获取所有会话列表（返回会话摘要信息）
        /// </summary>
        Task<List<ChatSessionSummary>> GetAllSessionsAsync();

        /// <summary>
        /// 删除会话
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        Task DeleteSessionAsync(string sessionId);

        /// <summary>
        /// 创建会话标题（基于第一条用户消息的前30个字符）
        /// </summary>
        /// <param name="firstUserMessage">第一条用户消息</param>
        string GenerateSessionTitle(string firstUserMessage);
    }
}
