using DataCollector.Contracts;
using DataCollector.Core;
using DataCollector.Core.Formula;

namespace DataCollector.Server.Api.Services;

public sealed class InMemoryEnterprisePlatformService : IEnterprisePlatformService
{
    private static readonly (string WorkshopCode, string WorkshopName)[] WorkshopSeed =
    [
        ("W01", "一车间"),
        ("W02", "二车间"),
        ("W03", "三车间"),
        ("W04", "四车间"),
        ("W05", "五车间"),
    ];

    private readonly object _gate = new();
    private readonly FormulaEngine _formulaEngine;
    private readonly DailyMetricsCalculator _dailyMetricsCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly List<DeviceDto> _devices;
    private readonly Dictionary<string, FormulaDefinitionDto> _formulas;
    private readonly SecurityOverviewDto _securityOverview;

    public InMemoryEnterprisePlatformService(
        FormulaEngine formulaEngine,
        DailyMetricsCalculator dailyMetricsCalculator,
        TimeProvider timeProvider)
    {
        _formulaEngine = formulaEngine;
        _dailyMetricsCalculator = dailyMetricsCalculator;
        _timeProvider = timeProvider;
        _devices = BuildSeedDevices();
        _formulas = BuildSeedFormulas();
        _securityOverview = BuildSecurityOverview();
    }

    public Task<DeviceManagementOverviewDto> GetDeviceManagementOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshotAt = _timeProvider.GetLocalNow();
        var devices = BuildDeviceSnapshots(snapshotAt);
        var workshops = devices
            .GroupBy(device => new { device.WorkshopCode, device.WorkshopName })
            .OrderBy(group => group.Key.WorkshopCode)
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

        lock (_gate)
        {
            var now = _timeProvider.GetLocalNow();
            var existing = request.DeviceId.HasValue
                ? _devices.FirstOrDefault(device => device.DeviceId == request.DeviceId.Value)
                : null;

            if (existing is null)
            {
                existing = new DeviceDto
                {
                    DeviceId = request.DeviceId ?? Guid.NewGuid(),
                    WorkshopCode = request.WorkshopCode,
                    WorkshopName = request.WorkshopName,
                    DeviceCode = request.DeviceCode,
                    DeviceName = request.DeviceName,
                    Manufacturer = request.Manufacturer,
                    ControllerModel = request.ControllerModel,
                    ProtocolName = request.ProtocolName,
                    IpAddress = request.IpAddress,
                    Port = request.Port,
                    AgentNodeName = request.AgentNodeName,
                    CurrentState = MachineOperationalState.Standby,
                    HealthLevel = DeviceHealthLevel.Normal,
                    IsEnabled = request.IsEnabled,
                    LastHeartbeatAt = now,
                    CurrentProgramNo = "O0001",
                    CurrentProgramName = "新设备默认程序",
                };
                _devices.Add(existing);
            }
            else
            {
                existing.WorkshopCode = request.WorkshopCode;
                existing.WorkshopName = request.WorkshopName;
                existing.DeviceCode = request.DeviceCode;
                existing.DeviceName = request.DeviceName;
                existing.Manufacturer = request.Manufacturer;
                existing.ControllerModel = request.ControllerModel;
                existing.ProtocolName = request.ProtocolName;
                existing.IpAddress = request.IpAddress;
                existing.Port = request.Port;
                existing.AgentNodeName = request.AgentNodeName;
                existing.IsEnabled = request.IsEnabled;
                existing.LastHeartbeatAt = now;
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

    public Task<DailyReportResponse> GetDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetLocalNow();
        var devices = BuildDeviceSnapshots(now);
        var formulas = _formulas.Values.OrderBy(formula => formula.Code).Select(CloneFormula).ToArray();
        var powerOnFormula = _formulas["power_on_rate"];
        var utilizationFormula = _formulas["utilization_rate"];

        var rows = devices
            .OrderBy(device => device.WorkshopCode)
            .ThenBy(device => device.DeviceCode)
            .Select(device =>
            {
                var timeline = BuildTimeline(device, reportDate, now);
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
                    DataQualityCode = "native_timer_first",
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
            _formulas.Values.OrderBy(formula => formula.Code).Select(CloneFormula).ToArray());
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

            existing.Expression = request.Expression;
            existing.UpdatedBy = request.UpdatedBy;
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
        var segments = BuildTimeline(device, reportDate, now);
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

    public Task<SecurityOverviewDto> GetSecurityOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_securityOverview);
    }

    private List<DeviceDto> BuildDeviceSnapshots(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _devices.Select(device =>
            {
                var clone = CloneDevice(device);
                clone.LastHeartbeatAt = now.AddSeconds(-(Math.Abs(device.DeviceCode.GetHashCode()) % 50));
                clone.CurrentState = ResolveCurrentState(clone, now);
                clone.HealthLevel = clone.CurrentState switch
                {
                    MachineOperationalState.Alarm or MachineOperationalState.Emergency or MachineOperationalState.CommunicationInterrupted => DeviceHealthLevel.Critical,
                    MachineOperationalState.PowerOff => DeviceHealthLevel.Warning,
                    _ => DeviceHealthLevel.Normal,
                };
                clone.CurrentProgramNo = clone.CurrentState is MachineOperationalState.Processing or MachineOperationalState.Waiting
                    ? $"O{Math.Abs(clone.DeviceCode.GetHashCode()) % 9000 + 1000:0000}"
                    : null;
                clone.CurrentProgramName = clone.CurrentProgramNo is null ? null : "转轴/外圆复合加工";
                return clone;
            }).ToList();
        }
    }

