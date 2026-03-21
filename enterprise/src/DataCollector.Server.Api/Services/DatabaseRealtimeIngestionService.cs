using DataCollector.Contracts;
using DataCollector.Server.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Services;

public sealed class DatabaseRealtimeIngestionService : IRealtimeIngestionService
{
    private readonly IDbContextFactory<EnterpriseDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;

    public DatabaseRealtimeIngestionService(
        IDbContextFactory<EnterpriseDbContext> dbContextFactory,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<MachineRealtimeIngestionResultDto> IngestAsync(MachineRealtimeBatchDto batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshotCodes = batch.Snapshots
            .Select(snapshot => snapshot.DeviceCode.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var devices = await dbContext.Devices
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

            ApplySnapshot(device, snapshot);
            await UpsertTimelineAsync(dbContext, device.DeviceId, snapshot, cancellationToken);
            acceptedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

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
        device.MachineOnline = snapshot.MachineOnline;
        device.CurrentState = snapshot.CurrentState;
        device.HealthLevel = snapshot.CurrentState switch
        {
            MachineOperationalState.Alarm or MachineOperationalState.Emergency => DeviceHealthLevel.Critical,
            MachineOperationalState.CommunicationInterrupted => DeviceHealthLevel.Warning,
            _ => DeviceHealthLevel.Normal,
        };
        device.LastCollectedAt = snapshot.CollectedAt;
        device.LastHeartbeatAt = snapshot.CollectedAt;
        device.CurrentProgramNo = snapshot.CurrentProgramNo;
        device.CurrentProgramName = snapshot.CurrentProgramName;
        device.SpindleSpeedRpm = snapshot.SpindleSpeedRpm;
        device.SpindleLoadPercent = snapshot.SpindleLoadPercent;
        device.AutomaticMode = snapshot.AutomaticMode;
        device.OperationMode = snapshot.OperationMode;
        device.AlarmState = snapshot.AlarmState;
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
            dbContext.TimelineSegments.Add(CreateSegment(deviceId, reportDateKey, snapshot.CurrentState, snapshot.CollectedAt, snapshot.DataQualityCode));
            return;
        }

        if (snapshot.CollectedAt < lastSegment.StartAt)
        {
            return;
        }

        if (lastSegment.State == snapshot.CurrentState && lastSegment.DataQualityCode == snapshot.DataQualityCode)
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

        dbContext.TimelineSegments.Add(CreateSegment(deviceId, reportDateKey, snapshot.CurrentState, snapshot.CollectedAt, snapshot.DataQualityCode));
    }

    private static TimelineSegmentEntity CreateSegment(
        Guid deviceId,
        int reportDateKey,
        MachineOperationalState state,
        DateTimeOffset timestamp,
        string dataQualityCode)
    {
        return new TimelineSegmentEntity
        {
            DeviceId = deviceId,
            ReportDateKey = reportDateKey,
            State = state,
            StartAt = timestamp,
            EndAt = timestamp,
            DurationMinutes = 0,
            DataQualityCode = dataQualityCode,
        };
    }

    private static int ToDateKey(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;
}
