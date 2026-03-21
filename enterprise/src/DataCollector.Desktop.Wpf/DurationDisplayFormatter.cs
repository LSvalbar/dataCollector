namespace DataCollector.Desktop.Wpf;

internal static class DurationDisplayFormatter
{
    public static string FormatFromSeconds(double totalSeconds)
    {
        var normalizedSeconds = Math.Max(0d, totalSeconds);
        if (normalizedSeconds < 3600d)
        {
            return $"{Math.Max(0L, (long)Math.Round(normalizedSeconds, MidpointRounding.AwayFromZero))} 秒";
        }

        var hoursValue = normalizedSeconds / 3600d;
        if (hoursValue < 24d)
        {
            var hours = Math.Max(1L, (long)Math.Round(hoursValue, MidpointRounding.AwayFromZero));
            return $"{hours} 小时";
        }

        var daysValue = hoursValue / 24d;
        if (daysValue < 30d)
        {
            var days = Math.Max(1L, (long)Math.Round(daysValue, MidpointRounding.AwayFromZero));
            return $"{days} 天";
        }

        var monthsValue = daysValue / 30d;
        if (monthsValue < 12d)
        {
            var months = Math.Max(1L, (long)Math.Round(monthsValue, MidpointRounding.AwayFromZero));
            return $"{months} 月";
        }

        var years = Math.Max(1L, (long)Math.Round(daysValue / 365d, MidpointRounding.AwayFromZero));
        return $"{years} 年";
    }

    public static string FormatFromMinutes(double totalMinutes)
    {
        return FormatFromSeconds(totalMinutes * 60d);
    }

    public static string FormatFromMilliseconds(long? totalMilliseconds)
    {
        if (!totalMilliseconds.HasValue)
        {
            return "-";
        }

        return FormatFromSeconds(totalMilliseconds.Value / 1000d);
    }
}