    private MachineOperationalState ResolveCurrentState(DeviceDto device, DateTimeOffset now)
    {
        if (!device.IsEnabled)
        {
            return MachineOperationalState.PowerOff;
        }

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var segment = BuildTimeline(device, today, now).FirstOrDefault(item => item.StartAt <= now && now < item.EndAt);
        return segment?.State ?? MachineOperationalState.PowerOff;
    }

    private IReadOnlyList<TimelineSegmentDto> BuildTimeline(DeviceDto device, DateOnly reportDate, DateTimeOffset now)
    {
        var offset = now.Offset;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (reportDate > today)
        {
            return [];
        }

        var patternIndex = Math.Abs(HashCode.Combine(device.DeviceCode, reportDate.DayNumber)) % 4;
        var pattern = patternIndex switch
        {
            0 => GetPatternA(),
            1 => GetPatternB(),
            2 => GetPatternC(),
            _ => GetPatternD(),
        };

        var windowEnd = reportDate == today
            ? now
            : new DateTimeOffset(reportDate.ToDateTime(new TimeOnly(23, 59, 59)), offset);

        var segments = new List<TimelineSegmentDto>();
        foreach (var entry in pattern)
        {
            var start = new DateTimeOffset(reportDate.ToDateTime(entry.Start), offset);
            var end = new DateTimeOffset(reportDate.ToDateTime(entry.End), offset);
            if (end <= start || start >= windowEnd)
            {
                continue;
            }

            if (end > windowEnd)
            {
                end = windowEnd;
            }

            segments.Add(new TimelineSegmentDto
            {
                State = entry.State,
                StartAt = start,
                EndAt = end,
                DurationMinutes = Math.Round((end - start).TotalMinutes, 2, MidpointRounding.AwayFromZero),
                DataQualityCode = entry.State == MachineOperationalState.CommunicationInterrupted ? "network_gap_isolated" : "native_timer_first",
            });
        }

        if (segments.Count == 0)
        {
            var start = new DateTimeOffset(reportDate.ToDateTime(TimeOnly.MinValue), offset);
            segments.Add(new TimelineSegmentDto
            {
                State = MachineOperationalState.PowerOff,
                StartAt = start,
                EndAt = windowEnd,
                DurationMinutes = Math.Round((windowEnd - start).TotalMinutes, 2, MidpointRounding.AwayFromZero),
                DataQualityCode = "native_timer_first",
            });
        }

        return MergeAdjacentSegments(segments);
    }

    private static IReadOnlyList<TimelineSegmentDto> MergeAdjacentSegments(IReadOnlyList<TimelineSegmentDto> segments)
    {
        var merged = new List<TimelineSegmentDto>();
        foreach (var segment in segments.OrderBy(item => item.StartAt))
        {
            var last = merged.LastOrDefault();
            if (last is not null && last.State == segment.State && last.EndAt == segment.StartAt)
            {
                last.EndAt = segment.EndAt;
                last.DurationMinutes = Math.Round((last.EndAt - last.StartAt).TotalMinutes, 2, MidpointRounding.AwayFromZero);
                continue;
            }

            merged.Add(new TimelineSegmentDto
            {
                State = segment.State,
                StartAt = segment.StartAt,
                EndAt = segment.EndAt,
                DurationMinutes = segment.DurationMinutes,
                DataQualityCode = segment.DataQualityCode,
            });
        }

        return merged;
    }

