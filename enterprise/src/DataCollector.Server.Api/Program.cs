using DataCollector.Core;
using DataCollector.Core.Formula;
using DataCollector.Server.Api.Persistence;
using DataCollector.Server.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var persistenceOptions = builder.Configuration.GetSection("Persistence").Get<PersistenceOptions>() ?? new PersistenceOptions();
var resolvedPersistenceOptions = PersistenceOptionsResolver.Resolve(builder.Environment.ContentRootPath, persistenceOptions);
var realtimeStateOptions = builder.Configuration.GetSection("RealtimeState").Get<RealtimeStateOptions>() ?? new RealtimeStateOptions();

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
builder.Services.AddSingleton(resolvedPersistenceOptions);
builder.Services.AddSingleton(realtimeStateOptions);
builder.Services.AddDbContextFactory<EnterpriseDbContext>(options => options.UseSqlite(resolvedPersistenceOptions.ConnectionString));
builder.Services.AddScoped<IEnterprisePlatformService, DatabaseEnterprisePlatformService>();
builder.Services.AddScoped<IRealtimeIngestionService, DatabaseRealtimeIngestionService>();
builder.Services.AddSingleton<EnterpriseDatabaseInitializer>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<EnterpriseDatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

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
