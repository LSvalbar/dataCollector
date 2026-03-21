using System.Net.Http;
using System.Net.Http.Json;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf.Services;

public sealed class EnterpriseApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public EnterpriseApiClient(string? baseAddress = null)
    {
        BaseAddress = baseAddress
            ?? Environment.GetEnvironmentVariable("DATACOLLECTOR_API_URL")
            ?? "http://localhost:5180";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseAddress, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public string BaseAddress { get; }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/healthz", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<DeviceManagementOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<DeviceManagementOverviewDto>("/api/device-management/overview", cancellationToken);
    }

    public Task<DailyReportResponse?> GetDailyReportAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<DailyReportResponse>($"/api/reports/daily?date={date:yyyy-MM-dd}", cancellationToken);
    }

    public Task<IReadOnlyList<FormulaDefinitionDto>?> GetFormulasAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<IReadOnlyList<FormulaDefinitionDto>>("/api/reports/formulas", cancellationToken);
    }

    public Task<IReadOnlyList<FormulaVariableOptionDto>?> GetFormulaOptionsAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<IReadOnlyList<FormulaVariableOptionDto>>("/api/reports/formula-options", cancellationToken);
    }

    public async Task<FormulaDefinitionDto?> UpdateFormulaAsync(string code, FormulaUpdateRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/reports/formulas/{code}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<FormulaDefinitionDto>(cancellationToken);
    }

    public Task<DeviceTimelineResponse?> GetTimelineAsync(Guid deviceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<DeviceTimelineResponse>($"/api/timeline/devices/{deviceId}?date={date:yyyy-MM-dd}", cancellationToken);
    }

    public Task<SecurityOverviewDto?> GetSecurityOverviewAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<SecurityOverviewDto>("/api/security/overview", cancellationToken);
    }

    public async Task<UserDto?> SaveUserAsync(UserUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/security/users", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken);
    }

    public async Task DeleteUserAsync(string userCode, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/security/users/{userCode}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RoleDto?> SaveRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/security/roles", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RoleDto>(cancellationToken);
    }

    public async Task DeleteRoleAsync(string roleCode, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/security/roles/{roleCode}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<DeviceDto?> CreateDeviceAsync(DeviceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/device-management/devices", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DeviceDto>(cancellationToken);
    }

    public async Task<DeviceDto?> UpdateDeviceAsync(Guid deviceId, DeviceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/device-management/devices/{deviceId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DeviceDto>(cancellationToken);
    }

    public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/device-management/devices/{deviceId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RenameDepartmentAsync(string departmentCode, string newName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/device-management/departments/{departmentCode}/rename",
            new NameChangeRequest { NewName = newName },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RenameWorkshopAsync(string workshopCode, string newName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/device-management/workshops/{workshopCode}/rename",
            new NameChangeRequest { NewName = newName },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RenameDeviceAsync(Guid deviceId, string newName, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            $"/api/device-management/devices/{deviceId}/rename",
            new NameChangeRequest { NewName = newName },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            response.EnsureSuccessStatusCode();
            return;
        }

        throw new InvalidOperationException(message.Trim().Trim('"'));
    }
}
