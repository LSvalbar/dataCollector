using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DataCollector.Contracts;
using DataCollector.Core;
using DataCollector.Core.Formula;
using DataCollector.Server.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Services;

public sealed class DatabaseEnterprisePlatformService : IEnterprisePlatformService
{
    private readonly IDbContextFactory<EnterpriseDbContext> _dbContextFactory;
    private readonly FormulaEngine _formulaEngine;
    private readonly DailyMetricsCalculator _dailyMetricsCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly RealtimeStateOptions _realtimeStateOptions;

    public DatabaseEnterprisePlatformService(
        IDbContextFactory<EnterpriseDbContext> dbContextFactory,
        FormulaEngine formulaEngine,
        DailyMetricsCalculator dailyMetricsCalculator,
        TimeProvider timeProvider,
        RealtimeStateOptions realtimeStateOptions)
    {
        _dbContextFactory = dbContextFactory;
        _formulaEngine = formulaEngine;
        _dailyMetricsCalculator = dailyMetricsCalculator;
        _timeProvider = timeProvider;
        _realtimeStateOptions = realtimeStateOptions;
    }

    public async Task<DeviceManagementOverviewDto> GetDeviceManagementOverviewAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshotAt = _timeProvider.GetLocalNow();
        var devices = await dbContext.Devices
            .AsNoTracking()
            .OrderBy(device => device.DepartmentCode)
            .ThenBy(device => device.WorkshopCode)
            .ThenBy(device => device.DeviceCode)
            .ToListAsync(cancellationToken);

        var deviceDtos = devices.Select(device => ToDeviceDto(device, snapshotAt)).ToArray();
        var workshops = deviceDtos
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

