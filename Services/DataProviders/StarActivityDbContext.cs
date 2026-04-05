using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using wish_drom.Data.Entities;

namespace wish_drom.Services.DataProviders
{
    /// <summary>
    /// STAR 活动平台 Provider 私有数据库
    /// </summary>
    public class StarActivityDbContext : DbContext
    {
        public DbSet<CampusActivity> Activities { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "star-activity.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CampusActivity>(entity =>
            {
                entity.ToTable("campus_activities");
                entity.HasIndex(e => e.ActivityDate);
                entity.HasIndex(e => e.Source);
                entity.HasIndex(e => e.SyncTime);
            });
        }

        public async Task InitializeDatabaseAsync()
        {
            await Database.EnsureCreatedAsync();
        }
    }
}
