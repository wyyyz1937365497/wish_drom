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
        private readonly ISchoolCalendarService _calendarService;

        public ScheduleService(AppDbContext dbContext, ISchoolCalendarService calendarService)
        {
            _dbContext = dbContext;
            _calendarService = calendarService;
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
            var weekNumber = await _calendarService.GetWeekNumberFromDateAsync(date);

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
            var startWeek = await _calendarService.GetWeekNumberFromDateAsync(startDate);
            var endWeek = await _calendarService.GetWeekNumberFromDateAsync(endDate);

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
        /// 从日期计算星期几（1=周一，7=周日）
        /// </summary>
        private static int GetDayOfWeekFromDate(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            return dayOfWeek == 0 ? 7 : dayOfWeek;
        }

        /// <summary>
        /// 根据日期计算所在周次（使用校历服务）
        /// </summary>
        public Task<int> GetWeekNumberFromDateAsync(DateTime date)
        {
            return _calendarService.GetWeekNumberFromDateAsync(date);
        }

        /// <summary>
        /// 获取当前周次（使用校历服务）
        /// </summary>
        public Task<int> GetCurrentWeekNumberAsync()
        {
            return _calendarService.GetCurrentWeekNumberAsync();
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
