namespace DataCollector.Core;

public sealed class DailyMetricsSnapshot
{
    public double PowerOnMinutes { get; set; }

    public double ProcessingMinutes { get; set; }

    public double WaitingMinutes { get; set; }

    public double StandbyMinutes { get; set; }

    public double PowerOffMinutes { get; set; }

    public double AlarmMinutes { get; set; }

    public double EmergencyMinutes { get; set; }

    public double CommunicationInterruptedMinutes { get; set; }

    public double ObservedMinutes { get; set; }
}
