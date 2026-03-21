namespace DataCollector.Contracts;

public sealed class FormulaDefinitionDto
{
    public required string Code { get; set; }

    public required string DisplayName { get; set; }

    public required string Description { get; set; }

    public required string Expression { get; set; }

    public required string PrimaryVariable { get; set; }

    public double StandardWorkHours { get; set; }

    public double Coefficient { get; set; }

    public required IReadOnlyList<string> VisibleOptions { get; set; }

    public required string ResultUnit { get; set; }

    public required string UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FormulaUpdateRequest
{
    public required string Expression { get; set; }

    public string? PrimaryVariable { get; set; }

    public double? StandardWorkHours { get; set; }

    public double? Coefficient { get; set; }

    public IReadOnlyList<string>? VisibleOptions { get; set; }

    public required string UpdatedBy { get; set; }
}

public sealed class FormulaVariableOptionDto
{
    public required string VariableName { get; set; }

    public required string DisplayName { get; set; }
}

public sealed class DailyReportRowDto
{
    public Guid DeviceId { get; set; }

    public required string WorkshopName { get; set; }

    public required string DeviceCode { get; set; }

    public required string DeviceName { get; set; }

    public DateOnly ReportDate { get; set; }

    public double PowerOnMinutes { get; set; }

    public double ProcessingMinutes { get; set; }

    public double WaitingMinutes { get; set; }

    public double StandbyMinutes { get; set; }

    public double PowerOffMinutes { get; set; }

    public double AlarmMinutes { get; set; }

    public double EmergencyMinutes { get; set; }

    public double CommunicationInterruptedMinutes { get; set; }

    public double PowerOnRate { get; set; }

    public double UtilizationRate { get; set; }

    public MachineOperationalState CurrentState { get; set; }

    public string DataQualityCode { get; set; } = "native_preferred";
}

public sealed class DailyReportResponse
{
    public DateOnly ReportDate { get; set; }

    public required IReadOnlyList<FormulaDefinitionDto> Formulas { get; set; }

    public required IReadOnlyList<DailyReportRowDto> Rows { get; set; }

    public DateTimeOffset SnapshotAt { get; set; }
}

public sealed class TimelineSegmentDto
{
    public MachineOperationalState State { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public double DurationMinutes { get; set; }

    public int DurationSeconds { get; set; }

    public string DataQualityCode { get; set; } = "native_preferred";
}

public sealed class DeviceTimelineResponse
{
    public Guid DeviceId { get; set; }

    public required string DeviceCode { get; set; }

    public required string DeviceName { get; set; }

    public required string WorkshopName { get; set; }

    public DateOnly ReportDate { get; set; }

    public required IReadOnlyList<TimelineSegmentDto> Segments { get; set; }

    public required IReadOnlyDictionary<string, double> DailyTotals { get; set; }

    public DateTimeOffset SnapshotAt { get; set; }
}
