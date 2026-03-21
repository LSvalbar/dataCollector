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

    [HttpPost("users")]
    public async Task<ActionResult<UserDto>> SaveUser([FromBody] UserUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _platformService.SaveUserAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpDelete("users/{userCode}")]
    public async Task<IActionResult> DeleteUser(string userCode, CancellationToken cancellationToken)
    {
        await _platformService.DeleteUserAsync(userCode, cancellationToken);
        return NoContent();
    }

    [HttpPost("roles")]
    public async Task<ActionResult<RoleDto>> SaveRole([FromBody] RoleUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _platformService.SaveRoleAsync(request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpDelete("roles/{roleCode}")]
    public async Task<IActionResult> DeleteRole(string roleCode, CancellationToken cancellationToken)
    {
        try
        {
            await _platformService.DeleteRoleAsync(roleCode, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }
}
