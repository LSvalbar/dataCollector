using DataCollector.Agent.Worker.Focas;
using DataCollector.Contracts;
using Microsoft.Extensions.Options;

namespace DataCollector.Agent.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _options;
    private readonly object _sessionGate = new();
    private readonly Dictionary<string, FanucMachineSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _activeCollectTasks = new(StringComparer.OrdinalIgnoreCase);
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

            List<FanucMachineSession> sessions;
            lock (_sessionGate)
            {
                sessions = _sessions.Values.ToList();
            }

            if (sessions.Count == 0)
            {
                continue;
            }

            foreach (var session in sessions)
            {
                var deviceCode = session.Endpoint.DeviceCode;
                lock (_sessionGate)
                {
                    if (_activeCollectTasks.TryGetValue(deviceCode, out var activeTask) && !activeTask.IsCompleted)
                    {
                        continue;
                    }

                    var collectTask = RunCollectAndPushAsync(session, stoppingToken);
                    _activeCollectTasks[deviceCode] = collectTask;
                    _ = collectTask.ContinueWith(
                        completedTask => CleanupCollectTask(deviceCode, completedTask),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }
    }

    public override void Dispose()
    {
        List<string> deviceCodes;
        lock (_sessionGate)
        {
            deviceCodes = _sessions.Keys.ToList();
        }

        foreach (var deviceCode in deviceCodes)
        {
            ScheduleSessionDisposal(deviceCode);
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

            var sessionCount = 0;
            lock (_sessionGate)
            {
                sessionCount = _sessions.Count;
            }

            if (sessionCount == 0)
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

        List<string> existingCodes;
        lock (_sessionGate)
        {
            existingCodes = _sessions.Keys.ToList();
        }

        foreach (var existingCode in existingCodes)
        {
            if (machineMap.ContainsKey(existingCode))
            {
                continue;
            }

            ScheduleSessionDisposal(existingCode);
            _logger.LogInformation("Removed runtime session for {DeviceCode}", existingCode);
        }

        foreach (var machine in machineMap.Values)
        {
            FanucMachineSession? existingSession;
            lock (_sessionGate)
            {
                _sessions.TryGetValue(machine.DeviceCode, out existingSession);
            }

            if (existingSession is not null)
            {
                if (!NeedsRecreate(existingSession.Endpoint, machine))
                {
                    continue;
                }

                ScheduleSessionDisposal(machine.DeviceCode);
            }

            var options = ToEndpointOptions(machine);
            var session = new FanucMachineSession(
                options,
                _logger,
                TimeSpan.FromSeconds(Math.Max(_options.TransientFailureToleranceSeconds, 0)));

            lock (_sessionGate)
            {
                _sessions[machine.DeviceCode] = session;
            }

            _logger.LogInformation("Configured runtime session for {DeviceCode} -> {IpAddress}:{Port}", machine.DeviceCode, machine.IpAddress, machine.Port);
        }
    }

    private async Task RunCollectAndPushAsync(FanucMachineSession session, CancellationToken stoppingToken)
    {
        var deviceCode = session.Endpoint.DeviceCode;

        try
        {
            var snapshot = await Task.Factory.StartNew(
                session.Collect,
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            if (snapshot is null)
            {
                return;
            }

            FanucMachineSession? currentSession;
            lock (_sessionGate)
            {
                _sessions.TryGetValue(deviceCode, out currentSession);
            }

            if (!ReferenceEquals(currentSession, session))
            {
                _logger.LogDebug("Skip pushing snapshot for {DeviceCode} because runtime session changed", deviceCode);
                return;
            }

            await PushSnapshotsAsync([snapshot], stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Collect pipeline failed for {DeviceCode}", deviceCode);
        }
    }

    private async Task PushSnapshotsAsync(IReadOnlyList<MachineRealtimeSnapshotDto> snapshots, CancellationToken stoppingToken)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        try
        {
            var result = await _ingestionClient!.PushAsync(snapshots, stoppingToken);
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Push realtime snapshots failed");
        }
    }

    private void CleanupCollectTask(string deviceCode, Task completedTask)
    {
        lock (_sessionGate)
        {
            if (_activeCollectTasks.TryGetValue(deviceCode, out var activeTask) && ReferenceEquals(activeTask, completedTask))
            {
                _activeCollectTasks.Remove(deviceCode);
            }
        }
    }

    private void ScheduleSessionDisposal(string deviceCode)
    {
        FanucMachineSession? session = null;
        Task? activeTask = null;
        lock (_sessionGate)
        {
            if (_sessions.TryGetValue(deviceCode, out session))
            {
                _sessions.Remove(deviceCode);
            }

            if (_activeCollectTasks.TryGetValue(deviceCode, out activeTask))
            {
                _activeCollectTasks.Remove(deviceCode);
            }
        }

        if (session is null)
        {
            return;
        }

        if (activeTask is { IsCompleted: false })
        {
            _ = activeTask.ContinueWith(
                _ => DisposeSessionSafe(session, deviceCode),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        DisposeSessionSafe(session, deviceCode);
    }

    private void DisposeSessionSafe(FanucMachineSession session, string deviceCode)
    {
        try
        {
            session.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Dispose session failed for {DeviceCode}", deviceCode);
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
