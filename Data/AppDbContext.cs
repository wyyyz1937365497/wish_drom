using Microsoft.EntityFrameworkCore;
using wish_drom.Data.Entities;
using Microsoft.Maui.Storage;

namespace wish_drom.Data
{
    /// <summary>
    /// EF Core 数据库上下文
    /// </summary>
    public class AppDbContext : DbContext
    {
        public DbSet<ChatHistoryRecord> ChatHistoryRecords { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "campus.db");
            options.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置 ChatHistoryRecord
            modelBuilder.Entity<ChatHistoryRecord>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.Timestamp);
            });
        }

        /// <summary>
        /// 初始化数据库，确保表结构已创建
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            await Database.EnsureCreatedAsync();
        }
    }
}
