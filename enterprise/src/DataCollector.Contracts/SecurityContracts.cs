namespace DataCollector.Contracts;

public sealed class PermissionDto
{
    public required string PermissionCode { get; set; }

    public required string PermissionName { get; set; }

    public required string Description { get; set; }
}

public sealed class RoleDto
{
    public required string RoleCode { get; set; }

    public required string RoleName { get; set; }

    public required string Description { get; set; }

    public required IReadOnlyList<PermissionDto> Permissions { get; set; }
}

public sealed class UserDto
{
    public required string UserCode { get; set; }

    public required string UserName { get; set; }

    public required string DisplayName { get; set; }

    public required string Department { get; set; }

    public required IReadOnlyList<string> RoleCodes { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }
}

public sealed class SecurityOverviewDto
{
    public required IReadOnlyList<UserDto> Users { get; set; }

    public required IReadOnlyList<RoleDto> Roles { get; set; }

    public required IReadOnlyList<PermissionDto> Permissions { get; set; }
}
