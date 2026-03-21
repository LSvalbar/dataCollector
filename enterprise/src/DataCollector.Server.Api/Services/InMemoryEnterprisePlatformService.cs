using System.Net;
using System.Net.Sockets;
using DataCollector.Contracts;
using DataCollector.Core;
using DataCollector.Core.Formula;

namespace DataCollector.Server.Api.Services;

public sealed class InMemoryEnterprisePlatformService : IEnterprisePlatformService
{
    private readonly object _gate = new();
    private readonly FormulaEngine _formulaEngine;
    private readonly DailyMetricsCalculator _dailyMetricsCalculator;
    private readonly LiveDeviceStateStore _liveDeviceStateStore;
    private readonly TimeProvider _timeProvider;
    private readonly List<DeviceDto> _devices;
    private readonly Dictionary<string, FormulaDefinitionDto> _formulas;
    private readonly List<UserDto> _users;
    private readonly List<RoleDto> _roles;
    private readonly IReadOnlyList<PermissionDto> _permissions;

    public InMemoryEnterprisePlatformService(
        FormulaEngine formulaEngine,
        DailyMetricsCalculator dailyMetricsCalculator,
        LiveDeviceStateStore liveDeviceStateStore,
        TimeProvider timeProvider)
    {
        _formulaEngine = formulaEngine;
        _dailyMetricsCalculator = dailyMetricsCalculator;
        _liveDeviceStateStore = liveDeviceStateStore;
        _timeProvider = timeProvider;
        _devices = [];
        _formulas = BuildDefaultFormulas();
        (_users, _roles, _permissions) = BuildSecuritySeed();
    }

    public Task<DeviceManagementOverviewDto> GetDeviceManagementOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshotAt = _timeProvider.GetLocalNow();
        var devices = BuildDeviceSnapshots(snapshotAt);

        var workshops = devices
            .GroupBy(device => new { device.WorkshopCode, device.WorkshopName })
            .OrderBy(group => group.Key.WorkshopCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkshopSummaryDto
            {
                WorkshopCode = group.Key.WorkshopCode,
                WorkshopName = group.Key.WorkshopName,
                MachineCount = group.Count(),
                ProcessingCount = group.Count(device => device.CurrentState == MachineOperationalState.Processing),
                WaitingCount = group.Count(device => device.CurrentState == MachineOperationalState.Waiting),
                StandbyCount = group.Count(device => device.CurrentState == MachineOperationalState.Standby),
                AlarmCount = group.Count(device => device.CurrentState == MachineOperationalState.Alarm),
                EmergencyCount = group.Count(device => device.CurrentState == MachineOperationalState.Emergency),
                PowerOffCount = group.Count(device => device.CurrentState == MachineOperationalState.PowerOff),
                CommunicationInterruptedCount = group.Count(device => device.CurrentState == MachineOperationalState.CommunicationInterrupted),
            })
            .ToArray();

