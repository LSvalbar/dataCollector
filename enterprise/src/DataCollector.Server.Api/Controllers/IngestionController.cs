using DataCollector.Contracts;
using DataCollector.Server.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataCollector.Server.Api.Controllers;

[ApiController]
[Route("api/ingestion")]
public sealed class IngestionController : ControllerBase
{
    private readonly LiveDeviceStateStore _liveDeviceStateStore;

    public IngestionController(LiveDeviceStateStore liveDeviceStateStore)
    {
        _liveDeviceStateStore = liveDeviceStateStore;
    }

    [HttpPost("snapshots")]
    public IActionResult IngestSnapshots([FromBody] MachineRealtimeBatchDto batch)
    {
        _liveDeviceStateStore.Ingest(batch);
        return Accepted(new
        {
            accepted = batch.Snapshots.Count,
            timestamp = DateTimeOffset.Now,
        });
    }
}
