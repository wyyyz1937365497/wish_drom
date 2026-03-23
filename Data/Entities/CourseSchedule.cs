namespace wish_drom.Data.Entities
{
    /// <summary>
    /// 课程表实体
    /// </summary>
    public class CourseSchedule
    {
        public int Id { get; set; }

        /// <summary>
        /// 课程名称
        /// </summary>
        public string CourseName { get; set; } = string.Empty;

        /// <summary>
        /// 上课地点
        /// </summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>
        /// 授课教师
        /// </summary>
        public string Teacher { get; set; } = string.Empty;

        /// <summary>
        /// 星期几 (1-7, 周一到周日)
        /// </summary>
        public int DayOfWeek { get; set; }

        /// <summary>
        /// 开始周次
        /// </summary>
        public int StartWeek { get; set; }

        /// <summary>
        /// 结束周次
        /// </summary>
        public int EndWeek { get; set; }

        /// <summary>
        /// 开始节次 (1-12)
        /// </summary>
        public int StartPeriod { get; set; }

        /// <summary>
        /// 结束节次 (1-12)
        /// </summary>
        public int EndPeriod { get; set; }

        /// <summary>
        /// 周次备注 (如: 单周、双周、全周)
        /// </summary>
        public string? WeekType { get; set; }

        /// <summary>
        /// 学期标识
        /// </summary>
        public string Semester { get; set; } = string.Empty;

        /// <summary>
        /// 数据同步时间
        /// </summary>
        public DateTime SyncTime { get; set; } = DateTime.Now;
    }
}
