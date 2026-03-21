namespace DataCollector.Contracts;

public sealed class AgentMachineConfigurationDto
{
    public required string DeviceCode { get; set; }

    public required string IpAddress { get; set; }

    public int Port { get; set; }

    public required string Protocol { get; set; }

    public int TimeoutSeconds { get; set; }

    public required IReadOnlyList<int> ProcessingOperationModes { get; set; }

    public required IReadOnlyList<int> WaitingOperationModes { get; set; }
}

public sealed class AgentRuntimeConfigurationDto
{
    public required string AgentNodeName { get; set; }

    public required string WorkshopCode { get; set; }

    public required IReadOnlyList<AgentMachineConfigurationDto> Machines { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
