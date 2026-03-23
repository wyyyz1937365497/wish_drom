using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 课表服务接口
    /// </summary>
    public interface IScheduleService
    {
        /// <summary>
        /// 获取今天的课程安排
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>今天的课程列表</returns>
        Task<List<CourseSchedule>> GetTodayScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定周次的课程安排
        /// </summary>
        /// <param name="weekNumber">周次 (1-20)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>指定周次的课程列表</returns>
        Task<List<CourseSchedule>> GetWeekScheduleAsync(int weekNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定星期的课程安排
        /// </summary>
        /// <param name="dayOfWeek">星期几 (1-7)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>指定星期的课程列表</returns>
        Task<List<CourseSchedule>> GetDayScheduleAsync(int dayOfWeek, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前学期的所有课程
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>课程列表</returns>
        Task<List<CourseSchedule>> GetSemesterScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 保存课程数据
        /// </summary>
        /// <param name="schedules">课程列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>保存的记录数</returns>
        Task<int> SaveSchedulesAsync(List<CourseSchedule> schedules, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清除当前学期的课程数据
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task ClearSemesterScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前周次
        /// </summary>
        /// <returns>当前周次 (1-20)</returns>
        int GetCurrentWeekNumber();
    }
}
