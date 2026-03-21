using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/ingestion")]
public sealed class IngestionController : ControllerBase
{
    private readonly IRealtimeIngestionService _realtimeIngestionService;

    public IngestionController(IRealtimeIngestionService realtimeIngestionService)
    {
        _realtimeIngestionService = realtimeIngestionService;
    }

    [HttpPost("snapshots")]
    public async Task<ActionResult<MachineRealtimeIngestionResultDto>> IngestSnapshots([FromBody] MachineRealtimeBatchDto batch, CancellationToken cancellationToken)
    {
        var result = await _realtimeIngestionService.IngestAsync(batch, cancellationToken);
        return Accepted(result);
    }
}
