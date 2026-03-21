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
        using var response = await _httpClient.GetAsync(_options.GetRuntimeConfigurationEndpoint(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"加载 Agent 运行配置失败：{(string.IsNullOrWhiteSpace(message) ? response.ReasonPhrase : message.Trim())}");
        }

        var configuration = await response.Content.ReadFromJsonAsync<AgentRuntimeConfigurationDto>(cancellationToken);

        return configuration ?? new AgentRuntimeConfigurationDto
        {
            AgentNodeName = _options.AgentNodeName,
            WorkshopCode = _options.WorkshopCode,
            Machines = [],
            GeneratedAt = DateTimeOffset.Now,
        };
    }
}
