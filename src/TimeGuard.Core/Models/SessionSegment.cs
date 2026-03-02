namespace TimeGuard.Models;

/// <summary>
/// A single app usage session returned by DatabaseService.LoadSessionsForDay().
/// Used in the dashboard drilldown to show start→end time segments.
/// </summary>
public class SessionSegment
{
    public string ProcessName  { get; set; } = string.Empty;
    public string WindowTitle  { get; set; } = string.Empty;
    public string StartTime    { get; set; } = string.Empty;
    public string EndTime      { get; set; } = string.Empty;
    public int    IsPassive    { get; set; }

    public string StartDisplay => DateTime.TryParse(StartTime, out var dt) ? dt.ToString("HH:mm") : StartTime;
    public string EndDisplay   => DateTime.TryParse(EndTime,   out var dt) ? dt.ToString("HH:mm") : EndTime;

    public string DurationDisplay
    {
        get
        {
            if (!DateTime.TryParse(StartTime, out var s) || !DateTime.TryParse(EndTime, out var e)) return "-";
            var mins = (e - s).TotalMinutes;
            return mins < 1 ? "<1 min" : $"{(int)mins} min";
        }
    }
}
