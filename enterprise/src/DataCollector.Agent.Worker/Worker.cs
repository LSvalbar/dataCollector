using DataCollector.Agent.Worker.Focas;
using DataCollector.Contracts;
using Microsoft.Extensions.Options;

namespace DataCollector.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _options;
    private readonly List<FanucMachineSession> _sessions = [];
    private RealtimeIngestionClient? _ingestionClient;

    public Worker(ILogger<Worker> logger, IOptions<AgentOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ingestionClient = new RealtimeIngestionClient(_options);

        foreach (var machine in _options.Machines.Where(machine => machine.Enabled))
        {
            _sessions.Add(new FanucMachineSession(machine, _logger));
        }

        _logger.LogInformation(
            "Agent {AgentNodeName} 已启动，车间 {WorkshopCode}，设备数量 {MachineCount}，轮询周期 {PollInterval}ms，本地缓存 {CachePath}",
            _options.AgentNodeName,
            _options.WorkshopCode,
            _sessions.Count,
            _options.PollIntervalMilliseconds,
            _options.LocalCachePath);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(_options.PollIntervalMilliseconds, 500)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var snapshots = new List<MachineRealtimeSnapshotDto>();
            foreach (var session in _sessions)
            {
                snapshots.Add(session.Collect());
            }

            try
            {
                await _ingestionClient.PushAsync(snapshots, stoppingToken);
                _logger.LogInformation("Pushed {Count} realtime snapshots to {UploadEndpoint}", snapshots.Count, _options.UploadEndpoint);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Push realtime snapshots failed");
            }
        }
    }

    public override void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        base.Dispose();
    }
}
