using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 课表服务实现
    /// </summary>
    public class ScheduleService : IScheduleService
    {
        private readonly AppDbContext _dbContext;

        public ScheduleService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<CourseSchedule>> GetTodayScheduleAsync(CancellationToken cancellationToken = default)
        {
            var today = (int)DateTime.Now.DayOfWeek;
            // 调整: 将周日(0)转为7
            var dayOfWeek = today == 0 ? 7 : today;
            var currentWeek = GetCurrentWeekNumber();

            return await _dbContext.CourseSchedules
                .Where(s => s.DayOfWeek == dayOfWeek &&
                           s.StartWeek <= currentWeek &&
                           s.EndWeek >= currentWeek)
                .OrderBy(s => s.StartPeriod)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<CourseSchedule>> GetWeekScheduleAsync(int weekNumber, CancellationToken cancellationToken = default)
        {
            return await _dbContext.CourseSchedules
                .Where(s => s.StartWeek <= weekNumber && s.EndWeek >= weekNumber)
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<CourseSchedule>> GetDayScheduleAsync(int dayOfWeek, CancellationToken cancellationToken = default)
        {
            return await _dbContext.CourseSchedules
                .Where(s => s.DayOfWeek == dayOfWeek)
                .OrderBy(s => s.StartPeriod)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<CourseSchedule>> GetSemesterScheduleAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.CourseSchedules
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SaveSchedulesAsync(List<CourseSchedule> schedules, CancellationToken cancellationToken = default)
        {
            // 先删除当前学期的数据
            await ClearSemesterScheduleAsync(cancellationToken);

            await _dbContext.CourseSchedules.AddRangeAsync(schedules, cancellationToken);
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task ClearSemesterScheduleAsync(CancellationToken cancellationToken = default)
        {
            var currentSemester = GetCurrentSemester();
            var existingSchedules = await _dbContext.CourseSchedules
                .Where(s => s.Semester == currentSemester)
                .ToListAsync(cancellationToken);

            _dbContext.CourseSchedules.RemoveRange(existingSchedules);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public int GetCurrentWeekNumber()
        {
            // 简单实现: 假设学期开始于9月1日
            var semesterStart = new DateTime(DateTime.Now.Year, 9, 1);
            if (DateTime.Now.Month < 9)
            {
                semesterStart = new DateTime(DateTime.Now.Year - 1, 9, 1);
            }

            var weeks = (int)((DateTime.Now - semesterStart).TotalDays / 7) + 1;
            return Math.Clamp(weeks, 1, 20);
        }

        private string GetCurrentSemester()
        {
            var year = DateTime.Now.Year;
            if (DateTime.Now.Month >= 9)
            {
                return $"{year}-{year + 1}第一学期";
            }
            else
            {
                return $"{year - 1}-{year}第二学期";
            }
        }
    }
}
