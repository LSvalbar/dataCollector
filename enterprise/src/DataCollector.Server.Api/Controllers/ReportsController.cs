using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IEnterprisePlatformService _platformService;

    public ReportsController(IEnterprisePlatformService platformService)
    {
        _platformService = platformService;
    }

    [HttpGet("daily")]
    public async Task<ActionResult<DailyReportResponse>> GetDailyReport([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var reportDate = date ?? DateOnly.FromDateTime(DateTime.Now);
        try
        {
            return await _platformService.GetDailyReportAsync(reportDate, cancellationToken);
        }
        catch (Exception exception)
        {
            return Problem(title: "报表刷新失败", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("formulas")]
    public async Task<ActionResult<IReadOnlyList<FormulaDefinitionDto>>> GetFormulas(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _platformService.GetFormulasAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            return Problem(title: "公式查询失败", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("formula-options")]
    public async Task<ActionResult<IReadOnlyList<FormulaVariableOptionDto>>> GetFormulaOptions(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _platformService.GetFormulaVariableOptionsAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            return Problem(title: "公式选项查询失败", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("formulas/{code}")]
    public async Task<ActionResult<FormulaDefinitionDto>> UpdateFormula(string code, [FromBody] FormulaUpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _platformService.UpdateFormulaAsync(code, request, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }
}
