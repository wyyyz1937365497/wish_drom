using Microsoft.Maui.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;
using wish_drom.Services.Plugins;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Threading.Channels;

namespace wish_drom.Services
{
    /// <summary>
    /// 聊天服务实现 - 基于 Semantic Kernel
    /// </summary>
    public class ChatService : IChatService
    {
        private static void Log(string msg)
        {
            Debug.WriteLine(msg);
            Console.WriteLine(msg);
        }

        private readonly AppDbContext _dbContext;
        private readonly IScheduleDataReader _scheduleDataReader;
        private readonly ISecureDataStorage _secureStorage;
        private readonly ISchoolCalendarService _calendarService;
        private Kernel? _kernel;
        private IChatCompletionService? _chatService;
        private string? _apiKey;
        private string? _baseUrl;
        private string? _modelId;
        private readonly Dictionary<string, ChatHistory> _sessionHistory = new();

        public ChatService(
            AppDbContext dbContext,
            IScheduleDataReader scheduleDataReader,
            ISecureDataStorage secureStorage,
            ISchoolCalendarService calendarService)
        {
            _dbContext = dbContext;
            _scheduleDataReader = scheduleDataReader;
            _secureStorage = secureStorage;
            _calendarService = calendarService;
        }

        public async Task<bool> IsConfiguredAsync()
        {
            _apiKey = await SecureStorage.Default.GetAsync("openai_api_key");
            _baseUrl = await SecureStorage.Default.GetAsync("openai_base_url");
            _modelId = await SecureStorage.Default.GetAsync("openai_model_id") ?? "gpt-4o-mini";

            var configured = !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_baseUrl);
            if (configured && (_kernel == null || _chatService == null))
            {
                InitializeKernel();
            }

            return configured;
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

            var normalizedUrl = NormalizeBaseUrl(baseUrl);
            Debug.WriteLine($"[ChatService] 测试连接 BaseAddress: {normalizedUrl}");

            // 使用 HttpClient 设置自定义端点
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(normalizedUrl)
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

            var normalizedUrl = NormalizeBaseUrl(_baseUrl);
            Log($"[ChatService] 初始化 BaseAddress: {normalizedUrl}");

            // 使用 HttpClient 设置自定义端点
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(normalizedUrl)
            };

            builder.AddOpenAIChatCompletion(
                _modelId ?? "gpt-4o-mini",
                _apiKey,
                httpClient: httpClient
            );

            // 注册课表插件
            builder.Plugins.AddFromObject(new SchedulePlugin(_scheduleDataReader, _secureStorage, _calendarService));
            Log("[ChatService] 已注册插件: SchedulePlugin");