        return Task.FromResult(new DeviceManagementOverviewDto
        {
            Workshops = workshops,
            Devices = devices,
            SnapshotAt = snapshotAt,
        });
    }

    public Task<DeviceDto> SaveDeviceAsync(DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        lock (_gate)
        {
            var now = _timeProvider.GetLocalNow();
            var existing = request.DeviceId.HasValue
                ? _devices.FirstOrDefault(device => device.DeviceId == request.DeviceId.Value)
                : null;

            ValidateAgentNetwork(request, existing?.DeviceId);
            ValidateDeviceCodeUniqueness(request, existing?.DeviceId);

            if (existing is null)
            {
                existing = new DeviceDto
                {
                    DeviceId = request.DeviceId ?? Guid.NewGuid(),
                    DepartmentCode = request.DepartmentCode.Trim(),
                    DepartmentName = request.DepartmentName.Trim(),
                    WorkshopCode = request.WorkshopCode.Trim(),
                    WorkshopName = request.WorkshopName.Trim(),
                    DeviceCode = request.DeviceCode.Trim(),
                    DeviceName = request.DeviceName.Trim(),
                    Manufacturer = request.Manufacturer.Trim(),
                    ControllerModel = request.ControllerModel.Trim(),
                    ProtocolName = request.ProtocolName.Trim(),
                    IpAddress = request.IpAddress.Trim(),
                    Port = request.Port,
                    AgentNodeName = request.AgentNodeName.Trim(),
                    ResponsiblePerson = request.ResponsiblePerson?.Trim(),
                    CurrentState = request.IsEnabled ? MachineOperationalState.CommunicationInterrupted : MachineOperationalState.PowerOff,
                    HealthLevel = request.IsEnabled ? DeviceHealthLevel.Warning : DeviceHealthLevel.Normal,
                    IsEnabled = request.IsEnabled,
                    MachineOnline = false,
                    LastHeartbeatAt = now,
                    DataQualityCode = "not_collected",
                };
                _devices.Add(existing);
            }
            else
            {
                existing.DepartmentCode = request.DepartmentCode.Trim();
                existing.DepartmentName = request.DepartmentName.Trim();
                existing.WorkshopCode = request.WorkshopCode.Trim();
                existing.WorkshopName = request.WorkshopName.Trim();
                existing.DeviceCode = request.DeviceCode.Trim();
                existing.DeviceName = request.DeviceName.Trim();
                existing.Manufacturer = request.Manufacturer.Trim();
                existing.ControllerModel = request.ControllerModel.Trim();
                existing.ProtocolName = request.ProtocolName.Trim();
                existing.IpAddress = request.IpAddress.Trim();
                existing.Port = request.Port;
                existing.AgentNodeName = request.AgentNodeName.Trim();
                existing.ResponsiblePerson = request.ResponsiblePerson?.Trim();
                existing.IsEnabled = request.IsEnabled;
                existing.CurrentState = request.IsEnabled ? existing.CurrentState : MachineOperationalState.PowerOff;
                existing.MachineOnline = request.IsEnabled && existing.MachineOnline;
                existing.HealthLevel = request.IsEnabled ? existing.HealthLevel : DeviceHealthLevel.Normal;
                existing.DataQualityCode ??= request.IsEnabled ? "not_collected" : "manual_disabled";
            }

            return Task.FromResult(CloneDevice(existing));
        }
    }

    public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var existing = _devices.FirstOrDefault(device => device.DeviceId == deviceId);
            if (existing is not null)
            {
                _devices.Remove(existing);
            }
        }

        return Task.CompletedTask;
    }

    public Task RenameDepartmentAsync(string departmentCode, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(departmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        lock (_gate)
        {
            var devices = _devices.Where(device => device.DepartmentCode.Equals(departmentCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (devices.Count == 0)
            {
                throw new KeyNotFoundException($"未找到部门 {departmentCode}。");
            }

            foreach (var device in devices)
            {
                device.DepartmentName = newName.Trim();
            }
        }

        return Task.CompletedTask;
    }

    public Task RenameWorkshopAsync(string workshopCode, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(workshopCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        lock (_gate)
        {
            var devices = _devices.Where(device => device.WorkshopCode.Equals(workshopCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (devices.Count == 0)
            {
                throw new KeyNotFoundException($"未找到车间 {workshopCode}。");
            }

            foreach (var device in devices)
            {
                device.WorkshopName = newName.Trim();
            }
        }

        return Task.CompletedTask;
    }

    public Task RenameDeviceAsync(Guid deviceId, string newName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        lock (_gate)
        {
            var device = _devices.FirstOrDefault(item => item.DeviceId == deviceId)
                ?? throw new KeyNotFoundException($"未找到设备 {deviceId}。");
            device.DeviceName = newName.Trim();
        }

        return Task.CompletedTask;
    }

    public Task<DailyReportResponse> GetDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetLocalNow();
        var devices = BuildDeviceSnapshots(now);
        var formulas = _formulas.Values
            .OrderBy(formula => formula.Code, StringComparer.OrdinalIgnoreCase)
            .Select(CloneFormula)
            .ToArray();
        var powerOnFormula = _formulas["power_on_rate"];
        var utilizationFormula = _formulas["utilization_rate"];

        var rows = devices
            .OrderBy(device => device.DepartmentCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.WorkshopCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceCode, StringComparer.OrdinalIgnoreCase)
            .Select(device =>
            {
                var timeline = _liveDeviceStateStore.HasTimeline(device.DeviceCode, reportDate)
                    ? _liveDeviceStateStore.GetTimeline(device.DeviceCode, reportDate, now)
                    : BuildFallbackTimeline(device, reportDate, now);

                var metrics = _dailyMetricsCalculator.Calculate(timeline);
                var variables = _formulaEngine.BuildVariableMap(metrics);

                return new DailyReportRowDto
                {
                    DeviceId = device.DeviceId,
                    WorkshopName = device.WorkshopName,
                    DeviceCode = device.DeviceCode,
                    DeviceName = device.DeviceName,
                    ReportDate = reportDate,
                    PowerOnMinutes = metrics.PowerOnMinutes,
                    ProcessingMinutes = metrics.ProcessingMinutes,
                    WaitingMinutes = metrics.WaitingMinutes,
                    StandbyMinutes = metrics.StandbyMinutes,
                    PowerOffMinutes = metrics.PowerOffMinutes,
                    AlarmMinutes = metrics.AlarmMinutes,
                    EmergencyMinutes = metrics.EmergencyMinutes,
                    CommunicationInterruptedMinutes = metrics.CommunicationInterruptedMinutes,
                    PowerOnRate = _formulaEngine.Evaluate(powerOnFormula.Expression, variables),
                    UtilizationRate = _formulaEngine.Evaluate(utilizationFormula.Expression, variables),
                    CurrentState = device.CurrentState,
                    DataQualityCode = _liveDeviceStateStore.HasTimeline(device.DeviceCode, reportDate)
                        ? "realtime_session"
                        : "not_collected",
                };
            })
            .ToArray();

        return Task.FromResult(new DailyReportResponse
        {
            ReportDate = reportDate,
            Formulas = formulas,
            Rows = rows,
            SnapshotAt = now,
        });
    }

    public Task<IReadOnlyList<FormulaDefinitionDto>> GetFormulasAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FormulaDefinitionDto>>(
            _formulas.Values
                .OrderBy(formula => formula.Code, StringComparer.OrdinalIgnoreCase)
                .Select(CloneFormula)
                .ToArray());
    }

    public Task<IReadOnlyList<FormulaVariableOptionDto>> GetFormulaVariableOptionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FormulaVariableOptionDto>>(
            _formulaEngine.GetSupportedVariableNames()
                .Select(variableName => new FormulaVariableOptionDto
                {
                    VariableName = variableName,
                    DisplayName = variableName,
                })
                .ToArray());
    }

    public Task<FormulaDefinitionDto> UpdateFormulaAsync(string code, FormulaUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedCode = code.Trim().ToLowerInvariant();
        lock (_gate)
        {
            if (!_formulas.TryGetValue(normalizedCode, out var existing))
            {
                throw new KeyNotFoundException($"未找到公式 {normalizedCode}。");
            }

            var expression = request.Expression.Trim();
            try
            {
                _formulaEngine.Evaluate(expression, _formulaEngine.BuildVariableMap(new DailyMetricsSnapshot()));
            }
            catch (Exception)
            {
                throw new InvalidOperationException("公式输入有误，请查看列名是否相同。");
            }

            existing.Expression = expression;
            existing.UpdatedBy = request.UpdatedBy.Trim();
            existing.UpdatedAt = _timeProvider.GetLocalNow();
            return Task.FromResult(CloneFormula(existing));
        }
    }

    public Task<DeviceTimelineResponse> GetDeviceTimelineAsync(Guid deviceId, DateOnly reportDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetLocalNow();
        var device = BuildDeviceSnapshots(now).FirstOrDefault(item => item.DeviceId == deviceId)
            ?? throw new KeyNotFoundException($"未找到设备 {deviceId}。");
        var segments = _liveDeviceStateStore.HasTimeline(device.DeviceCode, reportDate)
            ? _liveDeviceStateStore.GetTimeline(device.DeviceCode, reportDate, now)
            : BuildFallbackTimeline(device, reportDate, now);
        var metrics = _dailyMetricsCalculator.Calculate(segments);

        return Task.FromResult(new DeviceTimelineResponse
        {
            DeviceId = device.DeviceId,
            DeviceCode = device.DeviceCode,
            DeviceName = device.DeviceName,
            WorkshopName = device.WorkshopName,
            ReportDate = reportDate,
            Segments = segments,
            DailyTotals = new Dictionary<string, double>
            {
                ["开机时间"] = metrics.PowerOnMinutes,
                ["加工时间"] = metrics.ProcessingMinutes,
                ["等待时间"] = metrics.WaitingMinutes,
                ["待机时间"] = metrics.StandbyMinutes,
                ["关机时间"] = metrics.PowerOffMinutes,
                ["报警时间"] = metrics.AlarmMinutes,
                ["急停时间"] = metrics.EmergencyMinutes,
                ["通信中断时间"] = metrics.CommunicationInterruptedMinutes,
            },
            SnapshotAt = now,
        });
    }

    public Task<AgentRuntimeConfigurationDto> GetAgentRuntimeConfigurationAsync(string agentNodeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(agentNodeName);

        lock (_gate)
        {
            var devices = _devices
                .Where(device => device.AgentNodeName.Equals(agentNodeName.Trim(), StringComparison.OrdinalIgnoreCase) && device.IsEnabled)
                .OrderBy(device => device.DeviceCode, StringComparer.OrdinalIgnoreCase)
                .Select(device => new AgentMachineConfigurationDto
                {
                    DeviceCode = device.DeviceCode,
                    IpAddress = device.IpAddress,
                    Port = device.Port,
                    Protocol = string.IsNullOrWhiteSpace(device.ProtocolName) ? "FOCAS over Ethernet" : device.ProtocolName,
                    TimeoutSeconds = 10,
                    ProcessingOperationModes = [3],
                    WaitingOperationModes = [1, 2],
                })
                .ToArray();

            return Task.FromResult(new AgentRuntimeConfigurationDto
            {
                AgentNodeName = agentNodeName.Trim(),
                WorkshopCode = _devices.FirstOrDefault(device => device.AgentNodeName.Equals(agentNodeName.Trim(), StringComparison.OrdinalIgnoreCase))?.WorkshopCode ?? string.Empty,
                Machines = devices,
                GeneratedAt = _timeProvider.GetLocalNow(),
            });
        }
    }

    public Task<SecurityOverviewDto> GetSecurityOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(new SecurityOverviewDto
            {
                Users = _users.Select(CloneUser).ToArray(),
                Roles = _roles.Select(CloneRole).ToArray(),
                Permissions = _permissions.Select(ClonePermission).ToArray(),
            });
        }
    }

    public Task<UserDto> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.UserCode) ||
            string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.Department))
        {
            throw new InvalidOperationException("用户编码、用户名、显示名和所属部门不能为空。");
        }

        var normalizedRoleCodes = request.RoleCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedRoleCodes.Length == 0)
        {
            throw new InvalidOperationException("用户至少需要绑定一个角色。");
        }

        lock (_gate)
        {
            var missingRoleCode = normalizedRoleCodes.FirstOrDefault(code => _roles.All(role => !role.RoleCode.Equals(code, StringComparison.OrdinalIgnoreCase)));
            if (missingRoleCode is not null)
            {
                throw new InvalidOperationException($"角色 {missingRoleCode} 不存在，无法保存用户。");
            }

            var existing = _users.FirstOrDefault(user => user.UserCode.Equals(request.UserCode, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new UserDto
                {
                    UserCode = request.UserCode.Trim(),
                    UserName = request.UserName.Trim(),
                    DisplayName = request.DisplayName.Trim(),
                    Department = request.Department.Trim(),
                    RoleCodes = normalizedRoleCodes,
                    IsEnabled = request.IsEnabled,
                    LastLoginAt = DateTimeOffset.MinValue,
                };
                _users.Add(existing);
            }
            else
            {
                existing.UserName = request.UserName.Trim();
                existing.DisplayName = request.DisplayName.Trim();
                existing.Department = request.Department.Trim();
                existing.RoleCodes = normalizedRoleCodes;
                existing.IsEnabled = request.IsEnabled;
            }

            return Task.FromResult(CloneUser(existing));
        }
    }

    public Task DeleteUserAsync(string userCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userCode);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var existing = _users.FirstOrDefault(user => user.UserCode.Equals(userCode, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _users.Remove(existing);
            }
        }

        return Task.CompletedTask;
    }

    public Task<RoleDto> SaveRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.RoleCode) ||
            string.IsNullOrWhiteSpace(request.RoleName))
        {
            throw new InvalidOperationException("角色编码和角色名称不能为空。");
        }

        var normalizedPermissionCodes = request.PermissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPermissionCodes.Length == 0)
        {
            throw new InvalidOperationException("角色至少需要包含一个权限。");
        }

        lock (_gate)
        {
            var missingPermission = normalizedPermissionCodes.FirstOrDefault(code => _permissions.All(permission => !permission.PermissionCode.Equals(code, StringComparison.OrdinalIgnoreCase)));
            if (missingPermission is not null)
            {
                throw new InvalidOperationException($"权限 {missingPermission} 不存在，无法保存角色。");
            }

            var permissions = _permissions
                .Where(permission => normalizedPermissionCodes.Contains(permission.PermissionCode, StringComparer.OrdinalIgnoreCase))
                .Select(ClonePermission)
                .ToArray();

            var existing = _roles.FirstOrDefault(role => role.RoleCode.Equals(request.RoleCode, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new RoleDto
                {
                    RoleCode = request.RoleCode.Trim(),
                    RoleName = request.RoleName.Trim(),
                    Description = request.Description.Trim(),
                    Permissions = permissions,
                };
                _roles.Add(existing);
            }
            else
            {
                existing.RoleName = request.RoleName.Trim();
                existing.Description = request.Description.Trim();
                existing.Permissions = permissions;
            }

            return Task.FromResult(CloneRole(existing));
        }
    }

    public Task DeleteRoleAsync(string roleCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_users.Any(user => user.RoleCodes.Contains(roleCode, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"角色 {roleCode} 仍被用户引用，不能删除。");
            }

            var existing = _roles.FirstOrDefault(role => role.RoleCode.Equals(roleCode, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _roles.Remove(existing);
            }
        }

        return Task.CompletedTask;
    }

    private List<DeviceDto> BuildDeviceSnapshots(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _devices.Select(device =>
            {
                var clone = CloneDevice(device);
                ApplyLiveSnapshot(clone);

                if (clone.LastCollectedAt is null)
                {
                    clone.MachineOnline = false;
                    clone.CurrentState = clone.IsEnabled ? MachineOperationalState.CommunicationInterrupted : MachineOperationalState.PowerOff;
                    clone.HealthLevel = clone.IsEnabled ? DeviceHealthLevel.Warning : DeviceHealthLevel.Normal;
                    clone.DataQualityCode ??= clone.IsEnabled ? "not_collected" : "manual_disabled";
                }
                else if (!clone.IsEnabled)
                {
                    clone.MachineOnline = false;
                    clone.CurrentState = MachineOperationalState.PowerOff;
                    clone.HealthLevel = DeviceHealthLevel.Normal;
                    clone.DataQualityCode = "manual_disabled";
                }

                return clone;
            }).ToList();
        }
    }

    private void ApplyLiveSnapshot(DeviceDto device)
    {
        if (!_liveDeviceStateStore.TryGetLatest(device.DeviceCode, out var snapshot))
        {
            return;
        }

        device.MachineOnline = snapshot.MachineOnline;
        device.CurrentState = snapshot.CurrentState;
        device.LastCollectedAt = snapshot.CollectedAt;
        device.LastHeartbeatAt = snapshot.CollectedAt;
        device.CurrentProgramNo = snapshot.CurrentProgramNo;
        device.CurrentProgramName = snapshot.CurrentProgramName;
        device.SpindleSpeedRpm = snapshot.SpindleSpeedRpm;
        device.SpindleLoadPercent = snapshot.SpindleLoadPercent;
        device.AutomaticMode = snapshot.AutomaticMode;
        device.OperationMode = snapshot.OperationMode;
        device.AlarmState = snapshot.AlarmState;
        device.EmergencyState = snapshot.EmergencyState;
        device.ControllerModeText = snapshot.ControllerModeText;
        device.OeeStatusText = snapshot.OeeStatusText;
        device.NativePowerOnTotalMs = snapshot.NativePowerOnTotalMs;
        device.NativeOperatingTotalMs = snapshot.NativeOperatingTotalMs;
        device.NativeCuttingTotalMs = snapshot.NativeCuttingTotalMs;
        device.NativeFreeTotalMs = snapshot.NativeFreeTotalMs;
        device.DataQualityCode = snapshot.DataQualityCode;
        device.LastCollectionError = snapshot.ErrorMessage;
        device.HealthLevel = snapshot.CurrentState switch
        {
            MachineOperationalState.Alarm or MachineOperationalState.Emergency => DeviceHealthLevel.Critical,
            MachineOperationalState.CommunicationInterrupted => DeviceHealthLevel.Warning,
            _ => DeviceHealthLevel.Normal,
        };
    }

    private IReadOnlyList<TimelineSegmentDto> BuildFallbackTimeline(DeviceDto device, DateOnly reportDate, DateTimeOffset now)
    {
        if (reportDate > DateOnly.FromDateTime(now.LocalDateTime))
        {
            return [];
        }

        var offset = now.Offset;
        var startAt = new DateTimeOffset(reportDate.ToDateTime(TimeOnly.MinValue), offset);
        var endAt = reportDate == DateOnly.FromDateTime(now.LocalDateTime)
            ? now
            : new DateTimeOffset(reportDate.ToDateTime(new TimeOnly(23, 59, 59)), offset);

        if (endAt <= startAt)
        {
            endAt = startAt;
        }

        return
        [
            new TimelineSegmentDto
            {
                State = device.IsEnabled ? MachineOperationalState.CommunicationInterrupted : MachineOperationalState.PowerOff,
                StartAt = startAt,
                EndAt = endAt,
                DurationMinutes = Math.Round((endAt - startAt).TotalMinutes, 2, MidpointRounding.AwayFromZero),
                DataQualityCode = device.IsEnabled ? "not_collected" : "manual_disabled",
            },
        ];
    }

    private void ValidateRequest(DeviceUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DepartmentCode) ||
            string.IsNullOrWhiteSpace(request.DepartmentName) ||
            string.IsNullOrWhiteSpace(request.WorkshopCode) ||
            string.IsNullOrWhiteSpace(request.WorkshopName) ||
            string.IsNullOrWhiteSpace(request.DeviceCode) ||
            string.IsNullOrWhiteSpace(request.DeviceName) ||
            string.IsNullOrWhiteSpace(request.IpAddress) ||
            string.IsNullOrWhiteSpace(request.AgentNodeName))
        {
            throw new InvalidOperationException("部门、车间、设备编码、设备名称、IP 地址和 Agent 节点不能为空。");
        }

        if (request.Port <= 0 || request.Port > 65535)
        {
            throw new InvalidOperationException("端口必须在 1 到 65535 之间。");
        }
    }

    private void ValidateDeviceCodeUniqueness(DeviceUpsertRequest request, Guid? excludedDeviceId)
    {
        var duplicated = _devices.FirstOrDefault(device =>
            (!excludedDeviceId.HasValue || device.DeviceId != excludedDeviceId.Value) &&
            device.DeviceCode.Equals(request.DeviceCode.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicated is not null)
        {
            throw new InvalidOperationException($"设备编码 {request.DeviceCode.Trim()} 已存在，请使用唯一编码。");
        }
    }

    private void ValidateAgentNetwork(DeviceUpsertRequest request, Guid? excludedDeviceId)
    {
        if (!IPAddress.TryParse(request.IpAddress.Trim(), out var candidateAddress))
        {
            throw new InvalidOperationException($"设备 IP 地址 {request.IpAddress.Trim()} 格式不正确，无法进行网段校验。");
        }

        if (candidateAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("当前只支持 IPv4 地址校验。");
        }

        var devicesOnSameAgent = _devices
            .Where(device =>
                device.AgentNodeName.Equals(request.AgentNodeName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (!excludedDeviceId.HasValue || device.DeviceId != excludedDeviceId.Value))
            .ToList();

        if (devicesOnSameAgent.Count == 0)
        {
            return;
        }

        var referenceDevice = devicesOnSameAgent[0];
        if (!IPAddress.TryParse(referenceDevice.IpAddress, out var referenceAddress) || referenceAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException(
                $"Agent 节点 {request.AgentNodeName.Trim()} 下已有设备 {referenceDevice.DeviceCode} 的 IP 配置异常，无法继续做网段校验。");
        }

        if (IsSame24Subnet(referenceAddress, candidateAddress))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Agent 节点 {request.AgentNodeName.Trim()} 当前已绑定网段 {GetSubnetPrefix(referenceAddress)}.0/24，" +
            $"设备 {referenceDevice.DeviceCode} 的 IP 为 {referenceDevice.IpAddress}；当前新设备 IP 为 {request.IpAddress.Trim()}，不在同一网段，已拒绝保存。");
    }

    private static bool IsSame24Subnet(IPAddress left, IPAddress right)
    {
        var leftBytes = left.GetAddressBytes();
        var rightBytes = right.GetAddressBytes();
        return leftBytes[0] == rightBytes[0] &&
               leftBytes[1] == rightBytes[1] &&
               leftBytes[2] == rightBytes[2];
    }

    private static string GetSubnetPrefix(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
    }

    private static Dictionary<string, FormulaDefinitionDto> BuildDefaultFormulas()
    {
        var now = DateTimeOffset.Now;
        return new Dictionary<string, FormulaDefinitionDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["power_on_rate"] = new()
            {
                Code = "power_on_rate",
                DisplayName = "开机率",
                Description = "默认按当天已观测时长计算开机率。",
                Expression = "(开机时间 / 已观测时间) * 100",
                ResultUnit = "%",
                UpdatedAt = now,
                UpdatedBy = "system",
            },
            ["utilization_rate"] = new()
            {
                Code = "utilization_rate",
                DisplayName = "利用率",
                Description = "默认按开机时间中的加工占比计算利用率。",
                Expression = "(加工时间 / 开机时间) * 100",
                ResultUnit = "%",
                UpdatedAt = now,
                UpdatedBy = "system",
            },
        };
    }

    private static (List<UserDto> Users, List<RoleDto> Roles, IReadOnlyList<PermissionDto> Permissions) BuildSecuritySeed()
    {
        var permissions = new[]
        {
            new PermissionDto { PermissionCode = "device.read", PermissionName = "查看设备", Description = "查看设备列表、实时状态和时间线。" },
            new PermissionDto { PermissionCode = "device.write", PermissionName = "维护设备", Description = "维护部门、车间和设备主数据。" },
            new PermissionDto { PermissionCode = "report.read", PermissionName = "查看报表", Description = "查看日报、开机率和利用率。" },
            new PermissionDto { PermissionCode = "formula.write", PermissionName = "维护公式", Description = "修改开机率和利用率公式。" },
            new PermissionDto { PermissionCode = "security.write", PermissionName = "维护权限", Description = "管理用户、角色和权限。" },
        };

        var roles = new[]
        {
            new RoleDto
            {
                RoleCode = "admin",
                RoleName = "系统管理员",
                Description = "负责系统配置、权限和设备维护。",
                Permissions = permissions,
            },
            new RoleDto
            {
                RoleCode = "manager",
                RoleName = "车间主管",
                Description = "负责查看车间状态、报表和设备时间线。",
                Permissions = permissions.Where(permission => permission.PermissionCode is "device.read" or "report.read").ToArray(),
            },
            new RoleDto
            {
                RoleCode = "itops",
                RoleName = "IT 运维",
                Description = "负责 Agent、服务端和设备连通性维护。",
                Permissions = permissions.Where(permission => permission.PermissionCode is "device.read" or "device.write" or "report.read" or "formula.write").ToArray(),
            },
        };

        var users = new[]
        {
            new UserDto
            {
                UserCode = "admin",
                UserName = "admin",
                DisplayName = "系统管理员",
                Department = "信息部",
                RoleCodes = ["admin"],
                IsEnabled = true,
                LastLoginAt = DateTimeOffset.Now.AddMinutes(-15),
            },
        };

        return (users.ToList(), roles.ToList(), permissions);
    }

    private static DeviceDto CloneDevice(DeviceDto source)
    {
        return new DeviceDto
        {
            DeviceId = source.DeviceId,
            DepartmentCode = source.DepartmentCode,
            DepartmentName = source.DepartmentName,
            WorkshopCode = source.WorkshopCode,
            WorkshopName = source.WorkshopName,
            DeviceCode = source.DeviceCode,
            DeviceName = source.DeviceName,
            Manufacturer = source.Manufacturer,
            ControllerModel = source.ControllerModel,
            ProtocolName = source.ProtocolName,
            IpAddress = source.IpAddress,
            Port = source.Port,
            AgentNodeName = source.AgentNodeName,
            ResponsiblePerson = source.ResponsiblePerson,
            CurrentState = source.CurrentState,
            HealthLevel = source.HealthLevel,
            IsEnabled = source.IsEnabled,
            MachineOnline = source.MachineOnline,
            LastHeartbeatAt = source.LastHeartbeatAt,
            LastCollectedAt = source.LastCollectedAt,
            CurrentProgramNo = source.CurrentProgramNo,
            CurrentProgramName = source.CurrentProgramName,
            SpindleSpeedRpm = source.SpindleSpeedRpm,
            SpindleLoadPercent = source.SpindleLoadPercent,
            AutomaticMode = source.AutomaticMode,
            OperationMode = source.OperationMode,
            AlarmState = source.AlarmState,
            EmergencyState = source.EmergencyState,
            ControllerModeText = source.ControllerModeText,
            OeeStatusText = source.OeeStatusText,
            NativePowerOnTotalMs = source.NativePowerOnTotalMs,
            NativeOperatingTotalMs = source.NativeOperatingTotalMs,
            NativeCuttingTotalMs = source.NativeCuttingTotalMs,
            NativeFreeTotalMs = source.NativeFreeTotalMs,
            DataQualityCode = source.DataQualityCode,
            LastCollectionError = source.LastCollectionError,
        };
    }

    private static FormulaDefinitionDto CloneFormula(FormulaDefinitionDto source)
    {
        return new FormulaDefinitionDto
        {
            Code = source.Code,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Expression = source.Expression,
            ResultUnit = source.ResultUnit,
            UpdatedAt = source.UpdatedAt,
            UpdatedBy = source.UpdatedBy,
        };
    }

    private static UserDto CloneUser(UserDto source)
    {
        return new UserDto
        {
            UserCode = source.UserCode,
            UserName = source.UserName,
            DisplayName = source.DisplayName,
            Department = source.Department,
            RoleCodes = source.RoleCodes.ToArray(),
            IsEnabled = source.IsEnabled,
            LastLoginAt = source.LastLoginAt,
        };
    }

    private static RoleDto CloneRole(RoleDto source)
    {
        return new RoleDto
        {
            RoleCode = source.RoleCode,
            RoleName = source.RoleName,
            Description = source.Description,
            Permissions = source.Permissions.Select(ClonePermission).ToArray(),
        };
    }

    private static PermissionDto ClonePermission(PermissionDto source)
    {
        return new PermissionDto
        {
            PermissionCode = source.PermissionCode,
            PermissionName = source.PermissionName,
            Description = source.Description,
        };
    }
}
