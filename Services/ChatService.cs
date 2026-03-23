using Microsoft.Maui.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace wish_drom.Services
{
    /// <summary>
    /// 聊天服务实现 - 基于 Semantic Kernel
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly AppDbContext _dbContext;
        private Kernel? _kernel;
        private IChatCompletionService? _chatService;
        private string? _apiKey;
        private string? _baseUrl;
        private string? _modelId;
        private readonly Dictionary<string, ChatHistory> _sessionHistory = new();

        public ChatService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<bool> IsConfiguredAsync()
        {
            _apiKey = await SecureStorage.Default.GetAsync("openai_api_key");
            _baseUrl = await SecureStorage.Default.GetAsync("openai_base_url");
            _modelId = await SecureStorage.Default.GetAsync("openai_model_id") ?? "gpt-4o-mini";
            return !string.IsNullOrEmpty(_apiKey);
        }

        public async Task ConfigureAsync(string baseUrl, string apiKey, string modelId)
        {
            await SecureStorage.Default.SetAsync("openai_base_url", baseUrl);
            await SecureStorage.Default.SetAsync("openai_api_key", apiKey);
            await SecureStorage.Default.SetAsync("openai_model_id", modelId);

            _baseUrl = baseUrl;
            _apiKey = apiKey;
            _modelId = modelId;

            InitializeKernel();
        }

        public async Task TestConnectionAsync(string baseUrl, string apiKey, string modelId)
        {
            // 简单测试：创建 Kernel 并发送一个测试请求
            var builder = Kernel.CreateBuilder();

            // 使用 HttpClient 设置自定义端点
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(NormalizeBaseUrl(baseUrl))
            };

            builder.AddOpenAIChatCompletion(
                modelId,
                apiKey,
                httpClient: httpClient
            );

            var kernel = builder.Build();
            var chatService = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddUserMessage("Hi");

            // 发送一个简单的测试请求
            await foreach (var _ in chatService.GetStreamingChatMessageContentsAsync(history, kernel: kernel))
            {
                break; // 只需要确认能收到响应即可
            }
        }

        private void InitializeKernel()
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_baseUrl))
                return;

            var builder = Kernel.CreateBuilder();

            // 使用 HttpClient 设置自定义端点
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(NormalizeBaseUrl(_baseUrl))
            };

            builder.AddOpenAIChatCompletion(
                _modelId ?? "gpt-4o-mini",
                _apiKey,
                httpClient: httpClient
            );

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        }

        private string NormalizeBaseUrl(string baseUrl)
        {
            // 移除末尾的斜杠
            baseUrl = baseUrl.TrimEnd('/');

            // 确保有 /v1 后缀（如果没有且看起来像 OpenAI 兼容端点）
            if (!baseUrl.EndsWith("/v1") && !baseUrl.Contains("/chat/completions"))
            {
                // 检查是否已经包含了路径
                if (!baseUrl.EndsWith("/openai/v1") && !baseUrl.Contains("/v1/"))
                {
                    baseUrl += "/v1";
                }
            }

            return baseUrl;
        }

        private string GetSystemPrompt()
        {
            return @"你是一个智能校园助手，主要职责是帮助学生管理课表和查询校园活动。

## 核心原则
1. **隐私优先**: 所有用户数据都存储在本地，不要编造任何信息
2. **准确回答**: 只根据提供的数据回答问题，如果数据不足，明确告知用户
3. **简洁友好**: 使用学生易懂的语言，避免冗长的回复

## 功能说明
- 课程管理: 帮助学生查询和管理课程安排
- 活动查询: 提供校园活动信息
- 学习建议: 提供学习计划和时间管理建议

## 回答格式
使用简洁友好的语言，适当使用emoji让回答更生动。

注意: 课表和活动查询功能需要用户先同步数据。";
        }

        public string StartNewSession()
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var history = new ChatHistory();
            history.AddSystemMessage(GetSystemPrompt());
            _sessionHistory[sessionId] = history;
            return sessionId;
        }

        public async IAsyncEnumerable<string> SendMessageAsync(
            string message,
            string sessionId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 确保已配置
            if (!await IsConfiguredAsync() || _kernel == null || _chatService == null)
            {
                yield return "请先在设置页面配置 API Key。";
                yield break;
            }

            // 初始化 session
            if (!_sessionHistory.ContainsKey(sessionId))
            {
                StartNewSession();
            }

            // 添加用户消息
            _sessionHistory[sessionId].AddUserMessage(message);

            // 保存到数据库
            await SaveMessageAsync(sessionId, "user", message);

            // 流式响应
            var fullResponse = new System.Text.StringBuilder();
            await foreach (var content in _chatService.GetStreamingChatMessageContentsAsync(
                _sessionHistory[sessionId],
                executionSettings: new OpenAIPromptExecutionSettings
                {
                    MaxTokens = 1000,
                    Temperature = 0.7f
                },
                kernel: _kernel,
                cancellationToken: cancellationToken))
            {
                if (content.Content != null)
                {
                    fullResponse.Append(content.Content);
                    yield return content.Content;
                }
            }

            // 保存助手回复
            var responseText = fullResponse.ToString();
            _sessionHistory[sessionId].AddAssistantMessage(responseText);
            await SaveMessageAsync(sessionId, "assistant", responseText);
        }

        public async Task<List<(string Role, string Content)>> GetSessionHistoryAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            var records = await _dbContext.ChatHistoryRecords
                .Where(r => r.SessionId == sessionId)
                .OrderBy(r => r.Timestamp)
                .ToListAsync<ChatHistoryRecord>(cancellationToken);

            return records.Select(r => (r.Role, r.Content)).ToList();
        }

        public async Task ClearSessionAsync(string sessionId)
        {
            if (_sessionHistory.ContainsKey(sessionId))
            {
                _sessionHistory.Remove(sessionId);
            }

            var records = await _dbContext.ChatHistoryRecords
                .Where(r => r.SessionId == sessionId)
                .ToListAsync<ChatHistoryRecord>();

            _dbContext.ChatHistoryRecords.RemoveRange(records);
            await _dbContext.SaveChangesAsync();
        }

        private async Task SaveMessageAsync(string sessionId, string role, string content)
        {
            var record = new ChatHistoryRecord
            {
                SessionId = sessionId,
                Role = role,
                Content = content,
                Timestamp = DateTime.Now
            };

            _dbContext.ChatHistoryRecords.Add(record);
            await _dbContext.SaveChangesAsync();
        }
    }
}
