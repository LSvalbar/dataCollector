using DataCollector.Contracts;

namespace DataCollector.Server.Api.Services;

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDto> All { get; } =
    [
        new PermissionDto { PermissionCode = "device.read", PermissionName = "查看设备", Description = "查看设备列表、实时状态和时间线。" },
        new PermissionDto { PermissionCode = "device.write", PermissionName = "维护设备", Description = "维护部门、车间和设备主数据。" },
        new PermissionDto { PermissionCode = "report.read", PermissionName = "查看报表", Description = "查看日报、开机率和利用率。" },
        new PermissionDto { PermissionCode = "formula.write", PermissionName = "维护公式", Description = "修改开机率和利用率公式。" },
        new PermissionDto { PermissionCode = "security.write", PermissionName = "维护权限", Description = "管理用户、角色和权限。" },
    ];

    public static PermissionDto Clone(string permissionCode)
    {
        var permission = All.FirstOrDefault(item => item.PermissionCode.Equals(permissionCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"未定义权限 {permissionCode}。");

        return new PermissionDto
        {
            PermissionCode = permission.PermissionCode,
            PermissionName = permission.PermissionName,
            Description = permission.Description,
        };
    }
}
