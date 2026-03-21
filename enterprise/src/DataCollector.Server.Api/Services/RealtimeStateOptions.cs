namespace DataCollector.Server.Api.Services;

public sealed class RealtimeStateOptions
{
    public int OfflineAfterSeconds { get; set; } = 10;
}
