namespace DataCollector.Server.Api.Persistence;

public sealed class PersistenceOptions
{
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=data\\enterprise.db";

    public bool AutoInitialize { get; set; } = true;
}