        return new DeviceManagementOverviewDto
        {
            Workshops = workshops,
            Devices = deviceDtos,
            SnapshotAt = snapshotAt,
        };
    }

    public async Task<DeviceDto> SaveDeviceAsync(DeviceUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetLocalNow();
        var existing = request.DeviceId.HasValue
            ? await dbContext.Devices.FirstOrDefaultAsync(device => device.DeviceId == request.DeviceId.Value, cancellationToken)
            : null;

        await ValidateAgentNetworkAsync(dbContext, request, existing?.DeviceId, cancellationToken);
        await ValidateDeviceCodeUniquenessAsync(dbContext, request, existing?.DeviceId, cancellationToken);

        if (existing is null)
        {
            existing = new DeviceEntity
            {
                DeviceId = request.DeviceId ?? Guid.NewGuid(),
                LastHeartbeatAt = now,
            };
            dbContext.Devices.Add(existing);
        }

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
        existing.ResponsiblePerson = string.IsNullOrWhiteSpace(request.ResponsiblePerson) ? null : request.ResponsiblePerson.Trim();
        existing.IsEnabled = request.IsEnabled;

        if (!request.IsEnabled)
        {
            existing.MachineOnline = false;
            existing.CurrentState = MachineOperationalState.PowerOff;
            existing.HealthLevel = DeviceHealthLevel.Normal;
            existing.DataQualityCode = "manual_disabled";
        }
        else if (existing.LastCollectedAt is null)
        {
            existing.MachineOnline = false;
            existing.CurrentState = MachineOperationalState.CommunicationInterrupted;
            existing.HealthLevel = DeviceHealthLevel.Warning;
            existing.DataQualityCode = "not_collected";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDeviceDto(existing, now);
    }

    public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.Devices.FirstOrDefaultAsync(device => device.DeviceId == deviceId, cancellationToken);
        if (existing is null)
        {
            return;
        }

        dbContext.Devices.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameDepartmentAsync(string departmentCode, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(departmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var devices = await dbContext.Devices
            .Where(device => device.DepartmentCode == departmentCode)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            throw new KeyNotFoundException($"未找到部门 {departmentCode}。");
        }

        foreach (var device in devices)
        {
            device.DepartmentName = newName.Trim();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameWorkshopAsync(string workshopCode, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workshopCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var devices = await dbContext.Devices
            .Where(device => device.WorkshopCode == workshopCode)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            throw new KeyNotFoundException($"未找到车间 {workshopCode}。");
        }

        foreach (var device in devices)
        {
            device.WorkshopName = newName.Trim();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameDeviceAsync(Guid deviceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var device = await dbContext.Devices.FirstOrDefaultAsync(item => item.DeviceId == deviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"未找到设备 {deviceId}。");

        device.DeviceName = newName.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DailyReportResponse> GetDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetLocalNow();
        var reportDateKey = ToDateKey(reportDate);
        var devices = await dbContext.Devices
            .AsNoTracking()
            .OrderBy(device => device.DepartmentCode)
            .ThenBy(device => device.WorkshopCode)
            .ThenBy(device => device.DeviceCode)
            .ToListAsync(cancellationToken);
        var formulas = await EnsureRequiredFormulasAsync(dbContext, cancellationToken);
        var timelineSegments = await dbContext.TimelineSegments
            .AsNoTracking()
            .Where(segment => segment.ReportDateKey == reportDateKey)
            .ToListAsync(cancellationToken);
        var groupedSegments = timelineSegments
            .GroupBy(segment => segment.DeviceId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var powerOnFormula = formulas.First(formula => formula.Code == DefaultFormulaCatalog.PowerOnRateCode);
        var utilizationFormula = formulas.First(formula => formula.Code == DefaultFormulaCatalog.UtilizationRateCode);
        var rows = new List<DailyReportRowDto>(devices.Count);

        foreach (var device in devices)
        {
            var runtimeDevice = ToDeviceDto(device, now);
            var segments = groupedSegments.TryGetValue(device.DeviceId, out var foundSegments)
                ? ToTimelineDtos(foundSegments, reportDate, now)
                : BuildFallbackTimeline(runtimeDevice, reportDate, now);

            var metrics = _dailyMetricsCalculator.Calculate(segments);
            var variables = _formulaEngine.BuildVariableMap(metrics);
            rows.Add(new DailyReportRowDto
            {
                DeviceId = runtimeDevice.DeviceId,
                WorkshopName = runtimeDevice.WorkshopName,
                DeviceCode = runtimeDevice.DeviceCode,
                DeviceName = runtimeDevice.DeviceName,
                ReportDate = reportDate,
                PowerOnMinutes = metrics.PowerOnMinutes,
                ProcessingMinutes = metrics.ProcessingMinutes,
                WaitingMinutes = metrics.WaitingMinutes,
                StandbyMinutes = metrics.StandbyMinutes,
                PowerOffMinutes = metrics.PowerOffMinutes,
                AlarmMinutes = metrics.AlarmMinutes,
                EmergencyMinutes = metrics.EmergencyMinutes,
                CommunicationInterruptedMinutes = metrics.CommunicationInterruptedMinutes,
                PowerOnRate = EvaluateFormulaWithFallback(powerOnFormula.Expression, DefaultFormulaCatalog.PowerOnRateExpression, variables),
                UtilizationRate = EvaluateFormulaWithFallback(utilizationFormula.Expression, DefaultFormulaCatalog.UtilizationRateExpression, variables),
                CurrentState = runtimeDevice.CurrentState,
                DataQualityCode = segments.Count > 0 ? segments[^1].DataQualityCode : "not_collected",
            });
        }

        return new DailyReportResponse
        {
            ReportDate = reportDate,
            Formulas = formulas.Select(ToFormulaDto).ToArray(),
            Rows = rows,
            SnapshotAt = now,
        };
    }

    public async Task<IReadOnlyList<FormulaDefinitionDto>> GetFormulasAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await EnsureRequiredFormulasAsync(dbContext, cancellationToken))
            .OrderBy(formula => formula.Code)
            .Select(ToFormulaDto)
            .ToArray();
    }

    private async Task<List<FormulaEntity>> EnsureRequiredFormulasAsync(EnterpriseDbContext dbContext, CancellationToken cancellationToken)
    {
        var formulas = await dbContext.Formulas
            .OrderBy(formula => formula.Code)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetLocalNow();
        var missingDefaults = new List<FormulaEntity>();
        var hasUpdates = false;

        if (!formulas.Any(item => item.Code == DefaultFormulaCatalog.PowerOnRateCode))
        {
            missingDefaults.Add(DefaultFormulaCatalog.CreatePowerOnRate(now));
        }

        if (!formulas.Any(item => item.Code == DefaultFormulaCatalog.UtilizationRateCode))
        {
            missingDefaults.Add(DefaultFormulaCatalog.CreateUtilizationRate(now));
        }

        if (missingDefaults.Count > 0)
        {
            await dbContext.Formulas.AddRangeAsync(missingDefaults, cancellationToken);
            formulas.AddRange(missingDefaults);
            hasUpdates = true;
        }

        foreach (var formula in formulas)
        {
            if (TryNormalizeFormula(formula))
            {
                formula.UpdatedAt = now;
                if (string.IsNullOrWhiteSpace(formula.UpdatedBy))
                {
                    formula.UpdatedBy = "system";
                }

                hasUpdates = true;
            }
        }

        if (hasUpdates)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return formulas;
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

    public async Task<FormulaDefinitionDto> UpdateFormulaAsync(string code, FormulaUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(request);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedCode = code.Trim().ToLowerInvariant();
        var formula = await dbContext.Formulas.FirstOrDefaultAsync(item => item.Code == normalizedCode, cancellationToken)
            ?? throw new KeyNotFoundException($"未找到公式 {normalizedCode}。");

        var expression = request.Expression.Trim();
        try
        {
            _formulaEngine.Evaluate(expression, _formulaEngine.BuildVariableMap(new DailyMetricsSnapshot()));
        }
        catch (Exception)
        {
            throw new InvalidOperationException("公式输入有误，请查看列名是否相同。");
        }

        var primaryVariable = request.PrimaryVariable?.Trim();
        if (string.IsNullOrWhiteSpace(primaryVariable))
        {
            primaryVariable = TryParseFormulaSelection(expression)?.PrimaryVariable ?? formula.PrimaryVariable;
        }

        if (string.IsNullOrWhiteSpace(primaryVariable) ||
            !_formulaEngine.GetSupportedVariableNames().Contains(primaryVariable, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("公式主时间项无效，请从下拉选项中选择。");
        }

        var standardWorkHours = request.StandardWorkHours ?? formula.StandardWorkHours;
        if (standardWorkHours <= 0)
        {
            throw new InvalidOperationException("制式工时必须大于 0 小时。");
        }

        var coefficient = request.Coefficient ?? formula.Coefficient;
        if (coefficient <= 0)
        {
            throw new InvalidOperationException("系数必须大于 0。");
        }

        var visibleOptions = NormalizeVisibleOptions(request.VisibleOptions, primaryVariable);

        formula.Expression = expression;
        formula.PrimaryVariable = primaryVariable;
        formula.StandardWorkHours = standardWorkHours;
        formula.Coefficient = coefficient;
        formula.VisibleOptionsCsv = string.Join(",", visibleOptions);
        formula.UpdatedBy = request.UpdatedBy.Trim();
        formula.UpdatedAt = _timeProvider.GetLocalNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToFormulaDto(formula);
    }

    public async Task<DeviceTimelineResponse> GetDeviceTimelineAsync(Guid deviceId, DateOnly reportDate, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetLocalNow();
        var device = await dbContext.Devices.AsNoTracking().FirstOrDefaultAsync(item => item.DeviceId == deviceId, cancellationToken)
            ?? throw new KeyNotFoundException($"未找到设备 {deviceId}。");
        var runtimeDevice = ToDeviceDto(device, now);
        var segments = await dbContext.TimelineSegments
            .AsNoTracking()
            .Where(segment => segment.DeviceId == deviceId && segment.ReportDateKey == ToDateKey(reportDate))
            .ToListAsync(cancellationToken);

        var timeline = segments.Count > 0
            ? ToTimelineDtos(segments, reportDate, now)
            : BuildFallbackTimeline(runtimeDevice, reportDate, now);
        var metrics = _dailyMetricsCalculator.Calculate(timeline);

        return new DeviceTimelineResponse
        {
            DeviceId = runtimeDevice.DeviceId,
            DeviceCode = runtimeDevice.DeviceCode,
            DeviceName = runtimeDevice.DeviceName,
            WorkshopName = runtimeDevice.WorkshopName,
            ReportDate = reportDate,
            Segments = timeline,
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
        };
    }

    public async Task<AgentRuntimeConfigurationDto> GetAgentRuntimeConfigurationAsync(string agentNodeName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentNodeName);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var devices = await dbContext.Devices
            .AsNoTracking()
            .Where(device => device.AgentNodeName == agentNodeName.Trim() && device.IsEnabled)
            .OrderBy(device => device.DeviceCode)
            .ToListAsync(cancellationToken);

        return new AgentRuntimeConfigurationDto
        {
            AgentNodeName = agentNodeName.Trim(),
            WorkshopCode = devices.Select(device => device.WorkshopCode).FirstOrDefault() ?? string.Empty,
            Machines = devices.Select(device => new AgentMachineConfigurationDto
            {
                DeviceCode = device.DeviceCode,
                IpAddress = device.IpAddress,
                Port = device.Port,
                Protocol = string.IsNullOrWhiteSpace(device.ProtocolName) ? "FOCAS over Ethernet" : device.ProtocolName,
                TimeoutSeconds = 10,
                ProcessingOperationModes = [3],
                WaitingOperationModes = [1, 2],
            }).ToArray(),
            GeneratedAt = _timeProvider.GetLocalNow(),
        };
    }

    public async Task<SecurityOverviewDto> GetSecurityOverviewAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var users = await dbContext.Users
            .AsNoTracking()
            .Include(user => user.Roles)
            .OrderBy(user => user.UserCode)
            .ToListAsync(cancellationToken);
        var roles = await dbContext.Roles
            .AsNoTracking()
            .Include(role => role.Permissions)
            .OrderBy(role => role.RoleCode)
            .ToListAsync(cancellationToken);

        return new SecurityOverviewDto
        {
            Users = users.Select(ToUserDto).ToArray(),
            Roles = roles.Select(ToRoleDto).ToArray(),
            Permissions = PermissionCatalog.All.Select(permission => PermissionCatalog.Clone(permission.PermissionCode)).ToArray(),
        };
    }

    public async Task<UserDto> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingRoles = await dbContext.Roles
            .AsNoTracking()
            .Select(role => role.RoleCode)
            .ToListAsync(cancellationToken);
        var missingRoleCode = normalizedRoleCodes.FirstOrDefault(code => !existingRoles.Contains(code, StringComparer.OrdinalIgnoreCase));
        if (missingRoleCode is not null)
        {
            throw new InvalidOperationException($"角色 {missingRoleCode} 不存在，无法保存用户。");
        }

        var user = await dbContext.Users
            .Include(item => item.Roles)
            .FirstOrDefaultAsync(item => item.UserCode == request.UserCode, cancellationToken);
        if (user is null)
        {
            user = new UserEntity
            {
                UserCode = request.UserCode.Trim(),
                LastLoginAt = DateTimeOffset.MinValue,
            };
            dbContext.Users.Add(user);
        }

        user.UserName = request.UserName.Trim();
        user.DisplayName = request.DisplayName.Trim();
        user.Department = request.Department.Trim();
        user.IsEnabled = request.IsEnabled;
        user.Roles.Clear();
        foreach (var roleCode in normalizedRoleCodes)
        {
            user.Roles.Add(new UserRoleEntity
            {
                UserCode = user.UserCode,
                RoleCode = roleCode,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(user).Collection(item => item.Roles).LoadAsync(cancellationToken);
        return ToUserDto(user);
    }

    public async Task DeleteUserAsync(string userCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userCode);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(item => item.UserCode == userCode, cancellationToken);
        if (user is null)
        {
            return;
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RoleDto> SaveRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var missingPermission = normalizedPermissionCodes.FirstOrDefault(code =>
            PermissionCatalog.All.All(permission => !permission.PermissionCode.Equals(code, StringComparison.OrdinalIgnoreCase)));
        if (missingPermission is not null)
        {
            throw new InvalidOperationException($"权限 {missingPermission} 不存在，无法保存角色。");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var role = await dbContext.Roles
            .Include(item => item.Permissions)
            .FirstOrDefaultAsync(item => item.RoleCode == request.RoleCode, cancellationToken);
        if (role is null)
        {
            role = new RoleEntity
            {
                RoleCode = request.RoleCode.Trim(),
            };
            dbContext.Roles.Add(role);
        }

        role.RoleName = request.RoleName.Trim();
        role.Description = request.Description.Trim();
        role.Permissions.Clear();
        foreach (var permissionCode in normalizedPermissionCodes)
        {
            role.Permissions.Add(new RolePermissionEntity
            {
                RoleCode = role.RoleCode,
                PermissionCode = permissionCode,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(role).Collection(item => item.Permissions).LoadAsync(cancellationToken);
        return ToRoleDto(role);
    }

    public async Task DeleteRoleAsync(string roleCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var inUse = await dbContext.UserRoles.AnyAsync(item => item.RoleCode == roleCode, cancellationToken);
        if (inUse)
        {
            throw new InvalidOperationException($"角色 {roleCode} 仍被用户引用，不能删除。");
        }

        var role = await dbContext.Roles.FirstOrDefaultAsync(item => item.RoleCode == roleCode, cancellationToken);
        if (role is null)
        {
            return;
        }

        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private DeviceDto ToDeviceDto(DeviceEntity source, DateTimeOffset now)
    {
        var machineOnline = source.MachineOnline;
        var currentState = source.CurrentState;
        var healthLevel = source.HealthLevel;
        var dataQualityCode = source.DataQualityCode;
        var errorMessage = source.LastCollectionError;

        if (!source.IsEnabled)
        {
            machineOnline = false;
            currentState = MachineOperationalState.PowerOff;
            healthLevel = DeviceHealthLevel.Normal;
            dataQualityCode = "manual_disabled";
        }
        else if (source.LastCollectedAt is null)
        {
            machineOnline = false;
            currentState = MachineOperationalState.CommunicationInterrupted;
            healthLevel = DeviceHealthLevel.Warning;
            dataQualityCode ??= "not_collected";
        }
        else if ((now - source.LastCollectedAt.Value).TotalSeconds > Math.Max(_realtimeStateOptions.OfflineAfterSeconds, 3))
        {
            machineOnline = false;
            currentState = MachineOperationalState.CommunicationInterrupted;
            healthLevel = DeviceHealthLevel.Warning;
            dataQualityCode = "stale_snapshot";
            errorMessage ??= $"最近一次采集已超过 {_realtimeStateOptions.OfflineAfterSeconds} 秒，当前按通信中断处理。";
        }

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
            CurrentState = currentState,
            HealthLevel = healthLevel,
            IsEnabled = source.IsEnabled,
            MachineOnline = machineOnline,
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
            DataQualityCode = dataQualityCode,
            LastCollectionError = errorMessage,
        };
    }

    private bool TryNormalizeFormula(FormulaEntity formula)
    {
        var changed = false;
        var parsed = TryParseFormulaSelection(formula.Expression);

        if (string.IsNullOrWhiteSpace(formula.PrimaryVariable))
        {
            formula.PrimaryVariable = parsed?.PrimaryVariable
                ?? (formula.Code == DefaultFormulaCatalog.UtilizationRateCode
                    ? DefaultFormulaCatalog.DefaultUtilizationVariable
                    : DefaultFormulaCatalog.DefaultPowerOnVariable);
            changed = true;
        }

        if (formula.StandardWorkHours <= 0)
        {
            formula.StandardWorkHours = parsed?.StandardWorkHours ?? DefaultFormulaCatalog.DefaultStandardWorkHours;
            changed = true;
        }

        if (formula.Coefficient <= 0)
        {
            formula.Coefficient = parsed?.Coefficient ?? DefaultFormulaCatalog.DefaultCoefficient;
            changed = true;
        }

        var normalizedVisibleOptions = ParseVisibleOptions(formula.VisibleOptionsCsv, formula.PrimaryVariable);
        var normalizedCsv = string.Join(",", normalizedVisibleOptions);
        if (!string.Equals(formula.VisibleOptionsCsv, normalizedCsv, StringComparison.Ordinal))
        {
            formula.VisibleOptionsCsv = normalizedCsv;
            changed = true;
        }

        var normalizedExpression = DefaultFormulaCatalog.BuildExpression(formula.PrimaryVariable, formula.StandardWorkHours, formula.Coefficient);
        if (!string.Equals(formula.Expression, normalizedExpression, StringComparison.Ordinal))
        {
            formula.Expression = normalizedExpression;
            changed = true;
        }

        if (!string.IsNullOrEmpty(formula.ResultUnit))
        {
            formula.ResultUnit = string.Empty;
            changed = true;
        }

        return changed;
    }

    private IReadOnlyList<string> NormalizeVisibleOptions(IReadOnlyList<string>? requestedOptions, string primaryVariable)
    {
        var supported = _formulaEngine.GetSupportedVariableNames();
        var options = requestedOptions?
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(option => supported.Contains(option, StringComparer.OrdinalIgnoreCase))
            .ToList()
            ?? [];

        foreach (var baseOption in DefaultFormulaCatalog.BaseVisibleOptions)
        {
            if (!options.Contains(baseOption, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(baseOption);
            }
        }

        if (!options.Contains(primaryVariable, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(primaryVariable);
        }

        return options
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(option =>
            {
                var index = Array.IndexOf(DefaultFormulaCatalog.BaseVisibleOptions, option);
                return index >= 0 ? index : int.MaxValue;
            })
            .ThenBy(option => option, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseVisibleOptions(string? csv, string primaryVariable)
    {
        var options = (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var baseOption in DefaultFormulaCatalog.BaseVisibleOptions)
        {
            if (!options.Contains(baseOption, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(baseOption);
            }
        }

        if (!string.IsNullOrWhiteSpace(primaryVariable) &&
            !options.Contains(primaryVariable, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(primaryVariable);
        }

        return options.ToArray();
    }

    private static FormulaSelectionSnapshot? TryParseFormulaSelection(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var match = Regex.Match(
            expression.Trim(),
            @"^\(\((?<metric>.+?)\s*/\s*\((?<hours>\d+(?:\.\d+)?)\s*\*\s*60\)\)\s*\*\s*(?<coefficient>\d+(?:\.\d+)?)\)$",
            RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        return new FormulaSelectionSnapshot(
            match.Groups["metric"].Value.Trim(),
            double.Parse(match.Groups["hours"].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(match.Groups["coefficient"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static FormulaDefinitionDto ToFormulaDto(FormulaEntity source)
    {
        return new FormulaDefinitionDto
        {
            Code = source.Code,
            DisplayName = source.DisplayName,
            Description = source.Description,
            Expression = source.Expression,
            PrimaryVariable = source.PrimaryVariable,
            StandardWorkHours = source.StandardWorkHours,
            Coefficient = source.Coefficient,
            VisibleOptions = ParseVisibleOptions(source.VisibleOptionsCsv, source.PrimaryVariable),
            ResultUnit = source.ResultUnit,
            UpdatedAt = source.UpdatedAt,
            UpdatedBy = source.UpdatedBy,
        };
    }

    private sealed record FormulaSelectionSnapshot(string PrimaryVariable, double StandardWorkHours, double Coefficient);

    private static UserDto ToUserDto(UserEntity source)
    {
        return new UserDto
        {
            UserCode = source.UserCode,
            UserName = source.UserName,
            DisplayName = source.DisplayName,
            Department = source.Department,
            RoleCodes = source.Roles
                .Select(role => role.RoleCode)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsEnabled = source.IsEnabled,
            LastLoginAt = source.LastLoginAt,
        };
    }

    private static RoleDto ToRoleDto(RoleEntity source)
    {
        return new RoleDto
        {
            RoleCode = source.RoleCode,
            RoleName = source.RoleName,
            Description = source.Description,
            Permissions = source.Permissions
                .OrderBy(permission => permission.PermissionCode, StringComparer.OrdinalIgnoreCase)
                .Select(permission => PermissionCatalog.Clone(permission.PermissionCode))
                .ToArray(),
        };
    }

    private static IReadOnlyList<TimelineSegmentDto> ToTimelineDtos(
        IReadOnlyList<TimelineSegmentEntity> source,
        DateOnly reportDate,
        DateTimeOffset now)
    {
        var segments = source
            .OrderBy(segment => segment.StartAt.UtcDateTime)
            .Select(segment => new TimelineSegmentDto
            {
                State = segment.State,
                StartAt = segment.StartAt,
                EndAt = segment.EndAt,
                DurationMinutes = segment.DurationMinutes,
                DurationSeconds = (int)Math.Max(0, Math.Round(segment.DurationMinutes * 60d, MidpointRounding.AwayFromZero)),
                DataQualityCode = segment.DataQualityCode,
            })
            .ToList();

        if (reportDate == DateOnly.FromDateTime(now.LocalDateTime) && segments.Count > 0)
        {
            var last = segments[^1];
            if (now > last.EndAt)
            {
                last.EndAt = now;
                last.DurationMinutes = Math.Round((last.EndAt - last.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
                last.DurationSeconds = (int)Math.Max(0, Math.Round(last.DurationMinutes * 60d, MidpointRounding.AwayFromZero));
            }
        }

        return segments;
    }

    private static IReadOnlyList<TimelineSegmentDto> BuildFallbackTimeline(DeviceDto device, DateOnly reportDate, DateTimeOffset now)
    {
        if (reportDate > DateOnly.FromDateTime(now.LocalDateTime))
        {
            return [];
        }

        var startAt = new DateTimeOffset(reportDate.ToDateTime(TimeOnly.MinValue), now.Offset);
        var endAt = reportDate == DateOnly.FromDateTime(now.LocalDateTime)
            ? now
            : new DateTimeOffset(reportDate.ToDateTime(new TimeOnly(23, 59, 59)), now.Offset);
        var state = device.IsEnabled ? MachineOperationalState.CommunicationInterrupted : MachineOperationalState.PowerOff;
        var quality = device.IsEnabled ? "not_collected" : "manual_disabled";

        return
        [
            new TimelineSegmentDto
            {
                State = state,
                StartAt = startAt,
                EndAt = endAt,
                DurationMinutes = Math.Round((endAt - startAt).TotalMinutes, 2, MidpointRounding.AwayFromZero),
                DurationSeconds = (int)Math.Max(0, Math.Round((endAt - startAt).TotalSeconds, MidpointRounding.AwayFromZero)),
                DataQualityCode = quality,
            },
        ];
    }

    private static int ToDateKey(DateOnly date) => (date.Year * 10000) + (date.Month * 100) + date.Day;

    private static void ValidateRequest(DeviceUpsertRequest request)
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

    private static async Task ValidateDeviceCodeUniquenessAsync(
        EnterpriseDbContext dbContext,
        DeviceUpsertRequest request,
        Guid? excludedDeviceId,
        CancellationToken cancellationToken)
    {
        var duplicated = await dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                device => device.DeviceCode == request.DeviceCode.Trim() &&
                          (!excludedDeviceId.HasValue || device.DeviceId != excludedDeviceId.Value),
                cancellationToken);

        if (duplicated is not null)
        {
            throw new InvalidOperationException($"设备编码 {request.DeviceCode.Trim()} 已存在，请使用唯一编码。");
        }
    }

    private static async Task ValidateAgentNetworkAsync(
        EnterpriseDbContext dbContext,
        DeviceUpsertRequest request,
        Guid? excludedDeviceId,
        CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(request.IpAddress.Trim(), out var candidateAddress))
        {
            throw new InvalidOperationException($"设备 IP 地址 {request.IpAddress.Trim()} 格式不正确，无法进行网段校验。");
        }

        if (candidateAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("当前只支持 IPv4 地址校验。");
        }

        var referenceDevice = await dbContext.Devices
            .AsNoTracking()
            .Where(device => device.AgentNodeName == request.AgentNodeName.Trim() &&
                             (!excludedDeviceId.HasValue || device.DeviceId != excludedDeviceId.Value))
            .OrderBy(device => device.DeviceCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (referenceDevice is null)
        {
            return;
        }

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

    private double EvaluateFormulaWithFallback(string expression, string fallbackExpression, IReadOnlyDictionary<string, double> variables)
    {
        try
        {
            return _formulaEngine.Evaluate(expression, variables);
        }
        catch
        {
            return _formulaEngine.Evaluate(fallbackExpression, variables);
        }
    }
}
