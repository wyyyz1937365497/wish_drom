namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 数据捕获结果
    /// </summary>
    public class CaptureResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 保存记录数
        /// </summary>
        public int SavedRecordCount { get; set; }

        /// <summary>
        /// 数据源 ID
        /// </summary>
        public string? SourceId { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// WebView 数据抓取服务接口
    /// </summary>
    public interface IDataCaptureService
    {
        /// <summary>
        /// 注册数据提供者
        /// </summary>
        void RegisterProvider(string id, string displayName, string url, Models.IDataProvider provider, string? faviconUrl = null, string? toolDescription = null);

        /// <summary>
        /// 获取所有已注册的数据源
        /// </summary>
        List<Models.DataSourceConfig> GetRegisteredSources();

        /// <summary>
        /// 启动 WebView 数据抓取
        /// </summary>
        /// <param name="sourceId">数据源 ID</param>
        /// <param name="onResult">完成回调</param>
        void StartCapture(string sourceId, Action<CaptureResult> onResult);

        /// <summary>
        /// 取消当前抓取
        /// </summary>
        void CancelCapture();

        /// <summary>
        /// 清除所有存储的数据（Cookie 等）
        /// </summary>
        Task ClearAllDataAsync();
    }
}
