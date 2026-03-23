using Microsoft.Maui.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;
using wish_drom.Plugins;

namespace wish_drom.Services
{
    /// <summary>
    /// 聊天服务实现 - 基于 Semantic Kernel
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly AppDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;
        private Kernel? _kernel;
        private ChatCompletionAgent? _agent;
        private string? _apiKey;
        private string? _modelId;
        private readonly Dictionary<string, List<ChatMessageContent>> _sessionHistory = new();

        public ChatService(AppDbContext dbContext, IServiceProvider serviceProvider)
        {
            _dbContext = dbContext;
            _serviceProvider = serviceProvider;
        }

        public async Task<bool> IsConfiguredAsync()
        {
            _apiKey = await SecureStorage.Default.GetAsync("openai_api_key");
            _modelId = await SecureStorage.Default.GetAsync("openai_model_id") ?? "gpt-4o-mini";
            return !string.IsNullOrEmpty(_apiKey);
        }

        public async Task ConfigureAsync(string apiKey, string modelId = "gpt-4o-mini")
        {
            await SecureStorage.Default.SetAsync("openai_api_key", apiKey);
            await SecureStorage.Default.SetAsync("openai_model_id", modelId);

            _apiKey = apiKey;
            _modelId = modelId;

            InitializeKernel();
        }

        private void InitializeKernel()
        {
            if (string.IsNullOrEmpty(_apiKey))
                return;

            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(_modelId ?? "gpt-4o-mini", _apiKey);

            // 注册插件
            var scheduleService = _serviceProvider.GetRequiredService<IScheduleService>();
            var activityService = _serviceProvider.GetRequiredService<IActivityService>();

            builder.Plugins.AddFromType<SchedulePlugin>("SchedulePlugin");
            builder.Plugins.AddFromType<ActivityPlugin>("ActivityPlugin");

            _kernel = builder.Build();

            _agent = new ChatCompletionAgent
            {
                Instructions = GetSystemPrompt(),
                Name = "CampusAssistant",
                Kernel = _kernel
            };
        }

        private string GetSystemPrompt()
        {
            return @"你是一个智能校园助手，主要职责是帮助学生管理课表和查询校园活动。

## 核心原则
1. **隐私优先**: 所有用户数据都存储在本地，你只能通过插件访问数据，不要编造任何信息
2. **准确回答**: 只根据插件返回的数据回答问题，如果数据不足，明确告知用户
3. **简洁友好**: 使用学生易懂的语言，避免冗长的回复

## 可用工具
- SchedulePlugin: 查询课程安排
  - get_today_schedule: 获取今天的课程
  - get_week_schedule: 获取指定周次的课程
  - get_day_schedule: 获取指定星期的课程
  - get_current_week: 获取当前周次

- ActivityPlugin: 查询校园活动
  - get_upcoming_activities: 获取近期活动
  - get_activity_sources: 获取活动来源列表

## 使用规范
1. 用户询问课程时，优先使用 SchedulePlugin
2. 用户询问活动时，使用 ActivityPlugin
3. 回答时包含: 时间、地点、课程名等关键信息
4. 如果没有相关数据，礼貌地提示用户先同步数据

## 回答格式
对于课程查询，使用以下格式:
📅 [日期] [节次]
📍 [地点]
👨‍🏫 [教师]

对于活动查询，使用以下格式:
🎉 [活动标题]
📅 [日期]
📍 [地点]
🏢 [来源]";
        }

        public string StartNewSession()
        {
            var sessionId = Guid.NewGuid().ToString("N");
            _sessionHistory[sessionId] = new List<ChatMessageContent>
            {
                new ChatMessageContent(AuthorRole.System, GetSystemPrompt())
            };
            return sessionId;
        }

        public async IAsyncEnumerable<string> SendMessageAsync(
            string message,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            // 确保已配置
            if (!await IsConfiguredAsync() || _kernel == null || _agent == null)
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
            var userMessage = new ChatMessageContent(AuthorRole.User, message);
            _sessionHistory[sessionId].Add(userMessage);

            // 保存到数据库
            await SaveMessageAsync(sessionId, "user", message);

            // 获取聊天服务
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // 流式响应
            var fullResponse = new System.Text.StringBuilder();
            await foreach (var content in chatService.GetStreamingChatMessageContentsAsync(
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
            _sessionHistory[sessionId].Add(new ChatMessageContent(AuthorRole.Assistant, responseText));
            await SaveMessageAsync(sessionId, "assistant", responseText);
        }

        public async Task<List<(string Role, string Content)>> GetSessionHistoryAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            var records = await _dbContext.ChatHistoryRecords
                .Where(r => r.SessionId == sessionId)
                .OrderBy(r => r.Timestamp)
                .ToListAsync(cancellationToken);

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
                .ToListAsync();

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