    private static List<DeviceDto> BuildSeedDevices()
    {
        var devices = new List<DeviceDto>();
        var controllers = new[]
        {
            "FANUC Series 0i Mate-TC",
            "FANUC Series 0i-T",
            "FANUC Series 0i-TF",
            "FANUC Series 0i-TF Plus",
            "FANUC Series 0i Mate-TD",
        };

        var deviceIndex = 1;
        foreach (var workshop in WorkshopSeed)
        {
            for (var machineIndex = 1; machineIndex <= 3; machineIndex++)
            {
                devices.Add(new DeviceDto
                {
                    DeviceId = Guid.NewGuid(),
                    WorkshopCode = workshop.WorkshopCode,
                    WorkshopName = workshop.WorkshopName,
                    DeviceCode = $"{workshop.WorkshopCode}-CNC-{machineIndex:00}",
                    DeviceName = $"{workshop.WorkshopName}机床 {machineIndex:00}",
                    Manufacturer = "FANUC 车削设备",
                    ControllerModel = controllers[(deviceIndex - 1) % controllers.Length],
                    ProtocolName = "FOCAS over Ethernet",
                    IpAddress = $"192.168.{90 + deviceIndex / 3}.{40 + machineIndex}",
                    Port = 8193,
                    AgentNodeName = $"{workshop.WorkshopName}-Agent",
                    CurrentState = MachineOperationalState.Standby,
                    HealthLevel = DeviceHealthLevel.Normal,
                    IsEnabled = true,
                    LastHeartbeatAt = DateTimeOffset.Now,
                });
                deviceIndex++;
            }
        }

        return devices;
    }

