using DataCollector.Agent.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddWindowsService(options => options.ServiceName = "DataCollector Enterprise Agent");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
