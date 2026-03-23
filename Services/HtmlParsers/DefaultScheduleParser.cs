using HtmlAgilityPack;
using wish_drom.Data.Entities;

namespace wish_drom.Services.HtmlParsers
{
    /// <summary>
    /// 默认课表HTML解析器
    /// 支持常见的教务系统HTML结构
    /// </summary>
    public class DefaultScheduleParser : IScheduleParser
    {
        public List<CourseSchedule> ParseScheduleHtml(string html, string semester)
        {
            var schedules = new List<CourseSchedule>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 尝试多种常见解析模式

            // 模式1: 表格形式 (最常见)
            var tableRows = doc.DocumentNode.SelectNodes("//table[@class='kebiao']//tr | //table[@id='courseTable']//tr | //table[contains(@class, 'schedule')]//tr");
            if (tableRows != null)
            {
                schedules.AddRange(ParseTableFormat(tableRows, semester));
            }

            // 模式2: 列表形式
            if (schedules.Count == 0)
            {
                var listItems = doc.DocumentNode.SelectNodes("//div[@class='course-item'] | //li[@class='course'] | //div[contains(@class, 'course-list')]//div[contains(@class, 'item')]");
                if (listItems != null)
                {
                    schedules.AddRange(ParseListFormat(listItems, semester));
                }
            }

            // 模式3: 卡片形式
            if (schedules.Count == 0)
            {
                var cards = doc.DocumentNode.SelectNodes("//div[@class='course-card'] | //div[contains(@class, 'class-card')] | //div[contains(@class, 'lesson-card')]");
                if (cards != null)
                {
                    schedules.AddRange(ParseCardFormat(cards, semester));
                }
            }

            return schedules;
        }

        private List<CourseSchedule> ParseTableFormat(HtmlNodeCollection rows, string semester)
        {
            var schedules = new List<CourseSchedule>();

            for (int i = 1; i < rows.Count; i++) // 跳过表头
            {
                var cells = rows[i].SelectNodes(".//td");
                if (cells == null || cells.Count < 8) continue;

                // 假设格式: [节次] [周一] [周二] [周三] [周四] [周五] [周六] [周日]
                for (int day = 1; day <= 7; day++)
                {
                    var cellText = cells[day].InnerText.Trim();
                    if (string.IsNullOrWhiteSpace(cellText) || cellText == "&nbsp;" || cellText == "-")
                        continue;

                    var period = i; // 行号对应节次

                    // 解析课程信息 (可能有多个课程在同一时间段)
                    var coursesInCell = cellText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var courseText in coursesInCell)
                    {
                        var schedule = ParseCourseText(courseText.Trim(), day, period, semester);
                        if (schedule != null)
                        {
                            schedules.Add(schedule);
                        }
                    }
                }
            }

            return schedules;
        }

        private List<CourseSchedule> ParseListFormat(HtmlNodeCollection items, string semester)
        {
            var schedules = new List<CourseSchedule>();

            foreach (var item in items)
            {
                var text = item.InnerText.Trim();
                var schedule = ParseCourseText(text, 0, 0, semester);
                if (schedule != null)
                {
                    schedules.Add(schedule);
                }
            }

            return schedules;
        }

        private List<CourseSchedule> ParseCardFormat(HtmlNodeCollection cards, string semester)
        {
            var schedules = new List<CourseSchedule>();

            foreach (var card in cards)
            {
                // 尝试从卡片中提取结构化信息
                var courseName = card.SelectSingleNode(".//*[@class='course-name'] | .//*[@class='subject'] | .//h3 | .//h4")?.InnerText.Trim();
                var location = card.SelectSingleNode(".//*[@class='location'] | .//*[@class='room'] | .//*[@class='place']")?.InnerText.Trim();
                var teacher = card.SelectSingleNode(".//*[@class='teacher'] | .//*[@class='instructor']")?.InnerText.Trim();
                var timeText = card.SelectSingleNode(".//*[@class='time'] | .//*[@class='schedule']")?.InnerText.Trim();

                if (!string.IsNullOrWhiteSpace(courseName))
                {
                    var schedule = new CourseSchedule
                    {
                        CourseName = courseName,
                        Location = location ?? "",
                        Teacher = teacher ?? "",
                        Semester = semester
                    };

                    // 解析时间信息
                    if (!string.IsNullOrWhiteSpace(timeText))
                    {
                        ParseTimeText(schedule, timeText);
                    }

                    schedules.Add(schedule);
                }
            }

            return schedules;
        }

        private CourseSchedule? ParseCourseText(string text, int dayOfWeek, int period, string semester)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // 常见格式: "课程名 @教师 @地点" 或 "课程名 教师 地点"
            var parts = text.Split(new[] { '@', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return null;

            var schedule = new CourseSchedule
            {
                CourseName = parts[0],
                Location = parts.Length > 1 ? parts[^1] : "",
                Teacher = parts.Length > 2 ? parts[1] : "",
                DayOfWeek = dayOfWeek,
                StartPeriod = period,
                EndPeriod = period,
                StartWeek = 1,
                EndWeek = 20,
                Semester = semester
            };

            return schedule;
        }

        private void ParseTimeText(CourseSchedule schedule, string timeText)
        {
            // 解析如 "周一 3-4节" 或 "周1 第3-4节" 等
            var dayMatch = System.Text.RegularExpressions.Regex.Match(timeText, "周([一二三四五六日1234567])");
            if (dayMatch.Success)
            {
                var dayChar = dayMatch.Groups[1].Value;
                schedule.DayOfWeek = dayChar switch
                {
                    "一" or "1" => 1,
                    "二" or "2" => 2,
                    "三" or "3" => 3,
                    "四" or "4" => 4,
                    "五" or "5" => 5,
                    "六" or "6" => 6,
                    "日" or "7" => 7,
                    _ => schedule.DayOfWeek
                };
            }

            var periodMatch = System.Text.RegularExpressions.Regex.Match(timeText, @"(\d+)-(\d+)节");
            if (periodMatch.Success)
            {
                schedule.StartPeriod = int.Parse(periodMatch.Groups[1].Value);
                schedule.EndPeriod = int.Parse(periodMatch.Groups[2].Value);
            }
        }

        public bool IsSupported(string html)
        {
            // 检查是否包含常见的课表相关元素
            return html.Contains("课程") || html.Contains("课表") || html.Contains("course") ||
                   html.Contains("kebiao") || html.Contains("schedule");
        }
    }
}
