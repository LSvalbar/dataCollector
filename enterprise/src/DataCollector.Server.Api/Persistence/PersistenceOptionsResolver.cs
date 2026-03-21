using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DataCollector.Server.Api.Persistence;

public static class PersistenceOptionsResolver
{
    public static ResolvedPersistenceOptions Resolve(string contentRootPath, PersistenceOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"当前仅支持 Sqlite 持久化，配置值 {options.Provider} 尚未启用。");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Persistence:ConnectionString 不能为空。");
        }

        var builder = new SqliteConnectionStringBuilder(options.ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("Sqlite 连接字符串缺少 Data Source。");
        }

        var databasePath = builder.DataSource;
        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.GetFullPath(Path.Combine(contentRootPath, databasePath));
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        builder.DataSource = databasePath;
        return new ResolvedPersistenceOptions
        {
            Provider = "Sqlite",
            ConnectionString = builder.ConnectionString,
            DatabasePath = databasePath,
            AutoInitialize = options.AutoInitialize,
        };
    }
}
