using DataCollector.Core;
using DataCollector.Core.Formula;
using DataCollector.Server.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Server:Urls"] ?? "http://0.0.0.0:5180");
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "desktop-client",
        policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FormulaEngine>();
builder.Services.AddSingleton<DailyMetricsCalculator>();
builder.Services.AddSingleton<LiveDeviceStateStore>();
builder.Services.AddSingleton<IEnterprisePlatformService, InMemoryEnterprisePlatformService>();

var app = builder.Build();

app.MapOpenApi();
app.UseCors("desktop-client");
app.UseAuthorization();
app.MapGet(
    "/healthz",
    () => Results.Ok(new
    {
        status = "ok",
        service = "dataCollector-enterprise-api",
        timestamp = DateTimeOffset.Now,
    }));
app.MapControllers();

app.Run();
