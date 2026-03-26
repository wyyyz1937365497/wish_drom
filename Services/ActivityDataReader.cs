using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 活动数据路由实现
    /// 当前聚合所有实现 IActivityDataStore 的 Provider。
    /// </summary>
    public class ActivityDataReader : IActivityDataReader
    {
        private readonly IProviderRegistry _providerRegistry;

        public ActivityDataReader(IProviderRegistry providerRegistry)
        {
            _providerRegistry = providerRegistry;
        }

        public async Task<List<CampusActivity>> GetAllActivitiesAsync(CancellationToken cancellationToken = default)
        {
            var activityStores = _providerRegistry.GetAll()
                .Select(source => source.Provider)
                .OfType<IActivityDataStore>()
                .ToList();

            if (activityStores.Count == 0)
            {
                return new List<CampusActivity>();
            }

            var allActivities = new List<CampusActivity>();
            foreach (var store in activityStores)
            {
                var activities = await store.GetAllActivitiesAsync(cancellationToken);
                if (activities.Count > 0)
                {
                    allActivities.AddRange(activities);
                }
            }

            return allActivities;
        }
    }
}
