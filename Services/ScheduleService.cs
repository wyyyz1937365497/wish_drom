using Microsoft.EntityFrameworkCore;
using wish_drom.Data;
using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 课表服务实现
    /// 对外使用绝对日期，内部自动处理周次转换
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
            return await GetScheduleByDateAsync(DateTime.Now, cancellationToken);
        }

        /// <summary>
        /// 按绝对日期获取课程（推荐）
        /// 服务自动计算周次和星期，避免模糊性
        /// </summary>
        public async Task<List<CourseSchedule>> GetScheduleByDateAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var dayOfWeek = GetDayOfWeekFromDate(date);
            var weekNumber = GetWeekNumberFromDate(date);

            return await _dbContext.CourseSchedules
                .Where(s => s.DayOfWeek == dayOfWeek &&
                           s.StartWeek <= weekNumber &&
                           s.EndWeek >= weekNumber)
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

        public async Task<List<CourseSchedule>> GetScheduleByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            var startWeek = GetWeekNumberFromDate(startDate);
            var endWeek = GetWeekNumberFromDate(endDate);

            return await _dbContext.CourseSchedules
                .Where(s => s.StartWeek <= endWeek && s.EndWeek >= startWeek)
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToListAsync(cancellationToken);
        }

        public async Task<int> SaveSchedulesAsync(List<CourseSchedule> schedules, CancellationToken cancellationToken = default)
        {
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

        /// <summary>
        /// 根据日期计算所在周次
        /// 对应秋季学期9月1日、春季学期3月1日开始
        /// </summary>
        public int GetWeekNumberFromDate(DateTime date)
        {
            var semesterStart = GetSemesterStartDate(date);
            if (date < semesterStart)
                return 1;

            var weeks = (int)((date - semesterStart).TotalDays / 7) + 1;
            return Math.Clamp(weeks, 1, 20);
        }

        /// <summary>
        /// 根据日期返回该日期所在学期的开始日期
        /// </summary>
        private DateTime GetSemesterStartDate(DateTime date)
        {
            if (date.Month >= 9)
            {
                return new DateTime(date.Year, 9, 1);
            }
            else if (date.Month >= 3)
            {
                return new DateTime(date.Year, 3, 1);
            }
            else
            {
                return new DateTime(date.Year - 1, 9, 1);
            }
        }

        /// <summary>
        /// 从日期计算星期几（1=周一，7=周日）
        /// </summary>
        private static int GetDayOfWeekFromDate(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            return dayOfWeek == 0 ? 7 : dayOfWeek;
        }

        public int GetCurrentWeekNumber()
        {
            return GetWeekNumberFromDate(DateTime.Now);
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
