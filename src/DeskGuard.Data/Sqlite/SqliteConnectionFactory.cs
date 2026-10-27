namespace DeskGuard.Data.Sqlite;

using Microsoft.Data.Sqlite;

public static class SqliteConnectionFactory
{
    private static bool _initialized;
    private static readonly object _initLock = new();

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }

    public static SqliteConnection CreateConnection(string dbPath, bool readOnly = false)
    {
        EnsureInitialized();

        string mode = readOnly ? "ReadOnly" : "ReadWriteCreate";
        string connectionString = $"Data Source={dbPath};Mode={mode};Cache=Private";
        SqliteConnection connection = new(connectionString);
        connection.Open();

        ApplyPragmas(connection, readOnly);
        return connection;
    }

    public static void ApplyPragmas(SqliteConnection connection, bool readOnly)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        if (readOnly)
        {
            cmd.CommandText = "PRAGMA busy_timeout = 5000;";
            cmd.ExecuteNonQuery();
            return;
        }

        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA cache_size = -64000;
            PRAGMA temp_store = MEMORY;
        """;
        cmd.ExecuteNonQuery();
    }
}