    private static Dictionary<string, FormulaDefinitionDto> BuildSeedFormulas()
    {
        return new Dictionary<string, FormulaDefinitionDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["power_on_rate"] = new FormulaDefinitionDto
            {
                Code = "power_on_rate",
                DisplayName = "开机率",
                Description = "默认按当天已观测时长计算开机率。",
                Expression = "(power_on_minutes / observed_minutes) * 100",
                ResultUnit = "%",
                UpdatedAt = DateTimeOffset.Now,
                UpdatedBy = "system",
            },
            ["utilization_rate"] = new FormulaDefinitionDto
            {
                Code = "utilization_rate",
                DisplayName = "利用率",
                Description = "默认按开机时间中的加工占比计算利用率。",
                Expression = "(processing_minutes / power_on_minutes) * 100",
                ResultUnit = "%",
                UpdatedAt = DateTimeOffset.Now,
                UpdatedBy = "system",
            },
        };
    }

    private static SecurityOverviewDto BuildSecurityOverview()
    {
        var permissions = new[]
        {
            new PermissionDto { PermissionCode = "device.read", PermissionName = "查看设备", Description = "查看设备列表、状态、时间线" },
            new PermissionDto { PermissionCode = "device.write", PermissionName = "维护设备", Description = "增删改设备和采集点" },
            new PermissionDto { PermissionCode = "report.read", PermissionName = "查看报表", Description = "查看日报、利用率和开机率" },
            new PermissionDto { PermissionCode = "formula.write", PermissionName = "维护公式", Description = "修改开机率和利用率公式" },
            new PermissionDto { PermissionCode = "security.write", PermissionName = "维护权限", Description = "管理用户、角色和权限" },
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
            new UserDto
            {
                UserCode = "it_ops_01",
                UserName = "it.ops",
                DisplayName = "IT 运维",
                Department = "信息部",
                RoleCodes = ["itops"],
                IsEnabled = true,
                LastLoginAt = DateTimeOffset.Now.AddMinutes(-32),
            },
            new UserDto
            {
                UserCode = "mgr_w01",
                UserName = "w01.manager",
                DisplayName = "一车间主管",
                Department = "一车间",
                RoleCodes = ["manager"],
                IsEnabled = true,
                LastLoginAt = DateTimeOffset.Now.AddHours(-2),
            },
        };

        return new SecurityOverviewDto
        {
            Users = users,
            Roles = roles,
            Permissions = permissions,
        };
    }

    private static DeviceDto CloneDevice(DeviceDto source)
    {
        return new DeviceDto
        {
            DeviceId = source.DeviceId,
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
            CurrentState = source.CurrentState,
            HealthLevel = source.HealthLevel,
            IsEnabled = source.IsEnabled,
            LastHeartbeatAt = source.LastHeartbeatAt,
            CurrentProgramNo = source.CurrentProgramNo,
            CurrentProgramName = source.CurrentProgramName,
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

    private static IReadOnlyList<PatternEntry> GetPatternA() =>
    [
        new PatternEntry(new TimeOnly(0, 0), new TimeOnly(7, 30), MachineOperationalState.PowerOff),
        new PatternEntry(new TimeOnly(7, 30), new TimeOnly(8, 0), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(8, 0), new TimeOnly(10, 30), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(10, 30), new TimeOnly(10, 45), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(10, 45), new TimeOnly(12, 0), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(12, 0), new TimeOnly(13, 0), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(13, 0), new TimeOnly(15, 20), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(15, 20), new TimeOnly(15, 35), MachineOperationalState.Alarm),
        new PatternEntry(new TimeOnly(15, 35), new TimeOnly(17, 30), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(17, 30), new TimeOnly(18, 0), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(18, 0), new TimeOnly(23, 59, 59), MachineOperationalState.PowerOff),
    ];

    private static IReadOnlyList<PatternEntry> GetPatternB() =>
    [
        new PatternEntry(new TimeOnly(0, 0), new TimeOnly(7, 0), MachineOperationalState.PowerOff),
        new PatternEntry(new TimeOnly(7, 0), new TimeOnly(8, 15), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(8, 15), new TimeOnly(11, 10), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(11, 10), new TimeOnly(11, 50), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(11, 50), new TimeOnly(12, 20), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(12, 20), new TimeOnly(13, 30), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(13, 30), new TimeOnly(16, 20), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(16, 20), new TimeOnly(17, 0), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(17, 0), new TimeOnly(20, 0), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(20, 0), new TimeOnly(23, 59, 59), MachineOperationalState.PowerOff),
    ];

    private static IReadOnlyList<PatternEntry> GetPatternC() =>
    [
        new PatternEntry(new TimeOnly(0, 0), new TimeOnly(8, 0), MachineOperationalState.PowerOff),
        new PatternEntry(new TimeOnly(8, 0), new TimeOnly(9, 20), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(9, 20), new TimeOnly(11, 40), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(11, 40), new TimeOnly(11, 55), MachineOperationalState.Emergency),
        new PatternEntry(new TimeOnly(11, 55), new TimeOnly(12, 35), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(12, 35), new TimeOnly(14, 55), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(14, 55), new TimeOnly(15, 0), MachineOperationalState.CommunicationInterrupted),
        new PatternEntry(new TimeOnly(15, 0), new TimeOnly(17, 45), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(17, 45), new TimeOnly(18, 20), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(18, 20), new TimeOnly(23, 59, 59), MachineOperationalState.PowerOff),
    ];

    private static IReadOnlyList<PatternEntry> GetPatternD() =>
    [
        new PatternEntry(new TimeOnly(0, 0), new TimeOnly(6, 50), MachineOperationalState.PowerOff),
        new PatternEntry(new TimeOnly(6, 50), new TimeOnly(7, 40), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(7, 40), new TimeOnly(9, 50), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(9, 50), new TimeOnly(10, 40), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(10, 40), new TimeOnly(12, 10), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(12, 10), new TimeOnly(13, 30), MachineOperationalState.PowerOff),
        new PatternEntry(new TimeOnly(13, 30), new TimeOnly(14, 0), MachineOperationalState.Standby),
        new PatternEntry(new TimeOnly(14, 0), new TimeOnly(16, 15), MachineOperationalState.Processing),
        new PatternEntry(new TimeOnly(16, 15), new TimeOnly(17, 45), MachineOperationalState.Waiting),
        new PatternEntry(new TimeOnly(17, 45), new TimeOnly(23, 59, 59), MachineOperationalState.PowerOff),
    ];

    private sealed record PatternEntry(TimeOnly Start, TimeOnly End, MachineOperationalState State);
}
