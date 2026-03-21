namespace DataCollector.Contracts;

public sealed class WorkshopSummaryDto
{
    public required string WorkshopCode { get; set; }

    public required string WorkshopName { get; set; }

    public int MachineCount { get; set; }

    public int ProcessingCount { get; set; }

    public int WaitingCount { get; set; }

    public int StandbyCount { get; set; }

    public int AlarmCount { get; set; }

    public int EmergencyCount { get; set; }

    public int PowerOffCount { get; set; }

    public int CommunicationInterruptedCount { get; set; }
}

public sealed class DeviceDto
{
    public Guid DeviceId { get; set; }

    public required string DepartmentCode { get; set; }

    public required string DepartmentName { get; set; }

    public required string WorkshopCode { get; set; }

    public required string WorkshopName { get; set; }

    public required string DeviceCode { get; set; }

    public required string DeviceName { get; set; }

    public required string Manufacturer { get; set; }

    public required string ControllerModel { get; set; }

    public required string ProtocolName { get; set; }

    public required string IpAddress { get; set; }

    public int Port { get; set; }

    public required string AgentNodeName { get; set; }

    public string? ResponsiblePerson { get; set; }

    public MachineOperationalState CurrentState { get; set; }

    public DeviceHealthLevel HealthLevel { get; set; }

    public bool IsEnabled { get; set; }

    public bool MachineOnline { get; set; }

    public DateTimeOffset LastHeartbeatAt { get; set; }

    public DateTimeOffset? LastCollectedAt { get; set; }

    public string? CurrentProgramNo { get; set; }

    public string? CurrentProgramName { get; set; }

    public int? SpindleSpeedRpm { get; set; }

    public double? SpindleLoadPercent { get; set; }

    public int AutomaticMode { get; set; }

    public int OperationMode { get; set; }

    public bool AlarmState { get; set; }

    public bool EmergencyState { get; set; }

    public string? ControllerModeText { get; set; }

    public string? OeeStatusText { get; set; }

    public long? NativePowerOnTotalMs { get; set; }

    public long? NativeOperatingTotalMs { get; set; }

    public long? NativeCuttingTotalMs { get; set; }

    public long? NativeFreeTotalMs { get; set; }

    public string? DataQualityCode { get; set; }

    public string? LastCollectionError { get; set; }
}

public sealed class DeviceUpsertRequest
{
    public Guid? DeviceId { get; set; }

    public required string DepartmentCode { get; set; }

    public required string DepartmentName { get; set; }

    public required string WorkshopCode { get; set; }

    public required string WorkshopName { get; set; }

    public required string DeviceCode { get; set; }

    public required string DeviceName { get; set; }

    public required string Manufacturer { get; set; }

    public required string ControllerModel { get; set; }

    public required string ProtocolName { get; set; }

    public required string IpAddress { get; set; }

    public int Port { get; set; }

    public required string AgentNodeName { get; set; }

    public string? ResponsiblePerson { get; set; }

    public bool IsEnabled { get; set; }
}

public sealed class DeviceManagementOverviewDto
{
    public required IReadOnlyList<WorkshopSummaryDto> Workshops { get; set; }

    public required IReadOnlyList<DeviceDto> Devices { get; set; }

    public DateTimeOffset SnapshotAt { get; set; }
}

public sealed class NameChangeRequest
{
    public required string NewName { get; set; }
}
