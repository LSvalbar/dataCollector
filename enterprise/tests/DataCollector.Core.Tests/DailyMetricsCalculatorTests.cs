using DataCollector.Contracts;
using DataCollector.Core;

namespace DataCollector.Core.Tests;

public sealed class DailyMetricsCalculatorTests
{
    private readonly DailyMetricsCalculator _calculator = new();

    [Fact]
    public void Calculate_ShouldAggregateSegmentsIntoExpectedBuckets()
    {
        var segments = new[]
        {
            new TimelineSegmentDto { State = MachineOperationalState.PowerOff, DurationMinutes = 120 },
            new TimelineSegmentDto { State = MachineOperationalState.Standby, DurationMinutes = 30 },
            new TimelineSegmentDto { State = MachineOperationalState.Processing, DurationMinutes = 180 },
            new TimelineSegmentDto { State = MachineOperationalState.Waiting, DurationMinutes = 45 },
            new TimelineSegmentDto { State = MachineOperationalState.Alarm, DurationMinutes = 10 },
            new TimelineSegmentDto { State = MachineOperationalState.Emergency, DurationMinutes = 5 },
            new TimelineSegmentDto { State = MachineOperationalState.CommunicationInterrupted, DurationMinutes = 2 },
        };

        var result = _calculator.Calculate(segments);

        Assert.Equal(270, result.PowerOnMinutes);
        Assert.Equal(180, result.ProcessingMinutes);
        Assert.Equal(45, result.WaitingMinutes);
        Assert.Equal(30, result.StandbyMinutes);
        Assert.Equal(120, result.PowerOffMinutes);
        Assert.Equal(10, result.AlarmMinutes);
        Assert.Equal(5, result.EmergencyMinutes);
        Assert.Equal(2, result.CommunicationInterruptedMinutes);
        Assert.Equal(392, result.ObservedMinutes);
    }
}
