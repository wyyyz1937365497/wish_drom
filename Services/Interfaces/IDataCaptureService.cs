using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// WebView 数据抓取服务接口
    /// </summary>
    public interface IDataCaptureService
    {
        /// <summary>
        /// 启动 WebView 进行课表数据抓取
        /// </summary>
        /// <param name="targetUrl">目标教务系统URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>抓取到的课程数量</returns>
        Task<int> CaptureScheduleDataAsync(string targetUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// 启动 WebView 进行校园活动数据抓取
        /// </summary>
        /// <param name="targetUrl">目标活动页面URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>抓取到的活动数量</returns>
        Task<int> CaptureActivityDataAsync(string targetUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清除 WebView Cookie
        /// </summary>
        Task ClearCookiesAsync();

        /// <summary>
        /// 数据抓取进度事件
        /// </summary>
        event EventHandler<string>? CaptureProgress;
    }
}
