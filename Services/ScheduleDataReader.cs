using wish_drom.Data.Entities;
using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 课表数据路由实现
    /// 优先读取当前激活的数据源。
    /// </summary>
    public class ScheduleDataReader : IScheduleDataReader
    {
        private readonly IProviderRegistry _providerRegistry;

        public ScheduleDataReader(IProviderRegistry providerRegistry)
        {
            _providerRegistry = providerRegistry;
        }

        public async Task<List<CourseSchedule>> GetAllCoursesAsync(CancellationToken cancellationToken = default)
        {
            var scheduleStore = ResolveActiveScheduleStore();
            if (scheduleStore == null)
            {
                return new List<CourseSchedule>();
            }

            return await scheduleStore.GetAllSchedulesAsync(cancellationToken);
        }

        public async Task<List<CourseSchedule>> SearchCoursesAsync(string keyword, CancellationToken cancellationToken = default)
        {
            var scheduleStore = ResolveActiveScheduleStore();
            if (scheduleStore == null)
            {
                return new List<CourseSchedule>();
            }

            return await scheduleStore.SearchSchedulesAsync(keyword, cancellationToken);
        }

        private IScheduleDataStore? ResolveActiveScheduleStore()
        {
            var activeSourceId = _providerRegistry.GetActiveSourceId();
            if (!string.IsNullOrWhiteSpace(activeSourceId)
                && _providerRegistry.TryGet(activeSourceId, out var activeSource)
                && activeSource?.Provider is IScheduleDataStore activeStore)
            {
                return activeStore;
            }

            var fallbackSource = _providerRegistry.GetAll()
                .FirstOrDefault(source => source.Provider is IScheduleDataStore);

            return fallbackSource?.Provider as IScheduleDataStore;
        }
    }
}
