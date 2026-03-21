using DataCollector.Contracts;

namespace DataCollector.Core;

public sealed class DailyMetricsCalculator
{
    public DailyMetricsSnapshot Calculate(IReadOnlyCollection<TimelineSegmentDto> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var snapshot = new DailyMetricsSnapshot();

        foreach (var segment in segments)
        {
            snapshot.ObservedMinutes += segment.DurationMinutes;
            switch (segment.State)
            {
                case MachineOperationalState.Processing:
                    snapshot.PowerOnMinutes += segment.DurationMinutes;
                    snapshot.ProcessingMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.Waiting:
                    snapshot.PowerOnMinutes += segment.DurationMinutes;
                    snapshot.WaitingMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.Standby:
                    snapshot.PowerOnMinutes += segment.DurationMinutes;
                    snapshot.StandbyMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.PowerOff:
                    snapshot.PowerOffMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.Alarm:
                    snapshot.PowerOnMinutes += segment.DurationMinutes;
                    snapshot.AlarmMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.Emergency:
                    snapshot.PowerOnMinutes += segment.DurationMinutes;
                    snapshot.EmergencyMinutes += segment.DurationMinutes;
                    break;
                case MachineOperationalState.CommunicationInterrupted:
                    snapshot.CommunicationInterruptedMinutes += segment.DurationMinutes;
                    break;
                default:
                    throw new InvalidOperationException($"不支持的状态值：{segment.State}");
            }
        }

        return snapshot;
    }
}
