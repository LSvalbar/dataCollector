using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/timeline")]
public sealed class TimelineController : ControllerBase
{
    private readonly IEnterprisePlatformService _platformService;

    public TimelineController(IEnterprisePlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet("devices/{deviceId:guid}")]
    public Task<DeviceTimelineResponse> GetDeviceTimeline(Guid deviceId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var reportDate = date ?? DateOnly.FromDateTime(DateTime.Now);
        return _platformService.GetDeviceTimelineAsync(deviceId, reportDate, cancellationToken);
    }
}
