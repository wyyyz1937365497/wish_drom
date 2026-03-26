using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 校园活动服务实现
    /// 基于活动数据读取路由，不依赖主程序数据库表。
    /// </summary>
    public class ActivityService : IActivityService
    {
        private readonly IActivityDataReader _activityDataReader;

        public ActivityService(IActivityDataReader activityDataReader)
        {
            _activityDataReader = activityDataReader;
        }

        public async Task<List<CampusActivity>> GetUpcomingActivitiesAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            var startDate = DateTime.Today;
            var endDate = startDate.AddDays(days);
            var allActivities = await _activityDataReader.GetAllActivitiesAsync(cancellationToken);

            return allActivities
                .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                .OrderBy(a => a.ActivityDate)
                .ToList();
        }

        public async Task<List<CampusActivity>> GetActivitiesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            var allActivities = await _activityDataReader.GetAllActivitiesAsync(cancellationToken);
            return allActivities
                .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                .OrderBy(a => a.ActivityDate)
                .ToList();
        }

        public async Task<List<CampusActivity>> GetActivitiesBySourceAsync(string source, CancellationToken cancellationToken = default)
        {
            var allActivities = await _activityDataReader.GetAllActivitiesAsync(cancellationToken);
            return allActivities
                .Where(a => a.Source == source)
                .OrderByDescending(a => a.ActivityDate)
                .ToList();
        }

        public async Task<int> SaveActivitiesAsync(List<CampusActivity> activities, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return 0;
        }

        public async Task MarkAsReadAsync(int activityId, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
        {
            var allActivities = await _activityDataReader.GetAllActivitiesAsync(cancellationToken);
            return allActivities.Count(a => !a.IsRead && a.ActivityDate >= DateTime.Today);
        }
    }
}
