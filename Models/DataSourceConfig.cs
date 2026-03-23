namespace wish_drom.Models
{
    /// <summary>
    /// 数据源类型枚举
    /// </summary>
    public enum DataSourceType
    {
        /// <summary>
        /// 自定义数据源
        /// </summary>
        Custom
    }

    /// <summary>
    /// 数据源配置
    /// </summary>
    public class DataSourceConfig
    {
        /// <summary>
        /// 数据源唯一标识
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 登录/数据获取 URL
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 网站 Favicon URL（可选，默认会从域名获取）
        /// </summary>
        public string? FaviconUrl { get; set; }

        /// <summary>
        /// 作为 LLM 工具时的描述
        /// </summary>
        public string ToolDescription { get; set; } = string.Empty;

        /// <summary>
        /// 数据提供者实例
        /// </summary>
        public IDataProvider Provider { get; set; } = null!;

        /// <summary>
        /// 获取显示的图标（优先使用 FaviconUrl）
        /// </summary>
        public string GetDisplayIcon()
        {
            if (!string.IsNullOrEmpty(FaviconUrl))
                return $"<img src=\"{FaviconUrl}\" class=\"favicon\" />";

            // 从 URL 提取域名作为默认图标
            return "🌐";
        }

        /// <summary>
        /// 获取首字母作为占位图标
        /// </summary>
        public string GetFaviconLetter()
        {
            if (!string.IsNullOrEmpty(DisplayName))
                return DisplayName.Substring(0, 1).ToUpper();
            return "?";
        }
    }
}
