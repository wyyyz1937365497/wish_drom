using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 课表服务接口
    /// 对外统一使用绝对日期（几月几日），服务内部自动处理周次和星期的转换
    /// </summary>
    public interface IScheduleService
    {
        /// <summary>
        /// 获取今天的课程安排
        /// </summary>
        Task<List<CourseSchedule>> GetTodayScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定日期的课程安排（推荐使用，使用绝对日期避免歧义）
        /// </summary>
        Task<List<CourseSchedule>> GetScheduleByDateAsync(DateTime date, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定周次的课程安排（内部使用）
        /// </summary>
        Task<List<CourseSchedule>> GetWeekScheduleAsync(int weekNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定星期的课程安排
        /// </summary>
        Task<List<CourseSchedule>> GetDayScheduleAsync(int dayOfWeek, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前学期的所有课程
        /// </summary>
        Task<List<CourseSchedule>> GetSemesterScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定日期范围内的课程安排
        /// </summary>
        Task<List<CourseSchedule>> GetScheduleByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 保存课程数据
        /// </summary>
        Task<int> SaveSchedulesAsync(List<CourseSchedule> schedules, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清除当前学期的课程数据
        /// </summary>
        Task ClearSemesterScheduleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前周次
        /// </summary>
        int GetCurrentWeekNumber();

        /// <summary>
        /// 根据日期获取周次
        /// </summary>
        int GetWeekNumberFromDate(DateTime date);
    }
}
