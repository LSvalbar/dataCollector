using DataCollector.Server.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Services;

public sealed class EnterpriseDatabaseInitializer
{
    private readonly IDbContextFactory<EnterpriseDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ResolvedPersistenceOptions _persistenceOptions;

    public EnterpriseDatabaseInitializer(
        IDbContextFactory<EnterpriseDbContext> dbContextFactory,
        TimeProvider timeProvider,
        ResolvedPersistenceOptions persistenceOptions)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _persistenceOptions = persistenceOptions;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_persistenceOptions.AutoInitialize)
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await SeedDefaultsAsync(dbContext, cancellationToken);
    }

    private async Task SeedDefaultsAsync(EnterpriseDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        if (!await dbContext.Formulas.AnyAsync(cancellationToken))
        {
            dbContext.Formulas.AddRange(
                new FormulaEntity
                {
                    Code = "power_on_rate",
                    DisplayName = "开机率",
                    Description = "默认按当天已观测时长计算开机率。",
                    Expression = "(开机时间 / 已观测时间) * 100",
                    ResultUnit = "%",
                    UpdatedAt = now,
                    UpdatedBy = "system",
                },
                new FormulaEntity
                {
                    Code = "utilization_rate",
                    DisplayName = "利用率",
                    Description = "默认按开机时间中的加工占比计算利用率。",
                    Expression = "(加工时间 / 开机时间) * 100",
                    ResultUnit = "%",
                    UpdatedAt = now,
                    UpdatedBy = "system",
                });
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
