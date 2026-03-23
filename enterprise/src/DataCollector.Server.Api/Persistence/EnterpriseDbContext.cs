using DataCollector.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DataCollector.Server.Api.Persistence;

public sealed class EnterpriseDbContext : DbContext
{
    public EnterpriseDbContext(DbContextOptions<EnterpriseDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    public DbSet<FormulaEntity> Formulas => Set<FormulaEntity>();

    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<RoleEntity> Roles => Set<RoleEntity>();

    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();

    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();

    public DbSet<TimelineSegmentEntity> TimelineSegments => Set<TimelineSegmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(item => item.DeviceId);
            entity.HasIndex(item => item.DeviceCode).IsUnique();
            entity.Property(item => item.DepartmentCode).HasMaxLength(64);
            entity.Property(item => item.DepartmentName).HasMaxLength(128);
            entity.Property(item => item.WorkshopCode).HasMaxLength(64);
            entity.Property(item => item.WorkshopName).HasMaxLength(128);
            entity.Property(item => item.DeviceCode).HasMaxLength(128);
            entity.Property(item => item.DeviceName).HasMaxLength(160);
            entity.Property(item => item.Manufacturer).HasMaxLength(160);
            entity.Property(item => item.ControllerModel).HasMaxLength(200);
            entity.Property(item => item.ProtocolName).HasMaxLength(128);
            entity.Property(item => item.IpAddress).HasMaxLength(64);
            entity.Property(item => item.AgentNodeName).HasMaxLength(128);
            entity.Property(item => item.ResponsiblePerson).HasMaxLength(128);
            entity.Property(item => item.CurrentProgramNo).HasMaxLength(64);
            entity.Property(item => item.CurrentProgramName).HasMaxLength(256);
            entity.Property(item => item.CurrentAlarmMessage).HasMaxLength(1024);
            entity.Property(item => item.ControllerModeText).HasMaxLength(64);
            entity.Property(item => item.OeeStatusText).HasMaxLength(64);
            entity.Property(item => item.DataQualityCode).HasMaxLength(64);
            entity.Property(item => item.LastCollectionError).HasMaxLength(1024);
        });

        modelBuilder.Entity<FormulaEntity>(entity =>
        {
            entity.ToTable("formulas");
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.DisplayName).HasMaxLength(64);
            entity.Property(item => item.Description).HasMaxLength(256);
            entity.Property(item => item.Expression).HasMaxLength(512);
            entity.Property(item => item.PrimaryVariable).HasMaxLength(64);
            entity.Property(item => item.VisibleOptionsCsv).HasMaxLength(512);
            entity.Property(item => item.ResultUnit).HasMaxLength(16);
            entity.Property(item => item.UpdatedBy).HasMaxLength(64);
        });

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(item => item.UserCode);
            entity.Property(item => item.UserCode).HasMaxLength(64);
            entity.Property(item => item.UserName).HasMaxLength(64);
            entity.Property(item => item.DisplayName).HasMaxLength(128);
            entity.Property(item => item.Department).HasMaxLength(128);
        });

        modelBuilder.Entity<RoleEntity>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(item => item.RoleCode);
            entity.Property(item => item.RoleCode).HasMaxLength(64);
            entity.Property(item => item.RoleName).HasMaxLength(128);
            entity.Property(item => item.Description).HasMaxLength(256);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(item => new { item.UserCode, item.RoleCode });
            entity.Property(item => item.UserCode).HasMaxLength(64);
            entity.Property(item => item.RoleCode).HasMaxLength(64);
            entity.HasOne(item => item.User)
                .WithMany(item => item.Roles)
                .HasForeignKey(item => item.UserCode)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Role)
                .WithMany(item => item.Users)
                .HasForeignKey(item => item.RoleCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermissionEntity>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(item => new { item.RoleCode, item.PermissionCode });
            entity.Property(item => item.RoleCode).HasMaxLength(64);
            entity.Property(item => item.PermissionCode).HasMaxLength(64);
            entity.HasOne(item => item.Role)
                .WithMany(item => item.Permissions)
                .HasForeignKey(item => item.RoleCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TimelineSegmentEntity>(entity =>
        {
            entity.ToTable("timeline_segments");
            entity.HasKey(item => item.TimelineSegmentId);
            entity.HasIndex(item => new { item.DeviceId, item.ReportDateKey, item.StartAt });
            entity.Property(item => item.DataQualityCode).HasMaxLength(64);
            entity.Property(item => item.AlarmMessage).HasMaxLength(1024);
            entity.HasOne(item => item.Device)
                .WithMany(item => item.TimelineSegments)
                .HasForeignKey(item => item.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class DeviceEntity
{
    public Guid DeviceId { get; set; }

    public string DepartmentCode { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public string WorkshopCode { get; set; } = string.Empty;

    public string WorkshopName { get; set; } = string.Empty;

    public string DeviceCode { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string ControllerModel { get; set; } = string.Empty;

    public string ProtocolName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string AgentNodeName { get; set; } = string.Empty;

    public string? ResponsiblePerson { get; set; }

    public MachineOperationalState CurrentState { get; set; }

    public DeviceHealthLevel HealthLevel { get; set; }

    public bool IsEnabled { get; set; }

    public bool MachineOnline { get; set; }

    public DateTimeOffset LastHeartbeatAt { get; set; }

    public DateTimeOffset? LastCollectedAt { get; set; }

    public string? CurrentProgramNo { get; set; }

    public string? CurrentProgramName { get; set; }

    public int? SpindleSpeedRpm { get; set; }

    public double? SpindleLoadPercent { get; set; }

    public int AutomaticMode { get; set; }

    public int OperationMode { get; set; }

    public bool AlarmState { get; set; }

    public int? CurrentAlarmNumber { get; set; }

    public string? CurrentAlarmMessage { get; set; }

    public bool EmergencyState { get; set; }

    public string? ControllerModeText { get; set; }

    public string? OeeStatusText { get; set; }

    public long? NativePowerOnTotalMs { get; set; }

    public long? NativeOperatingTotalMs { get; set; }

    public long? NativeCuttingTotalMs { get; set; }

    public long? NativeFreeTotalMs { get; set; }

    public string? DataQualityCode { get; set; }

    public string? LastCollectionError { get; set; }

    public List<TimelineSegmentEntity> TimelineSegments { get; set; } = [];
}

public sealed class FormulaEntity
{
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Expression { get; set; } = string.Empty;

    public string PrimaryVariable { get; set; } = string.Empty;

    public double StandardWorkHours { get; set; }

    public double Coefficient { get; set; }

    public string VisibleOptionsCsv { get; set; } = string.Empty;

    public string ResultUnit { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserEntity
{
    public string UserCode { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }

    public List<UserRoleEntity> Roles { get; set; } = [];
}

public sealed class RoleEntity
{
    public string RoleCode { get; set; } = string.Empty;

    public string RoleName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<UserRoleEntity> Users { get; set; } = [];

    public List<RolePermissionEntity> Permissions { get; set; } = [];
}

public sealed class UserRoleEntity
{
    public string UserCode { get; set; } = string.Empty;

    public UserEntity User { get; set; } = null!;

    public string RoleCode { get; set; } = string.Empty;

    public RoleEntity Role { get; set; } = null!;
}

public sealed class RolePermissionEntity
{
    public string RoleCode { get; set; } = string.Empty;

    public RoleEntity Role { get; set; } = null!;

    public string PermissionCode { get; set; } = string.Empty;
}

public sealed class TimelineSegmentEntity
{
    public long TimelineSegmentId { get; set; }

    public Guid DeviceId { get; set; }

    public DeviceEntity Device { get; set; } = null!;

    public int ReportDateKey { get; set; }

    public MachineOperationalState State { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public double DurationMinutes { get; set; }

    public string DataQualityCode { get; set; } = "native_preferred";

    public int? AlarmNumber { get; set; }

    public string? AlarmMessage { get; set; }
}
