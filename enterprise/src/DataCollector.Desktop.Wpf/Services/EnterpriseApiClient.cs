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

    public async Task<FormulaDefinitionDto?> UpdateFormulaAsync(string code, FormulaUpdateRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/reports/formulas/{code}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
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

    public async Task<DeviceDto?> CreateDeviceAsync(DeviceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/device-management/devices", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceDto>(cancellationToken);
    }

    public async Task<DeviceDto?> UpdateDeviceAsync(Guid deviceId, DeviceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync($"/api/device-management/devices/{deviceId}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeviceDto>(cancellationToken);
    }

    public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/device-management/devices/{deviceId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
