using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// Provider 内部课表数据存储读取接口
    /// </summary>
    public interface IScheduleDataStore
    {
        Task<List<CourseSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken = default);

        Task<List<CourseSchedule>> SearchSchedulesAsync(string keyword, CancellationToken cancellationToken = default);
    }
}
