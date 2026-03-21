namespace DataCollector.Contracts;

public sealed class MachineRealtimeSnapshotDto
{
    public required string DeviceCode { get; set; }

    public DateTimeOffset CollectedAt { get; set; }

    public bool MachineOnline { get; set; }

    public MachineOperationalState CurrentState { get; set; }

    public int AutomaticMode { get; set; }

    public int OperationMode { get; set; }

    public bool EmergencyState { get; set; }

    public bool AlarmState { get; set; }

    public string? ControllerModeText { get; set; }

    public string? OeeStatusText { get; set; }

    public int? SpindleSpeedRpm { get; set; }

    public double? SpindleLoadPercent { get; set; }

    public string? CurrentProgramNo { get; set; }

    public string? CurrentProgramName { get; set; }

    public long? NativePowerOnTotalMs { get; set; }

    public long? NativeOperatingTotalMs { get; set; }

    public long? NativeCuttingTotalMs { get; set; }

    public long? NativeFreeTotalMs { get; set; }

    public string DataQualityCode { get; set; } = "realtime_session";

    public string? ErrorMessage { get; set; }
}

public sealed class MachineRealtimeBatchDto
{
    public required string AgentNodeName { get; set; }

    public required string WorkshopCode { get; set; }

    public required IReadOnlyList<MachineRealtimeSnapshotDto> Snapshots { get; set; }
}
