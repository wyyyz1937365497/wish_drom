using wish_drom.Data.Entities;
using wish_drom.Services.Interfaces;

namespace wish_drom.Services
{
    /// <summary>
    /// 课表服务实现
    /// 对外使用绝对日期，内部自动处理周次转换。
    /// </summary>
    public class ScheduleService : IScheduleService
    {
        private readonly IScheduleDataReader _scheduleDataReader;
        private readonly ISchoolCalendarService _calendarService;

        public ScheduleService(IScheduleDataReader scheduleDataReader, ISchoolCalendarService calendarService)
        {
            _scheduleDataReader = scheduleDataReader;
            _calendarService = calendarService;
        }

        public async Task<List<CourseSchedule>> GetTodayScheduleAsync(CancellationToken cancellationToken = default)
        {
            return await GetScheduleByDateAsync(DateTime.Now, cancellationToken);
        }

        public async Task<List<CourseSchedule>> GetScheduleByDateAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var dayOfWeek = GetDayOfWeekFromDate(date);
            var weekNumber = await _calendarService.GetWeekNumberFromDateAsync(date);
            var allCourses = await _scheduleDataReader.GetAllCoursesAsync(cancellationToken);

            return allCourses
                .Where(s => s.DayOfWeek == dayOfWeek && s.StartWeek <= weekNumber && s.EndWeek >= weekNumber)
                .OrderBy(s => s.StartPeriod)
                .ToList();
        }

        public async Task<List<CourseSchedule>> GetWeekScheduleAsync(int weekNumber, CancellationToken cancellationToken = default)
        {
            var allCourses = await _scheduleDataReader.GetAllCoursesAsync(cancellationToken);
            return allCourses
                .Where(s => s.StartWeek <= weekNumber && s.EndWeek >= weekNumber)
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToList();
        }

        public async Task<List<CourseSchedule>> GetDayScheduleAsync(int dayOfWeek, CancellationToken cancellationToken = default)
        {
            var allCourses = await _scheduleDataReader.GetAllCoursesAsync(cancellationToken);
            return allCourses
                .Where(s => s.DayOfWeek == dayOfWeek)
                .OrderBy(s => s.StartPeriod)
                .ToList();
        }

        public async Task<List<CourseSchedule>> GetSemesterScheduleAsync(CancellationToken cancellationToken = default)
        {
            var allCourses = await _scheduleDataReader.GetAllCoursesAsync(cancellationToken);
            return allCourses
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToList();
        }

        public async Task<List<CourseSchedule>> GetScheduleByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            var allCourses = await _scheduleDataReader.GetAllCoursesAsync(cancellationToken);
            var result = new List<CourseSchedule>();

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                var dayOfWeek = GetDayOfWeekFromDate(day);
                var week = await _calendarService.GetWeekNumberFromDateAsync(day);

                var dayCourses = allCourses
                    .Where(s => s.DayOfWeek == dayOfWeek && s.StartWeek <= week && s.EndWeek >= week)
                    .ToList();

                result.AddRange(dayCourses);
            }

            return result
                .OrderBy(s => s.DayOfWeek)
                .ThenBy(s => s.StartPeriod)
                .ToList();
        }

        public async Task<int> SaveSchedulesAsync(List<CourseSchedule> schedules, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return 0;
        }

        public async Task ClearSemesterScheduleAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
        }

        public Task<int> GetWeekNumberFromDateAsync(DateTime date)
        {
            return _calendarService.GetWeekNumberFromDateAsync(date);
        }

        public Task<int> GetCurrentWeekNumberAsync()
        {
            return _calendarService.GetCurrentWeekNumberAsync();
        }

        private static int GetDayOfWeekFromDate(DateTime date)
        {
            var dayOfWeek = (int)date.DayOfWeek;
            return dayOfWeek == 0 ? 7 : dayOfWeek;
        }
    }
}