            _kernel = builder.Build();
            _chatService = _kernel.GetRequiredService<IChatCompletionService>();
        }

        private string NormalizeBaseUrl(string baseUrl)
        {
            // 移除末尾的斜杠
            baseUrl = baseUrl.TrimEnd('/');

            // 检查是否已经包含版本号路径（如 /v1, /v2, /v3, /v4 等）
            // 或者已经包含完整的 API 路径（如 /api/paas/v4）
            var hasVersionPath = System.Text.RegularExpressions.Regex.IsMatch(baseUrl, @"/v\d+(/|$)");

            if (hasVersionPath)
            {
                // 已经包含版本号，直接使用
                Debug.WriteLine($"[ChatService] URL 已包含版本路径: {baseUrl}");
                return baseUrl;
            }

            // 没有版本号，添加默认的 /v1
            var result = baseUrl + "/v1";
            Debug.WriteLine($"[ChatService] URL 添加 /v1: {baseUrl} -> {result}");
            return result;
        }

        private string GetSystemPrompt()
        {
            return @"你是一个智能校园助手，主要职责是帮助学生管理课表和查询校园活动。

## 核心原则
1. **隐私优先**: 所有用户数据都存储在本地，不要编造任何信息
2. **准确回答**: 只根据提供的数据回答问题，如果数据不足，明确告知用户
3. **简洁友好**: 使用学生易懂的语言，避免冗长的回复

## 可用功能
你有以下工具可以调用：

### 课表查询
- 查询今天的课程：获取今天的所有课程安排
- 查询指定日期的课程：支持【3月25日】、【明天】、【后天】、【下周一】等日期格式
- 查询日期范围内的课程：如查询本周、下周的课程，使用开始日期和结束日期
- 查询本周课程：获取本周所有课程概览
- 查询课程详情：根据课程名称获取详细信息
- 获取课表统计：获取课表统计数据，包括当前日期、周次、课程数等
- 通过同济服务查询当天课表：仅在用户明确要求实时同济结果时使用
- 通过同济服务查询日期范围课表：仅在用户明确要求实时同济结果时使用

## 使用指引
- 日期参数使用自然语言格式：【今天】、【明天】、【后天】、【周X】、【下周X】、【3月25日】等
- 当用户问课表相关问题时，调用相应的工具查询
- 默认优先使用本地课表数据进行推断，避免频繁调用远程 API
- 只有当用户明确要求“实时同济接口/在线结果”时，才调用“通过同济服务查询...”工具
- 如果用户没有明确提“实时/在线/同济接口/官网最新”，禁止调用 get_today_courses_from_tongji 和 get_courses_by_date_range_from_tongji
- 服务会自动处理日期到周次的转换，你只需要传递用户说的日期
- 如果查询结果显示没有数据，提醒用户先在【数据同步】页面同步课表
- 返回的结果已经格式化，可以直接展示给用户

## 回答格式
使用简洁友好的语言，适当使用emoji让回答更生动。";
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
                var history = new ChatHistory();
                history.AddSystemMessage(GetSystemPrompt());
                _sessionHistory[sessionId] = history;
            }

            // 添加用户消息
            _sessionHistory[sessionId].AddUserMessage(message);
            Log($"[ChatService] 用户消息: session={sessionId}, text={message}");

            // 保存到数据库
            await SaveMessageAsync(sessionId, "user", message);

            // 流式响应
            var fullResponse = new System.Text.StringBuilder();
            var isScheduleQuery = IsScheduleRelatedQuery(message);
            var settings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = 1000,
                Temperature = 0.7f,
                FunctionChoiceBehavior = isScheduleQuery
                    ? FunctionChoiceBehavior.Required()
                    : FunctionChoiceBehavior.Auto()
            };

            Log($"[ChatService] 执行设置: scheduleQuery={isScheduleQuery}, functionChoice={(isScheduleQuery ? "Required" : "Auto")}");

            var chunkChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            var streamTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var content in _chatService.GetStreamingChatMessageContentsAsync(
                        _sessionHistory[sessionId],
                        executionSettings: settings,
                        kernel: _kernel,
                        cancellationToken: cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        Log($"[ChatService] 流式分片: role={content.Role}, hasContent={!string.IsNullOrEmpty(content.Content)}");
                        if (!string.IsNullOrEmpty(content.Content))
                        {
                            await chunkChannel.Writer.WriteAsync(content.Content, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    chunkChannel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    chunkChannel.Writer.TryComplete(ex);
                }
            }, cancellationToken);

            await foreach (var chunk in chunkChannel.Reader.ReadAllAsync(cancellationToken))
            {
                fullResponse.Append(chunk);
                yield return chunk;
            }

            await streamTask.ConfigureAwait(false);

            // 保存助手回复
            var responseText = fullResponse.ToString();
            _sessionHistory[sessionId].AddAssistantMessage(responseText);
            await SaveMessageAsync(sessionId, "assistant", responseText);
        }

        private static bool IsScheduleRelatedQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var m = message.ToLowerInvariant();
            return m.Contains("课")
                || m.Contains("课程")
                || m.Contains("课表")
                || m.Contains("今天")
                || m.Contains("明天")
                || m.Contains("下周")
                || m.Contains("周一")
                || m.Contains("周二")
                || m.Contains("周三")
                || m.Contains("周四")
                || m.Contains("周五")
                || m.Contains("周六")
                || m.Contains("周日")
                || m.Contains("schedule")
                || m.Contains("course");
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
