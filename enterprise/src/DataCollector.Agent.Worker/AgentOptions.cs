namespace DataCollector.Agent.Worker;

public sealed class AgentOptions
{
    public string AgentNodeName { get; set; } = "W01-Agent";

    public string WorkshopCode { get; set; } = "W01";

    public string LocalCachePath { get; set; } = "C:\\DataCollector\\AgentCache";

    public string UploadEndpoint { get; set; } = "http://localhost:5180/api/ingestion";

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public int UploadIntervalSeconds { get; set; } = 5;

    public List<MachineEndpointOptions> Machines { get; set; } = [];
}

public sealed class MachineEndpointOptions
{
    public string DeviceCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 8193;

    public string Protocol { get; set; } = "FOCAS over Ethernet";
}
