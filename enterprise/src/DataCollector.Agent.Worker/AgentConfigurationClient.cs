using System.Net.Http.Json;
using DataCollector.Contracts;

namespace DataCollector.Agent.Worker;

internal sealed class AgentConfigurationClient
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly AgentOptions _options;

    public AgentConfigurationClient(AgentOptions options)
    {
        _options = options;
    }

    public async Task<AgentRuntimeConfigurationDto> GetRuntimeConfigurationAsync(CancellationToken cancellationToken)
    {
        var configuration = await _httpClient.GetFromJsonAsync<AgentRuntimeConfigurationDto>(
            _options.GetRuntimeConfigurationEndpoint(),
            cancellationToken);

        return configuration ?? new AgentRuntimeConfigurationDto
        {
            AgentNodeName = _options.AgentNodeName,
            WorkshopCode = _options.WorkshopCode,
            Machines = [],
            GeneratedAt = DateTimeOffset.Now,
        };
    }
}
