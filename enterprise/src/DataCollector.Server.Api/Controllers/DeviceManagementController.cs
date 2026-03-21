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
    public async Task<ActionResult<DeviceDto>> CreateDevice([FromBody] DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _platformService.SaveDeviceAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPut("devices/{deviceId:guid}")]
    public async Task<ActionResult<DeviceDto>> UpdateDevice(Guid deviceId, [FromBody] DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        request.DeviceId = deviceId;
        try
        {
            return await _platformService.SaveDeviceAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpDelete("devices/{deviceId:guid}")]
    public async Task<IActionResult> DeleteDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        await _platformService.DeleteDeviceAsync(deviceId, cancellationToken);
        return NoContent();
    }

    [HttpPut("departments/{departmentCode}/rename")]
    public async Task<IActionResult> RenameDepartment(string departmentCode, [FromBody] NameChangeRequest request, CancellationToken cancellationToken)
    {
        await _platformService.RenameDepartmentAsync(departmentCode, request.NewName, cancellationToken);
        return NoContent();
    }

    [HttpPut("workshops/{workshopCode}/rename")]
    public async Task<IActionResult> RenameWorkshop(string workshopCode, [FromBody] NameChangeRequest request, CancellationToken cancellationToken)
    {
        await _platformService.RenameWorkshopAsync(workshopCode, request.NewName, cancellationToken);
        return NoContent();
    }

    [HttpPut("devices/{deviceId:guid}/rename")]
    public async Task<IActionResult> RenameDevice(Guid deviceId, [FromBody] NameChangeRequest request, CancellationToken cancellationToken)
    {
        await _platformService.RenameDeviceAsync(deviceId, request.NewName, cancellationToken);
        return NoContent();
    }
}
