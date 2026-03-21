using DataCollector.Contracts;

namespace DataCollector.Server.Api.Services;

public interface IEnterprisePlatformService
{
    Task<DeviceManagementOverviewDto> GetDeviceManagementOverviewAsync(CancellationToken cancellationToken);

    Task<DeviceDto> SaveDeviceAsync(DeviceUpsertRequest request, CancellationToken cancellationToken);

    Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    Task RenameDepartmentAsync(string departmentCode, string newName, CancellationToken cancellationToken);

    Task RenameWorkshopAsync(string workshopCode, string newName, CancellationToken cancellationToken);

    Task RenameDeviceAsync(Guid deviceId, string newName, CancellationToken cancellationToken);

    Task<DailyReportResponse> GetDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormulaDefinitionDto>> GetFormulasAsync(CancellationToken cancellationToken);

    Task<FormulaDefinitionDto> UpdateFormulaAsync(string code, FormulaUpdateRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormulaVariableOptionDto>> GetFormulaVariableOptionsAsync(CancellationToken cancellationToken);

    Task<DeviceTimelineResponse> GetDeviceTimelineAsync(Guid deviceId, DateOnly reportDate, CancellationToken cancellationToken);

    Task<AgentRuntimeConfigurationDto> GetAgentRuntimeConfigurationAsync(string agentNodeName, CancellationToken cancellationToken);

    Task<SecurityOverviewDto> GetSecurityOverviewAsync(CancellationToken cancellationToken);

    Task<UserDto> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken);

    Task DeleteUserAsync(string userCode, CancellationToken cancellationToken);

    Task<RoleDto> SaveRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken);

    Task DeleteRoleAsync(string roleCode, CancellationToken cancellationToken);
}
