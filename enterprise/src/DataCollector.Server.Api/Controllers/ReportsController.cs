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

    [HttpPut("formulas/{code}")]
    public Task<FormulaDefinitionDto> UpdateFormula(string code, [FromBody] FormulaUpdateRequest request, CancellationToken cancellationToken)
    {
        return _platformService.UpdateFormulaAsync(code, request, cancellationToken);
    }
}
