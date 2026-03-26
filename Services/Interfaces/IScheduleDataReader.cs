using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 课表数据读取路由接口
    /// 主程序仅依赖该接口，不依赖具体 Provider 表结构。
    /// </summary>
    public interface IScheduleDataReader
    {
        Task<List<CourseSchedule>> GetAllCoursesAsync(CancellationToken cancellationToken = default);

        Task<List<CourseSchedule>> SearchCoursesAsync(string keyword, CancellationToken cancellationToken = default);
    }
}
