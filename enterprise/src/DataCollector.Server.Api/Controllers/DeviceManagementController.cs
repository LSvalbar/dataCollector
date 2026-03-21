using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/device-management")]
public sealed class DeviceManagementController : ControllerBase
{
    private readonly IEnterprisePlatformService _platformService;

    public DeviceManagementController(IEnterprisePlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet("overview")]
    public Task<DeviceManagementOverviewDto> GetOverview(CancellationToken cancellationToken)
    {
        return _platformService.GetDeviceManagementOverviewAsync(cancellationToken);
    }

    [HttpPost("devices")]
    public Task<DeviceDto> CreateDevice([FromBody] DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        return _platformService.SaveDeviceAsync(request, cancellationToken);
    }

    [HttpPut("devices/{deviceId:guid}")]
    public Task<DeviceDto> UpdateDevice(Guid deviceId, [FromBody] DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        request.DeviceId = deviceId;
        return _platformService.SaveDeviceAsync(request, cancellationToken);
    }

    [HttpDelete("devices/{deviceId:guid}")]
    public async Task<IActionResult> DeleteDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        await _platformService.DeleteDeviceAsync(deviceId, cancellationToken);
        return NoContent();
    }
}
