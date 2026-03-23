using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 校园活动服务接口
    /// </summary>
    public interface IActivityService
    {
        /// <summary>
        /// 获取即将到来的活动
        /// </summary>
        /// <param name="days">天数范围</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>活动列表</returns>
        Task<List<CampusActivity>> GetUpcomingActivitiesAsync(int days = 7, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定日期范围的活动
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>活动列表</returns>
        Task<List<CampusActivity>> GetActivitiesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取按来源分组的活动
        /// </summary>
        /// <param name="source">来源名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>活动列表</returns>
        Task<List<CampusActivity>> GetActivitiesBySourceAsync(string source, CancellationToken cancellationToken = default);

        /// <summary>
        /// 保存活动数据
        /// </summary>
        /// <param name="activities">活动列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>保存的记录数</returns>
        Task<int> SaveActivitiesAsync(List<CampusActivity> activities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 标记活动为已读
        /// </summary>
        /// <param name="activityId">活动ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task MarkAsReadAsync(int activityId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取未读活动数量
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    }
}
