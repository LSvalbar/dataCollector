using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/security")]
public sealed class SecurityController : ControllerBase
{
    private readonly IEnterprisePlatformService _platformService;

    public SecurityController(IEnterprisePlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet("overview")]
    public Task<SecurityOverviewDto> GetOverview(CancellationToken cancellationToken)
    {
        return _platformService.GetSecurityOverviewAsync(cancellationToken);
    }
}
