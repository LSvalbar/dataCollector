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

        if (_sessions.Count == 0)
        {
            _logger.LogWarning("Agent 当前没有启用任何机床配置，请先编辑 appsettings.json 中的 Agent:Machines。");
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
                var result = await _ingestionClient.PushAsync(snapshots, stoppingToken);
                _logger.LogInformation(
                    "Pushed realtime snapshots to {UploadEndpoint}, accepted {Accepted}/{Count}, processed at {ProcessedAt}",
                    _options.UploadEndpoint,
                    result.AcceptedSnapshots,
                    snapshots.Count,
                    result.ProcessedAt);

                if (result.UnknownDeviceCodes.Count > 0)
                {
                    _logger.LogWarning(
                        "以下设备编码在服务端不存在，已忽略：{DeviceCodes}。请检查客户端设备编码和 Agent 配置的 DeviceCode 是否完全一致。",
                        string.Join(", ", result.UnknownDeviceCodes));
                }

                if (result.AgentNodeMismatchDeviceCodes.Count > 0)
                {
                    _logger.LogWarning(
                        "以下设备编码的 Agent 节点不匹配，已忽略：{DeviceCodes}。请检查服务端设备档案中的 Agent 节点和当前 AgentNodeName 是否一致。",
                        string.Join(", ", result.AgentNodeMismatchDeviceCodes));
                }

                if (result.DisabledDeviceCodes.Count > 0)
                {
                    _logger.LogWarning(
                        "以下设备在服务端被禁用，实时快照未入库：{DeviceCodes}。",
                        string.Join(", ", result.DisabledDeviceCodes));
                }
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
