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
    public Task<DailyReportResponse> GetDailyReport([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var reportDate = date ?? DateOnly.FromDateTime(DateTime.Now);
        return _platformService.GetDailyReportAsync(reportDate, cancellationToken);
    }

    [HttpGet("formulas")]
    public Task<IReadOnlyList<FormulaDefinitionDto>> GetFormulas(CancellationToken cancellationToken)
    {
        return _platformService.GetFormulasAsync(cancellationToken);
    }

    [HttpGet("formula-options")]
    public Task<IReadOnlyList<FormulaVariableOptionDto>> GetFormulaOptions(CancellationToken cancellationToken)
    {
        return _platformService.GetFormulaVariableOptionsAsync(cancellationToken);
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
