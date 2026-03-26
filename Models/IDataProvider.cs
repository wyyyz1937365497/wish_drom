using wish_drom.Services.Interfaces;

namespace wish_drom.Models
{
    /// <summary>
    /// 原始数据持久化结果
    /// </summary>
    public class PersistResult
    {
        public bool Success { get; set; }

        public int SavedRecordCount { get; set; }

        public string? Error { get; set; }
    }

    /// <summary>
    /// 数据提供者接口 - 支持 WebView 凭证提取 + 原生 API 请求双模式
    /// </summary>
    public interface IDataProvider
    {
        /// <summary>
        /// 检查 WebView 当前页面是否已完成登录/数据加载
        /// </summary>
        /// <param name="currentUrl">当前 WebView URL</param>
        /// <param name="html">当前页面 HTML 内容</param>
        /// <returns>true 表示可以开始提取数据</returns>
        bool IsReadyForExtraction(string currentUrl, string html);

        /// <summary>
        /// 【阶段一】在 WebView 上下文中执行，提取 Cookie/Token 等鉴权凭证并存储。
        /// 对于纯 HTML 解析的 Provider 可忽略 evaluateJavaScript 参数。
        /// </summary>
        /// <param name="html">页面 HTML 内容（兼容传统解析模式）</param>
        /// <param name="secureStorage">安全存储接口，可用于保存 Cookie 等敏感信息</param>
        /// <param name="evaluateJavaScript">
        /// WebView JS 执行委托，允许在 WebView 中执行异步脚本并获取结果。
        /// 传入 JS 表达式字符串，返回执行结果字符串。
        /// </param>
        /// <returns>
        /// 提取结果：原始业务数据 JSON、"CredentialsStored" 表示凭证已存储待后续 FetchDataAsync、或 null 表示失败
        /// </returns>
        Task<string?> ExtractDataAsync(
            string html,
            ISecureDataStorage secureStorage,
            Func<string, Task<string?>>? evaluateJavaScript = null
        );

        /// <summary>
        /// 【阶段二】在原生上下文中执行，使用存储的凭证发起 HTTP 请求获取业务数据。
        /// 适用于凭证已缓存、可静默同步的场景。
        /// </summary>
        /// <param name="secureStorage">安全存储，用于读取之前保存的 Cookie/Token</param>
        /// <returns>业务数据 JSON 字符串，或 null 表示获取失败</returns>
        /// <exception cref="AuthExpiredException">当凭证过期时抛出，触发重新登录流程</exception>
        Task<string?> FetchDataAsync(ISecureDataStorage secureStorage)
        {
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// 将原始数据转换为标准格式（可选实现）
        /// </summary>
        /// <param name="rawData">ExtractDataAsync 或 FetchDataAsync 返回的原始数据</param>
        /// <returns>标准化后的数据，默认直接返回原始数据</returns>
        Task<string?> QueryDataAsync(string rawData)
        {
            return Task.FromResult<string?>(rawData);
        }

        /// <summary>
        /// 将原始 JSON 持久化到 Provider 私有存储
        /// </summary>
        Task<PersistResult> PersistRawDataAsync(string rawData);
    }
}
