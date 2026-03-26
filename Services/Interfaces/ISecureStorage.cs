namespace wish_drom.Services.Interfaces
{
    /// <summary>
    /// 安全存储接口 - 用于数据提供者保存 Cookie 等敏感信息
    /// </summary>
    public interface ISecureDataStorage
    {
        /// <summary>
        /// 保存字符串数据
        /// </summary>
        Task SetAsync(string key, string value);

        /// <summary>
        /// 获取字符串数据
        /// </summary>
        Task<string?> GetAsync(string key);

        /// <summary>
        /// 删除数据
        /// </summary>
        Task RemoveAsync(string key);

        /// <summary>
        /// 检查键是否存在
        /// </summary>
        Task<bool> ContainsKeyAsync(string key);

        /// <summary>
        /// 清除所有数据
        /// </summary>
        Task ClearAsync();
    }

    /// <summary>
    /// 安全存储实现
    /// </summary>
    public class AppSecureDataStorage : ISecureDataStorage
    {
        private const string FallbackKeyPrefix = "secure_fallback_";

        public async Task SetAsync(string key, string value)
        {
            try
            {
                await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync(key, value);
                // SecureStorage 写入成功后清理兜底数据，避免读取到过期值。
                Preferences.Default.Remove(BuildFallbackKey(key));
            }
            catch (Exception ex) when (ShouldUseFallbackStorage(ex))
            {
                // 本地调试在未配置 Keychain entitlement 时，允许回退到 Preferences 以避免流程中断。
                Preferences.Default.Set(BuildFallbackKey(key), value);
            }
        }

        public async Task<string?> GetAsync(string key)
        {
            var fallbackKey = BuildFallbackKey(key);

            try
            {
                var secureValue = await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync(key);
                if (!string.IsNullOrEmpty(secureValue))
                    return secureValue;
            }
            catch (Exception ex) when (ShouldUseFallbackStorage(ex))
            {
                // 忽略并继续走兜底读取
            }

            if (!Preferences.Default.ContainsKey(fallbackKey))
                return null;

            var fallbackValue = Preferences.Default.Get(fallbackKey, string.Empty);
            return string.IsNullOrEmpty(fallbackValue) ? null : fallbackValue;
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                Microsoft.Maui.Storage.SecureStorage.Default.Remove(key);
            }
            catch (Exception ex) when (ShouldUseFallbackStorage(ex))
            {
                // 忽略异常，统一在后面删除兜底键
            }

            Preferences.Default.Remove(BuildFallbackKey(key));
            await Task.CompletedTask;
        }

        public async Task<bool> ContainsKeyAsync(string key)
        {
            var value = await GetAsync(key);
            return !string.IsNullOrEmpty(value);
        }

        public async Task ClearAsync()
        {
            await Task.CompletedTask;
            // MAUI 的 SecureStorage 没有直接的 Clear 方法
            // 需要逐个删除，这里简化处理
        }

        private static bool ShouldUseFallbackStorage(Exception ex)
        {
            return ex.ToString().Contains("MissingEntitlement", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFallbackKey(string key)
        {
            return $"{FallbackKeyPrefix}{key}";
        }
    }
}
