using DataCollector.Server.Api.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Services;

public sealed class EnterpriseDatabaseInitializer
{
    private readonly IDbContextFactory<EnterpriseDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ResolvedPersistenceOptions _persistenceOptions;
    private readonly ILogger<EnterpriseDatabaseInitializer> _logger;

    public EnterpriseDatabaseInitializer(
        IDbContextFactory<EnterpriseDbContext> dbContextFactory,
        TimeProvider timeProvider,
        ResolvedPersistenceOptions persistenceOptions,
        ILogger<EnterpriseDatabaseInitializer> logger)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _persistenceOptions = persistenceOptions;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_persistenceOptions.AutoInitialize)
        {
            return;
        }

        await EnsureCompatibleDatabaseAsync(cancellationToken);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await SeedDefaultsAsync(dbContext, cancellationToken);
    }

    private async Task EnsureCompatibleDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_persistenceOptions.DatabasePath))
        {
            return;
        }

        await using var connection = new SqliteConnection(_persistenceOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (await IsSchemaCompatibleAsync(connection, cancellationToken))
        {
            return;
        }

        await connection.CloseAsync();
        var backupPath = $"{_persistenceOptions.DatabasePath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(_persistenceOptions.DatabasePath, backupPath, overwrite: true);
        File.Delete(_persistenceOptions.DatabasePath);
        _logger.LogWarning("检测到旧版或不兼容数据库结构，已备份到 {BackupPath} 并重建新库。", backupPath);
    }

    private static async Task<bool> IsSchemaCompatibleAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var requiredSchema = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["devices"] =
            [
                "DeviceId", "DepartmentCode", "DepartmentName", "WorkshopCode", "WorkshopName",
                "DeviceCode", "DeviceName", "Manufacturer", "ControllerModel", "ProtocolName",
                "IpAddress", "Port", "AgentNodeName", "ResponsiblePerson", "CurrentState",
                "HealthLevel", "IsEnabled", "MachineOnline", "LastHeartbeatAt", "LastCollectedAt",
                "CurrentProgramNo", "CurrentProgramName", "CurrentDrawingNumber", "SpindleSpeedRpm", "SpindleLoadPercent",
                "AutomaticMode", "OperationMode", "AlarmState", "CurrentAlarmNumber", "CurrentAlarmMessage", "EmergencyState", "ControllerModeText",
                "OeeStatusText", "NativePowerOnTotalMs", "NativeOperatingTotalMs", "NativeCuttingTotalMs",
                "NativeFreeTotalMs", "DataQualityCode", "LastCollectionError"
            ],
            ["formulas"] =
            [
                "Code", "DisplayName", "Description", "Expression", "PrimaryVariable",
                "StandardWorkHours", "Coefficient", "VisibleOptionsCsv", "ResultUnit",
                "UpdatedBy", "UpdatedAt",
            ],
            ["users"] = ["UserCode", "UserName", "DisplayName", "Department", "IsEnabled", "LastLoginAt"],
            ["roles"] = ["RoleCode", "RoleName", "Description"],
            ["user_roles"] = ["UserCode", "RoleCode"],
            ["role_permissions"] = ["RoleCode", "PermissionCode"],
            ["timeline_segments"] = ["TimelineSegmentId", "DeviceId", "ReportDateKey", "State", "StartAt", "EndAt", "DurationMinutes", "DataQualityCode", "ProgramNo", "DrawingNumber", "AlarmNumber", "AlarmMessage"],
        };

        foreach (var table in requiredSchema)
        {
            var columns = await ReadColumnNamesAsync(connection, table.Key, cancellationToken);
            if (columns.Count == 0)
            {
                return false;
            }

            if (table.Value.Any(requiredColumn => !columns.Contains(requiredColumn)))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var escapedTableName = tableName.Replace("'", "''");
        command.CommandText = $"PRAGMA table_info('{escapedTableName}')";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1))
            {
                columns.Add(reader.GetString(1));
            }
        }

        return columns;
    }

    private async Task SeedDefaultsAsync(EnterpriseDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        var formulas = await dbContext.Formulas.ToDictionaryAsync(item => item.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        if (!formulas.ContainsKey(DefaultFormulaCatalog.PowerOnRateCode))
        {
            dbContext.Formulas.Add(DefaultFormulaCatalog.CreatePowerOnRate(now));
        }

        if (!formulas.ContainsKey(DefaultFormulaCatalog.UtilizationRateCode))
        {
            dbContext.Formulas.Add(DefaultFormulaCatalog.CreateUtilizationRate(now));
        }

        if (!await dbContext.Roles.AnyAsync(cancellationToken))
        {
            var roles = new[]
            {
                new RoleEntity
                {
                    RoleCode = "admin",
                    RoleName = "系统管理员",
                    Description = "负责系统配置、权限和设备维护。",
                },
                new RoleEntity
                {
                    RoleCode = "manager",
                    RoleName = "车间主管",
                    Description = "负责查看车间状态、报表和设备时间线。",
                },
                new RoleEntity
                {
                    RoleCode = "itops",
                    RoleName = "IT 运维",
                    Description = "负责 Agent、服务端和设备连通性维护。",
                },
            };
            dbContext.Roles.AddRange(roles);
            dbContext.RolePermissions.AddRange(
                new RolePermissionEntity { RoleCode = "admin", PermissionCode = "device.read" },
                new RolePermissionEntity { RoleCode = "admin", PermissionCode = "device.write" },
                new RolePermissionEntity { RoleCode = "admin", PermissionCode = "report.read" },
                new RolePermissionEntity { RoleCode = "admin", PermissionCode = "formula.write" },
                new RolePermissionEntity { RoleCode = "admin", PermissionCode = "security.write" },
                new RolePermissionEntity { RoleCode = "manager", PermissionCode = "device.read" },
                new RolePermissionEntity { RoleCode = "manager", PermissionCode = "report.read" },
                new RolePermissionEntity { RoleCode = "itops", PermissionCode = "device.read" },
                new RolePermissionEntity { RoleCode = "itops", PermissionCode = "device.write" },
                new RolePermissionEntity { RoleCode = "itops", PermissionCode = "report.read" },
                new RolePermissionEntity { RoleCode = "itops", PermissionCode = "formula.write" });
        }

        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            dbContext.Users.Add(new UserEntity
            {
                UserCode = "admin",
                UserName = "admin",
                DisplayName = "系统管理员",
                Department = "信息部",
                IsEnabled = true,
                LastLoginAt = now.AddMinutes(-15),
            });
            dbContext.UserRoles.Add(new UserRoleEntity
            {
                UserCode = "admin",
                RoleCode = "admin",
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
