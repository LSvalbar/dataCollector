namespace DataCollector.Agent.Worker;

public sealed class AgentOptions
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5180";

    public string AgentNodeName { get; set; } = "W01-Agent";

    public string WorkshopCode { get; set; } = "W01";

    public string LocalCachePath { get; set; } = "C:\\DataCollector\\AgentCache";

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public int UploadIntervalSeconds { get; set; } = 5;

    public int ConfigurationRefreshSeconds { get; set; } = 15;

    public List<MachineEndpointOptions> Machines { get; set; } = [];

    public string GetUploadEndpoint()
    {
        return $"{ServerBaseUrl.TrimEnd('/')}/api/ingestion/snapshots";
    }

    public string GetRuntimeConfigurationEndpoint()
    {
        return $"{ServerBaseUrl.TrimEnd('/')}/api/agent/runtime-config/{Uri.EscapeDataString(AgentNodeName)}";
    }
}

public sealed class MachineEndpointOptions
{
    public bool Enabled { get; set; } = true;

    public string DeviceCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 8193;

    public string Protocol { get; set; } = "FOCAS over Ethernet";

    public int TimeoutSeconds { get; set; } = 10;

    public List<int> ProcessingOperationModes { get; set; } = [3];

    public List<int> WaitingOperationModes { get; set; } = [1, 2];
}
