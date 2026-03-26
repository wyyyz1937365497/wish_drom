using wish_drom.Models;

namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// Provider 注册中心接口
    /// 统一管理数据源注册、查询与当前激活源。
    /// </summary>
    public interface IProviderRegistry
    {
        void Register(DataSourceConfig sourceConfig);

        bool TryGet(string sourceId, out DataSourceConfig? sourceConfig);

        List<DataSourceConfig> GetAll();

        void SetActiveSource(string sourceId);

        string? GetActiveSourceId();
    }
}
