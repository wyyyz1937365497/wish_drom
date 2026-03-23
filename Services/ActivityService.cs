using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 校园活动服务实现
    /// </summary>
    public class ActivityService : IActivityService
    {
        private readonly AppDbContext _dbContext;

        public ActivityService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<CampusActivity>> GetUpcomingActivitiesAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            var startDate = DateTime.Today;
            var endDate = startDate.AddDays(days);

            return await _dbContext.CampusActivities
                .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                .OrderBy(a => a.ActivityDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<CampusActivity>> GetActivitiesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.CampusActivities
                .Where(a => a.ActivityDate >= startDate && a.ActivityDate <= endDate)
                .OrderBy(a => a.ActivityDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<CampusActivity>> GetActivitiesBySourceAsync(string source, CancellationToken cancellationToken = default)
        {
            return await _dbContext.CampusActivities
                .Where(a => a.Source == source)
                .OrderByDescending(a => a.ActivityDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SaveActivitiesAsync(List<CampusActivity> activities, CancellationToken cancellationToken = default)
        {
            // 去重: 根据Title和ActivityDate判断是否已存在
            var existingTitles = await _dbContext.CampusActivities
                .Where(a => activities.Select(act => act.Title).Contains(a.Title))
                .Select(a => a.Title)
                .Distinct()
                .ToListAsync(cancellationToken);

            var newActivities = activities
                .Where(a => !existingTitles.Contains(a.Title))
                .ToList();

            if (newActivities.Any())
            {
                await _dbContext.CampusActivities.AddRangeAsync(newActivities, cancellationToken);
                return await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return 0;
        }

        public async Task MarkAsReadAsync(int activityId, CancellationToken cancellationToken = default)
        {
            var activity = await _dbContext.CampusActivities
                .FirstOrDefaultAsync(a => a.Id == activityId, cancellationToken);

            if (activity != null)
            {
                activity.IsRead = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.CampusActivities
                .CountAsync(a => !a.IsRead && a.ActivityDate >= DateTime.Today, cancellationToken);
        }
    }
}
