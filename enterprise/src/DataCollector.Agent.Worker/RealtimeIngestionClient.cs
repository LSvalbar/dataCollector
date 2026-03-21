using System.Net.Http.Json;
using DataCollector.Contracts;

namespace DataCollector.Agent.Worker;

internal sealed class RealtimeIngestionClient
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AgentOptions _options;

    public RealtimeIngestionClient(AgentOptions options)
    {
        _options = options;
    }

    public async Task<MachineRealtimeIngestionResultDto> PushAsync(IReadOnlyList<MachineRealtimeSnapshotDto> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return new MachineRealtimeIngestionResultDto
            {
                AcceptedSnapshots = 0,
                UnknownDeviceCodes = [],
                AgentNodeMismatchDeviceCodes = [],
                DisabledDeviceCodes = [],
                ProcessedAt = DateTimeOffset.Now,
            };
        }

        var response = await _httpClient.PostAsJsonAsync(
            _options.GetUploadEndpoint(),
            new MachineRealtimeBatchDto
            {
                AgentNodeName = _options.AgentNodeName,
                WorkshopCode = _options.WorkshopCode,
                Snapshots = snapshots,
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MachineRealtimeIngestionResultDto>(cancellationToken)
            ?? new MachineRealtimeIngestionResultDto
            {
                AcceptedSnapshots = snapshots.Count,
                UnknownDeviceCodes = [],
                AgentNodeMismatchDeviceCodes = [],
                DisabledDeviceCodes = [],
                ProcessedAt = DateTimeOffset.Now,
            };
    }
}
