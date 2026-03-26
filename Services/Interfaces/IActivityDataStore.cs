using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// Provider 内部活动数据存储读取接口
    /// </summary>
    public interface IActivityDataStore
    {
        Task<List<CampusActivity>> GetAllActivitiesAsync(CancellationToken cancellationToken = default);
    }
}
