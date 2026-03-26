using wish_drom.Data.Entities;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 活动数据读取路由接口
    /// </summary>
    public interface IActivityDataReader
    {
        Task<List<CampusActivity>> GetAllActivitiesAsync(CancellationToken cancellationToken = default);
    }
}
