using DataCollector.Contracts;

namespace DataCollector.Server.Api.Services;

public interface IRealtimeIngestionService
{
    Task<MachineRealtimeIngestionResultDto> IngestAsync(MachineRealtimeBatchDto batch, CancellationToken cancellationToken);
}
