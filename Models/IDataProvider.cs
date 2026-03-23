using wish_drom.Services.Interfaces;

namespace wish_drom.Models
{
    /// <summary>
    /// 数据提供者接口 - 由第三方实现
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
        /// 从页面中提取数据并返回 JSON
        /// </summary>
        /// <param name="html">页面 HTML 内容</param>
        /// <param name="secureStorage">安全存储接口，可用于保存 Cookie 等敏感信息</param>
        /// <returns>JSON 字符串</returns>
        Task<string> ExtractDataAsync(string html, ISecureDataStorage secureStorage);

        /// <summary>
        /// 解析返回的 JSON 数据，用于 LLM 工具调用
        /// </summary>
        /// <param name="jsonData">之前保存的 JSON 数据</param>
        /// <param name="query">用户查询（如"今天有什么课？"）</param>
        /// <returns>自然语言回复</returns>
        Task<string> QueryDataAsync(string jsonData, string query);
    }
}
