using DataCollector.Agent.Worker.Focas;
using DataCollector.Contracts;
using Microsoft.Extensions.Options;

namespace DataCollector.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _options;
    private readonly Dictionary<string, FanucMachineSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private RealtimeIngestionClient? _ingestionClient;
    private AgentConfigurationClient? _configurationClient;
    private DateTimeOffset _lastConfigurationRefreshAt = DateTimeOffset.MinValue;

    public Worker(ILogger<Worker> logger, IOptions<AgentOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ingestionClient = new RealtimeIngestionClient(_options);
        _configurationClient = new AgentConfigurationClient(_options);

        await RefreshRuntimeConfigurationAsync(force: true, stoppingToken);

        _logger.LogInformation(
            "Agent {AgentNodeName} 已启动，服务端 {ServerBaseUrl}，轮询周期 {PollInterval}ms，本地缓存 {CachePath}",
            _options.AgentNodeName,
            _options.ServerBaseUrl,
            _options.PollIntervalMilliseconds,
            _options.LocalCachePath);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(_options.PollIntervalMilliseconds, 500)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshRuntimeConfigurationAsync(force: false, stoppingToken);

            var sessions = _sessions.Values.ToList();
            if (sessions.Count == 0)
            {
                continue;
            }

            var snapshots = new List<MachineRealtimeSnapshotDto>(sessions.Count);
            foreach (var session in sessions)
            {
                var snapshot = session.Collect();
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            if (snapshots.Count == 0)
            {
                continue;
            }

            try
            {
                var result = await _ingestionClient.PushAsync(snapshots, stoppingToken);
                _logger.LogDebug(
                    "Pushed realtime snapshots to {UploadEndpoint}, accepted {Accepted}/{Count}, processed at {ProcessedAt}",
                    _options.GetUploadEndpoint(),
                    result.AcceptedSnapshots,
                    snapshots.Count,
                    result.ProcessedAt);

                if (result.UnknownDeviceCodes.Count > 0)
                {
                    _logger.LogWarning(
                        "以下设备编码在服务端不存在，已忽略：{DeviceCodes}。请检查客户端设备编码和 Agent 配置是否一致。",
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
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _configurationGate.Dispose();
        base.Dispose();
    }

    private async Task RefreshRuntimeConfigurationAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force)
        {
            var refreshInterval = TimeSpan.FromSeconds(Math.Max(_options.ConfigurationRefreshSeconds, 5));
            if (DateTimeOffset.Now - _lastConfigurationRefreshAt < refreshInterval)
            {
                return;
            }
        }

        if (!await _configurationGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            _lastConfigurationRefreshAt = DateTimeOffset.Now;
            var configuration = await TryLoadRuntimeConfigurationAsync(cancellationToken);
            ApplyRuntimeConfiguration(configuration.Machines);

            if (_sessions.Count == 0)
            {
                _logger.LogWarning("Agent 当前没有启用任何机床配置，请先在客户端为节点 {AgentNodeName} 绑定设备。", _options.AgentNodeName);
            }
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    private async Task<AgentRuntimeConfigurationDto> TryLoadRuntimeConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await _configurationClient!.GetRuntimeConfigurationAsync(cancellationToken);
            _logger.LogDebug(
                "Loaded runtime configuration for {AgentNodeName}, machine count {MachineCount}, generated at {GeneratedAt}",
                configuration.AgentNodeName,
                configuration.Machines.Count,
                configuration.GeneratedAt);
            return configuration;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Load runtime configuration from server failed, fallback to local appsettings.json machines");

            return new AgentRuntimeConfigurationDto
            {
                AgentNodeName = _options.AgentNodeName,
                WorkshopCode = _options.WorkshopCode,
                Machines = _options.Machines
                    .Where(machine => machine.Enabled)
                    .Select(ToRuntimeMachine)
                    .ToArray(),
                GeneratedAt = DateTimeOffset.Now,
            };
        }
    }

    private void ApplyRuntimeConfiguration(IReadOnlyList<AgentMachineConfigurationDto> machines)
    {
        var machineMap = machines
            .GroupBy(machine => machine.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(machine => machine.DeviceCode, StringComparer.OrdinalIgnoreCase);

        foreach (var existingCode in _sessions.Keys.ToList())
        {
            if (machineMap.ContainsKey(existingCode))
            {
                continue;
            }

            _sessions[existingCode].Dispose();
            _sessions.Remove(existingCode);
            _logger.LogInformation("Removed runtime session for {DeviceCode}", existingCode);
        }

        foreach (var machine in machineMap.Values)
        {
            if (_sessions.TryGetValue(machine.DeviceCode, out var existingSession))
            {
                if (!NeedsRecreate(existingSession.Endpoint, machine))
                {
                    continue;
                }

                existingSession.Dispose();
                _sessions.Remove(machine.DeviceCode);
            }

            var options = ToEndpointOptions(machine);
            _sessions[machine.DeviceCode] = new FanucMachineSession(
                options,
                _logger,
                TimeSpan.FromSeconds(Math.Max(_options.TransientFailureToleranceSeconds, 0)));
            _logger.LogInformation("Configured runtime session for {DeviceCode} -> {IpAddress}:{Port}", machine.DeviceCode, machine.IpAddress, machine.Port);
        }
    }

    private static bool NeedsRecreate(MachineEndpointOptions current, AgentMachineConfigurationDto incoming)
    {
        return !current.DeviceCode.Equals(incoming.DeviceCode, StringComparison.OrdinalIgnoreCase) ||
               !current.IpAddress.Equals(incoming.IpAddress, StringComparison.OrdinalIgnoreCase) ||
               current.Port != incoming.Port ||
               current.TimeoutSeconds != incoming.TimeoutSeconds ||
               !current.ProcessingOperationModes.SequenceEqual(incoming.ProcessingOperationModes) ||
               !current.WaitingOperationModes.SequenceEqual(incoming.WaitingOperationModes);
    }

    private static MachineEndpointOptions ToEndpointOptions(AgentMachineConfigurationDto machine)
    {
        return new MachineEndpointOptions
        {
            Enabled = true,
            DeviceCode = machine.DeviceCode,
            IpAddress = machine.IpAddress,
            Port = machine.Port,
            Protocol = machine.Protocol,
            TimeoutSeconds = machine.TimeoutSeconds,
            ProcessingOperationModes = machine.ProcessingOperationModes.ToList(),
            WaitingOperationModes = machine.WaitingOperationModes.ToList(),
        };
    }

    private static AgentMachineConfigurationDto ToRuntimeMachine(MachineEndpointOptions machine)
    {
        return new AgentMachineConfigurationDto
        {
            DeviceCode = machine.DeviceCode,
            IpAddress = machine.IpAddress,
            Port = machine.Port,
            Protocol = machine.Protocol,
            TimeoutSeconds = machine.TimeoutSeconds,
            ProcessingOperationModes = machine.ProcessingOperationModes.ToArray(),
            WaitingOperationModes = machine.WaitingOperationModes.ToArray(),
        };
    }
}
