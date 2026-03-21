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

    public async Task PushAsync(IReadOnlyList<MachineRealtimeSnapshotDto> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var response = await _httpClient.PostAsJsonAsync(
            _options.UploadEndpoint,
            new MachineRealtimeBatchDto
            {
                AgentNodeName = _options.AgentNodeName,
                WorkshopCode = _options.WorkshopCode,
                Snapshots = snapshots,
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
