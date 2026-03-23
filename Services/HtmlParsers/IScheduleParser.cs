using wish_drom.Data.Entities;

namespace wish_drom.Services.HtmlParsers
{
    /// <summary>
    /// 课表HTML解析器接口
    /// </summary>
    public interface IScheduleParser
    {
        /// <summary>
        /// 解析课表HTML内容
        /// </summary>
        /// <param name="html">HTML内容</param>
        /// <param name="semester">学期标识</param>
        /// <returns>解析出的课程列表</returns>
        List<CourseSchedule> ParseScheduleHtml(string html, string semester);

        /// <summary>
        /// 检查HTML是否来自支持的教务系统
        /// </summary>
        /// <param name="html">HTML内容</param>
        /// <returns>是否支持</returns>
        bool IsSupported(string html);
    }
}
