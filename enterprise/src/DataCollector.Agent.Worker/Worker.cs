using Microsoft.Extensions.Options;

namespace DataCollector.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _options;

    public Worker(ILogger<Worker> logger, IOptions<AgentOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Agent {AgentNodeName} 已启动，车间 {WorkshopCode}，设备数量 {MachineCount}，轮询周期 {PollInterval}ms，本地缓存 {CachePath}",
            _options.AgentNodeName,
            _options.WorkshopCode,
            _options.Machines.Count,
            _options.PollIntervalMilliseconds,
            _options.LocalCachePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var machine in _options.Machines)
            {
                _logger.LogInformation(
                    "计划采集设备 {DeviceCode} -> {IpAddress}:{Port} ({Protocol})，流程：原生 CNC 采集 -> 本地缓存 -> 幂等上传。",
                    machine.DeviceCode,
                    machine.IpAddress,
                    machine.Port,
                    machine.Protocol);
            }

            _logger.LogInformation(
                "Agent 上传周期 {UploadIntervalSeconds}s，设计职责：断线补传、质量标记、与中央服务对齐公式和主数据。",
                _options.UploadIntervalSeconds);

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(_options.UploadIntervalSeconds, 5)), stoppingToken);
        }
    }
}
