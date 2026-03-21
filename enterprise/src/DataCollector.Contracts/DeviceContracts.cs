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

    public MachineOperationalState CurrentState { get; set; }

    public DeviceHealthLevel HealthLevel { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset LastHeartbeatAt { get; set; }

    public string? CurrentProgramNo { get; set; }

    public string? CurrentProgramName { get; set; }
}

public sealed class DeviceUpsertRequest
{
    public Guid? DeviceId { get; set; }

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

    public bool IsEnabled { get; set; }
}

public sealed class DeviceManagementOverviewDto
{
    public required IReadOnlyList<WorkshopSummaryDto> Workshops { get; set; }

    public required IReadOnlyList<DeviceDto> Devices { get; set; }

    public DateTimeOffset SnapshotAt { get; set; }
}
