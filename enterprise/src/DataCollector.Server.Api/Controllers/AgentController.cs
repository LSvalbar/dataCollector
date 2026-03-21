using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController : ControllerBase
{
    private readonly IEnterprisePlatformService _platformService;

    public AgentController(IEnterprisePlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet("runtime-config/{agentNodeName}")]
    public Task<AgentRuntimeConfigurationDto> GetRuntimeConfiguration(string agentNodeName, CancellationToken cancellationToken)
    {
        return _platformService.GetAgentRuntimeConfigurationAsync(agentNodeName, cancellationToken);
    }
}
