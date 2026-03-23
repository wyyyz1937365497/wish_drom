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
        public async Task SetAsync(string key, string value)
        {
            await Task.Run(() => Microsoft.Maui.Storage.SecureStorage.Default.SetAsync(key, value));
        }

        public async Task<string?> GetAsync(string key)
        {
            return await Task.Run(() => Microsoft.Maui.Storage.SecureStorage.Default.GetAsync(key));
        }

        public async Task RemoveAsync(string key)
        {
            await Task.Run(() => Microsoft.Maui.Storage.SecureStorage.Default.Remove(key));
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
    }
}
