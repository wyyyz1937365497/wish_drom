using wish_drom.Models;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// Provider 注册中心实现
    /// </summary>
    public class ProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<string, DataSourceConfig> _sources = new();
        private readonly object _syncRoot = new();
        private string? _activeSourceId;

        public void Register(DataSourceConfig sourceConfig)
        {
            lock (_syncRoot)
            {
                _sources[sourceConfig.Id] = sourceConfig;
            }
        }

        public bool TryGet(string sourceId, out DataSourceConfig? sourceConfig)
        {
            lock (_syncRoot)
            {
                var found = _sources.TryGetValue(sourceId, out var config);
                sourceConfig = config;
                return found;
            }
        }

        public List<DataSourceConfig> GetAll()
        {
            lock (_syncRoot)
            {
                return _sources.Values.ToList();
            }
        }

        public void SetActiveSource(string sourceId)
        {
            lock (_syncRoot)
            {
                if (_sources.ContainsKey(sourceId))
                {
                    _activeSourceId = sourceId;
                }
            }
        }

        public string? GetActiveSourceId()
        {
            lock (_syncRoot)
            {
                return _activeSourceId;
            }
        }
    }
}
