namespace DataCollector.Server.Api.Persistence;

public sealed class ResolvedPersistenceOptions
{
    public required string Provider { get; init; }

    public required string ConnectionString { get; init; }

    public required string DatabasePath { get; init; }

    public bool AutoInitialize { get; init; }
}
