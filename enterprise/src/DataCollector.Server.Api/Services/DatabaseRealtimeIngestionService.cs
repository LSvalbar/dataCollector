using DataCollector.Contracts;
using DataCollector.Server.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Services;

public sealed class DatabaseRealtimeIngestionService : IRealtimeIngestionService
{
    private readonly IDbContextFactory<EnterpriseDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DatabaseRealtimeIngestionService> _logger;

    public DatabaseRealtimeIngestionService(
        IDbContextFactory<EnterpriseDbContext> dbContextFactory,
        TimeProvider timeProvider,
        ILogger<DatabaseRealtimeIngestionService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MachineRealtimeIngestionResultDto> IngestAsync(MachineRealtimeBatchDto batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var lookupContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshotCodes = batch.Snapshots
            .Select(snapshot => snapshot.DeviceCode.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var devices = await lookupContext.Devices
            .AsNoTracking()
            .Where(device => snapshotCodes.Contains(device.DeviceCode))
            .ToDictionaryAsync(device => device.DeviceCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var unknownDeviceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var agentNodeMismatchDeviceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledDeviceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedCount = 0;

        foreach (var snapshot in batch.Snapshots.OrderBy(item => item.CollectedAt))
        {
            var deviceCode = snapshot.DeviceCode.Trim();
            if (string.IsNullOrWhiteSpace(deviceCode))
            {
                continue;
            }

            if (!devices.TryGetValue(deviceCode, out var device))
            {
                unknownDeviceCodes.Add(deviceCode);
                continue;
            }

            if (!device.AgentNodeName.Equals(batch.AgentNodeName, StringComparison.OrdinalIgnoreCase))
            {
                agentNodeMismatchDeviceCodes.Add(deviceCode);
                continue;
            }

            if (!device.IsEnabled)
            {
                disabledDeviceCodes.Add(deviceCode);
                continue;
            }

            try
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                var trackedDevice = await dbContext.Devices.FirstOrDefaultAsync(item => item.DeviceId == device.DeviceId, cancellationToken);
                if (trackedDevice is null)
                {
                    unknownDeviceCodes.Add(deviceCode);
                    continue;
                }

                ApplySnapshot(trackedDevice, snapshot);
                await UpsertTimelineAsync(dbContext, trackedDevice.DeviceId, snapshot, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                acceptedCount++;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to ingest realtime snapshot for {DeviceCode}", deviceCode);
            }
        }

        return new MachineRealtimeIngestionResultDto
        {
            AcceptedSnapshots = acceptedCount,
            UnknownDeviceCodes = unknownDeviceCodes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            AgentNodeMismatchDeviceCodes = agentNodeMismatchDeviceCodes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            DisabledDeviceCodes = disabledDeviceCodes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            ProcessedAt = _timeProvider.GetLocalNow(),
        };
    }

    private static void ApplySnapshot(DeviceEntity device, MachineRealtimeSnapshotDto snapshot)
    {
        var normalizedState = NormalizeState(snapshot.CurrentState);
        device.MachineOnline = snapshot.MachineOnline;
        device.CurrentState = normalizedState;
        device.HealthLevel = normalizedState switch
        {
            MachineOperationalState.Alarm or MachineOperationalState.Emergency => DeviceHealthLevel.Critical,
            MachineOperationalState.CommunicationInterrupted => DeviceHealthLevel.Warning,
            _ => DeviceHealthLevel.Normal,
        };
        device.LastCollectedAt = snapshot.CollectedAt;
        device.LastHeartbeatAt = snapshot.CollectedAt;
        device.CurrentProgramNo = snapshot.CurrentProgramNo;
        device.CurrentProgramName = snapshot.CurrentProgramName;
        device.CurrentDrawingNumber = snapshot.CurrentDrawingNumber;
        device.SpindleSpeedRpm = snapshot.SpindleSpeedRpm;
        device.SpindleLoadPercent = snapshot.SpindleLoadPercent;
        device.AutomaticMode = snapshot.AutomaticMode;
        device.OperationMode = snapshot.OperationMode;
        device.AlarmState = snapshot.AlarmState;
        device.CurrentAlarmNumber = snapshot.CurrentAlarmNumber;
        device.CurrentAlarmMessage = snapshot.CurrentAlarmMessage;
        device.EmergencyState = snapshot.EmergencyState;
        device.ControllerModeText = snapshot.ControllerModeText;
        device.OeeStatusText = snapshot.OeeStatusText;
        device.NativePowerOnTotalMs = snapshot.NativePowerOnTotalMs;
        device.NativeOperatingTotalMs = snapshot.NativeOperatingTotalMs;
        device.NativeCuttingTotalMs = snapshot.NativeCuttingTotalMs;
        device.NativeFreeTotalMs = snapshot.NativeFreeTotalMs;
        device.DataQualityCode = snapshot.DataQualityCode;
        device.LastCollectionError = snapshot.ErrorMessage;
    }

    private static async Task UpsertTimelineAsync(
        EnterpriseDbContext dbContext,
        Guid deviceId,
        MachineRealtimeSnapshotDto snapshot,
        CancellationToken cancellationToken)
    {
        var reportDateKey = ToDateKey(DateOnly.FromDateTime(snapshot.CollectedAt.LocalDateTime));
        var lastSegment = await dbContext.TimelineSegments
            .Where(segment => segment.DeviceId == deviceId && segment.ReportDateKey == reportDateKey)
            .OrderByDescending(segment => segment.TimelineSegmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastSegment is null)
        {
            dbContext.TimelineSegments.Add(CreateSegment(deviceId, reportDateKey, snapshot, NormalizeState(snapshot.CurrentState)));
            return;
        }

        if (snapshot.CollectedAt < lastSegment.StartAt)
        {
            return;
        }

        var normalizedState = NormalizeState(snapshot.CurrentState);
        if (lastSegment.State == normalizedState &&
            lastSegment.DataQualityCode == snapshot.DataQualityCode &&
            string.Equals(lastSegment.ProgramNo, snapshot.CurrentProgramNo, StringComparison.Ordinal) &&
            string.Equals(lastSegment.DrawingNumber, snapshot.CurrentDrawingNumber, StringComparison.Ordinal) &&
            lastSegment.AlarmNumber == snapshot.CurrentAlarmNumber &&
            string.Equals(lastSegment.AlarmMessage, snapshot.CurrentAlarmMessage, StringComparison.Ordinal))
        {
            if (snapshot.CollectedAt > lastSegment.EndAt)
            {
                lastSegment.EndAt = snapshot.CollectedAt;
                lastSegment.DurationMinutes = Math.Round((lastSegment.EndAt - lastSegment.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
            }

            return;
        }

        if (snapshot.CollectedAt > lastSegment.EndAt)
        {
            lastSegment.EndAt = snapshot.CollectedAt;
            lastSegment.DurationMinutes = Math.Round((lastSegment.EndAt - lastSegment.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
        }

        dbContext.TimelineSegments.Add(CreateSegment(deviceId, reportDateKey, snapshot, normalizedState));
    }

    private static TimelineSegmentEntity CreateSegment(Guid deviceId, int reportDateKey, MachineRealtimeSnapshotDto snapshot, MachineOperationalState state)
    {
        return new TimelineSegmentEntity
        {
            DeviceId = deviceId,
            ReportDateKey = reportDateKey,
            State = state,
            StartAt = snapshot.CollectedAt,
            EndAt = snapshot.CollectedAt,
            DurationMinutes = 0,
            DataQualityCode = snapshot.DataQualityCode,
            ProgramNo = snapshot.CurrentProgramNo,
            DrawingNumber = snapshot.CurrentDrawingNumber,
            AlarmNumber = snapshot.CurrentAlarmNumber,
            AlarmMessage = snapshot.CurrentAlarmMessage,
        };
    }

    private static MachineOperationalState NormalizeState(MachineOperationalState state)
    {
        return state == MachineOperationalState.Waiting ? MachineOperationalState.Standby : state;
    }

    private static int ToDateKey(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;
}
