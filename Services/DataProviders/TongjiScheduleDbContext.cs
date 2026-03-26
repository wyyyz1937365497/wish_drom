using Microsoft.EntityFrameworkCore;
using Microsoft.Maui.Storage;
using wish_drom.Data.Entities;

namespace wish_drom.Services.DataProviders
{
    /// <summary>
    /// 同济课表 Provider 私有数据库
    /// </summary>
    public class TongjiScheduleDbContext : DbContext
    {
        public DbSet<CourseSchedule> CourseSchedules { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "tongji-schedule.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CourseSchedule>(entity =>
            {
                entity.ToTable("course_schedules");
                entity.HasIndex(e => new { e.DayOfWeek, e.StartPeriod, e.StartWeek });
                entity.HasIndex(e => e.Semester);
                entity.HasIndex(e => e.CourseName);
            });
        }

        public async Task InitializeDatabaseAsync()
        {
            await Database.EnsureCreatedAsync();
        }
    }
}
