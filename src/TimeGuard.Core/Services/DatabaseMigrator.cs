using Microsoft.Data.Sqlite;

namespace TimeGuard.Services;

/// <summary>
/// Creates the SQLite schema on first run and applies future migrations.
/// Call Migrate() once at application startup before any other DB access.
/// </summary>
public class DatabaseMigrator
{
    private readonly string _connectionString;

    public DatabaseMigrator(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Migrate()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Enable WAL mode for better concurrent read performance
        Execute(conn, "PRAGMA journal_mode=WAL;");

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS AppRules (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                ProcessName         TEXT    NOT NULL,
                DisplayName         TEXT    NOT NULL,
                DailyLimitMins      INTEGER NOT NULL DEFAULT 0,
                WindowStart         TEXT,
                WindowEnd           TEXT,
                BreakEveryMins      INTEGER NOT NULL DEFAULT 0,
                BreakDurationMins   INTEGER NOT NULL DEFAULT 0,
                Enabled             INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS DailyUsage (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Date        TEXT    NOT NULL,
                ProcessName TEXT    NOT NULL,
                UsageMins   REAL    NOT NULL DEFAULT 0,
                Blocked     INTEGER NOT NULL DEFAULT 0,
                WarningSent INTEGER NOT NULL DEFAULT 0,
                UNIQUE(Date, ProcessName)
            );

            CREATE TABLE IF NOT EXISTS Settings_v1_applied (
                Id INTEGER PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS Sessions (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                ProcessName         TEXT    NOT NULL,
                StartTime           TEXT    NOT NULL,
                EndTime             TEXT,
                TimeSinceBreakMins  REAL    NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AppRuleDaySchedules (
                RuleId          INTEGER NOT NULL,
                DayOfWeek       INTEGER NOT NULL,
                DailyLimitMins  INTEGER NOT NULL DEFAULT 0,
                WindowStart     TEXT,
                WindowEnd       TEXT,
                PRIMARY KEY (RuleId, DayOfWeek)
            );

            CREATE INDEX IF NOT EXISTS idx_dailyusage_date ON DailyUsage(Date);
            CREATE INDEX IF NOT EXISTS idx_sessions_process ON Sessions(ProcessName, EndTime);
        """);

        // Phase 3 migrations — idempotent
        AddColumnIfMissing(conn, "Sessions", "WindowTitle", "TEXT");
        AddColumnIfMissing(conn, "Sessions", "IsPassive",   "INTEGER NOT NULL DEFAULT 0");

        Execute(conn, """
            INSERT INTO AppRuleDaySchedules (RuleId, DayOfWeek, DailyLimitMins, WindowStart, WindowEnd)
            SELECT r.Id, d.DayOfWeek, r.DailyLimitMins, r.WindowStart, r.WindowEnd
            FROM AppRules r
            CROSS JOIN (
                SELECT 0 AS DayOfWeek
                UNION ALL SELECT 1
                UNION ALL SELECT 2
                UNION ALL SELECT 3
                UNION ALL SELECT 4
                UNION ALL SELECT 5
                UNION ALL SELECT 6
            ) d
            WHERE NOT EXISTS (
                SELECT 1
                FROM AppRuleDaySchedules s
                WHERE s.RuleId = r.Id
            );
        """);
    }

    // Private helper — avoids a dependency on Dapper inside the migrator
    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var exists = (long)(check.ExecuteScalar() ?? 0L);
        if (exists == 0)
            Execute(conn, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}
