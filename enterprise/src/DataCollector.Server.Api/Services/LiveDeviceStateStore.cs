using DataCollector.Contracts;

namespace DataCollector.Server.Api.Services;

public sealed class LiveDeviceStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MachineRealtimeSnapshotDto> _latestSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<DateOnly, List<TimelineSegmentDto>>> _timelines = new(StringComparer.OrdinalIgnoreCase);

    public void Ingest(MachineRealtimeBatchDto batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        lock (_gate)
        {
            foreach (var snapshot in batch.Snapshots)
            {
                IngestSnapshot(snapshot);
            }
        }
    }

    public bool TryGetLatest(string deviceCode, out MachineRealtimeSnapshotDto snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);

        lock (_gate)
        {
            if (_latestSnapshots.TryGetValue(deviceCode, out var found))
            {
                snapshot = CloneSnapshot(found);
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    public IReadOnlyList<TimelineSegmentDto> GetTimeline(string deviceCode, DateOnly reportDate, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);

        lock (_gate)
        {
            if (!_timelines.TryGetValue(deviceCode, out var byDate) || !byDate.TryGetValue(reportDate, out var segments))
            {
                return [];
            }

            var cloned = segments.Select(CloneSegment).ToList();
            if (reportDate == DateOnly.FromDateTime(now.LocalDateTime) && cloned.Count > 0)
            {
                var last = cloned[^1];
                if (now > last.EndAt)
                {
                    last.EndAt = now;
                    last.DurationMinutes = Math.Round((last.EndAt - last.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
                }
            }

            return cloned;
        }
    }

    public bool HasTimeline(string deviceCode, DateOnly reportDate)
    {
        lock (_gate)
        {
            return _timelines.TryGetValue(deviceCode, out var byDate) && byDate.ContainsKey(reportDate);
        }
    }

    private void IngestSnapshot(MachineRealtimeSnapshotDto snapshot)
    {
        var code = snapshot.DeviceCode.Trim();
        var normalized = CloneSnapshot(snapshot);
        _latestSnapshots[code] = normalized;

        var reportDate = DateOnly.FromDateTime(snapshot.CollectedAt.LocalDateTime);
        if (!_timelines.TryGetValue(code, out var byDate))
        {
            byDate = new Dictionary<DateOnly, List<TimelineSegmentDto>>();
            _timelines[code] = byDate;
        }

        if (!byDate.TryGetValue(reportDate, out var segments))
        {
            segments = [];
            byDate[reportDate] = segments;
        }

        if (segments.Count == 0)
        {
            segments.Add(CreateSegment(snapshot.CurrentState, snapshot.CollectedAt, snapshot.DataQualityCode));
            return;
        }

        var last = segments[^1];
        if (snapshot.CollectedAt < last.StartAt)
        {
            return;
        }

        if (last.State == snapshot.CurrentState && last.DataQualityCode == snapshot.DataQualityCode)
        {
            if (snapshot.CollectedAt > last.EndAt)
            {
                last.EndAt = snapshot.CollectedAt;
                last.DurationMinutes = Math.Round((last.EndAt - last.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
            }

            return;
        }

        if (snapshot.CollectedAt > last.EndAt)
        {
            last.EndAt = snapshot.CollectedAt;
            last.DurationMinutes = Math.Round((last.EndAt - last.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
        }

        segments.Add(CreateSegment(snapshot.CurrentState, snapshot.CollectedAt, snapshot.DataQualityCode));
    }

    private static TimelineSegmentDto CreateSegment(MachineOperationalState state, DateTimeOffset timestamp, string dataQualityCode)
    {
        return new TimelineSegmentDto
        {
            State = state,
            StartAt = timestamp,
            EndAt = timestamp,
            DurationMinutes = 0,
            DataQualityCode = dataQualityCode,
        };
    }

    private static MachineRealtimeSnapshotDto CloneSnapshot(MachineRealtimeSnapshotDto source)
    {
        return new MachineRealtimeSnapshotDto
        {
            DeviceCode = source.DeviceCode,
            CollectedAt = source.CollectedAt,
            MachineOnline = source.MachineOnline,
            CurrentState = source.CurrentState,
            AutomaticMode = source.AutomaticMode,
            OperationMode = source.OperationMode,
            EmergencyState = source.EmergencyState,
            AlarmState = source.AlarmState,
            ControllerModeText = source.ControllerModeText,
            OeeStatusText = source.OeeStatusText,
            SpindleSpeedRpm = source.SpindleSpeedRpm,
            SpindleLoadPercent = source.SpindleLoadPercent,
            CurrentProgramNo = source.CurrentProgramNo,
            CurrentProgramName = source.CurrentProgramName,
            NativePowerOnTotalMs = source.NativePowerOnTotalMs,
            NativeOperatingTotalMs = source.NativeOperatingTotalMs,
            NativeCuttingTotalMs = source.NativeCuttingTotalMs,
            NativeFreeTotalMs = source.NativeFreeTotalMs,
            DataQualityCode = source.DataQualityCode,
            ErrorMessage = source.ErrorMessage,
        };
    }

    private static TimelineSegmentDto CloneSegment(TimelineSegmentDto source)
    {
        return new TimelineSegmentDto
        {
            State = source.State,
            StartAt = source.StartAt,
            EndAt = source.EndAt,
            DurationMinutes = source.DurationMinutes,
            DataQualityCode = source.DataQualityCode,
        };
    }
}
