using System.Text.Json;
using Microsoft.Data.Sqlite;
using Triodos.KruispostMonitor.Matching;
using Triodos.KruispostMonitor.Services;

namespace Triodos.KruispostMonitor.State;

public class SqliteStateStore : IStateStore
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SqliteStateStore(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = $"Data Source={dbPath};Cache=Shared";
    }

    public async Task InitializeAsync()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id TEXT PRIMARY KEY,
                amount REAL NOT NULL,
                counterpart_name TEXT NOT NULL,
                remittance_information TEXT NOT NULL,
                execution_date TEXT NOT NULL,
                transaction_type TEXT NOT NULL DEFAULT '',
                source_file TEXT NOT NULL,
                imported_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS matched_transaction_ids (
                transaction_id TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS manual_matches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS manual_match_members (
                match_id INTEGER NOT NULL REFERENCES manual_matches(id) ON DELETE CASCADE,
                transaction_id TEXT NOT NULL,
                side TEXT NOT NULL CHECK(side IN ('debit', 'credit')),
                PRIMARY KEY (match_id, transaction_id)
            );

            CREATE TABLE IF NOT EXISTS excluded_transactions (
                transaction_id TEXT PRIMARY KEY,
                excluded_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS processing_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                file_name TEXT NOT NULL,
                transaction_count INTEGER NOT NULL,
                auto_matched INTEGER NOT NULL,
                unmatched_debits INTEGER NOT NULL,
                unmatched_credits INTEGER NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        await MigrateFromJsonIfNeededAsync(conn);
    }

    public async Task<RunState> LoadAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var state = new RunState
        {
            LastRunUtc = await GetAppStateDateTimeOffsetAsync(conn, "last_run_utc"),
            RefreshToken = await GetAppStateAsync(conn, "refresh_token"),
            LastProcessedFile = await GetAppStateAsync(conn, "last_processed_file"),
            MatchedTransactionIds = await LoadMatchedIdsAsync(conn),
            ManualMatches = await LoadManualMatchesAsync(conn),
            ExcludedTransactionIds = await LoadExcludedIdsAsync(conn),
            History = await LoadHistoryAsync(conn)
        };

        return state;
    }

    public async Task SaveAsync(RunState state)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // App state scalars
        await SetAppStateAsync(conn, "last_run_utc", state.LastRunUtc?.ToString("o"));
        await SetAppStateAsync(conn, "refresh_token", state.RefreshToken);
        await SetAppStateAsync(conn, "last_processed_file", state.LastProcessedFile);

        // Matched transaction IDs
        await ExecuteAsync(conn, "DELETE FROM matched_transaction_ids");
        foreach (var id in state.MatchedTransactionIds)
        {
            await ExecuteAsync(conn,
                "INSERT OR IGNORE INTO matched_transaction_ids (transaction_id) VALUES (@id)",
                ("@id", id));
        }

        // Manual matches
        await ExecuteAsync(conn, "DELETE FROM manual_match_members");
        await ExecuteAsync(conn, "DELETE FROM manual_matches");
        foreach (var mm in state.ManualMatches)
        {
            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO manual_matches (created_at) VALUES (@created_at) RETURNING id";
            insertCmd.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToString("o"));
            var matchId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

            foreach (var debitId in mm.DebitIds)
            {
                await ExecuteAsync(conn,
                    "INSERT INTO manual_match_members (match_id, transaction_id, side) VALUES (@matchId, @txId, 'debit')",
                    ("@matchId", matchId), ("@txId", debitId));
            }
            foreach (var creditId in mm.CreditIds)
            {
                await ExecuteAsync(conn,
                    "INSERT INTO manual_match_members (match_id, transaction_id, side) VALUES (@matchId, @txId, 'credit')",
                    ("@matchId", matchId), ("@txId", creditId));
            }
        }

        // Excluded transactions
        await ExecuteAsync(conn, "DELETE FROM excluded_transactions");
        foreach (var id in state.ExcludedTransactionIds)
        {
            await ExecuteAsync(conn,
                "INSERT OR IGNORE INTO excluded_transactions (transaction_id, excluded_at) VALUES (@id, @now)",
                ("@id", id), ("@now", DateTimeOffset.UtcNow.ToString("o")));
        }

        // History
        await ExecuteAsync(conn, "DELETE FROM processing_history");
        foreach (var run in state.History)
        {
            await ExecuteAsync(conn,
                """
                INSERT INTO processing_history (timestamp, file_name, transaction_count, auto_matched, unmatched_debits, unmatched_credits)
                VALUES (@ts, @fn, @tc, @am, @ud, @uc)
                """,
                ("@ts", run.Timestamp.ToString("o")),
                ("@fn", run.FileName),
                ("@tc", run.TransactionCount),
                ("@am", run.AutoMatched),
                ("@ud", run.UnmatchedDebits),
                ("@uc", run.UnmatchedCredits));
        }

        await tx.CommitAsync();
    }

    public async Task SaveTransactionsAsync(IReadOnlyList<TransactionRecord> transactions, string sourceFile)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var now = DateTimeOffset.UtcNow.ToString("o");
        foreach (var t in transactions)
        {
            await ExecuteAsync(conn,
                """
                INSERT OR IGNORE INTO transactions (id, amount, counterpart_name, remittance_information, execution_date, transaction_type, source_file, imported_at)
                VALUES (@id, @amount, @name, @remi, @date, @type, @source, @imported)
                """,
                ("@id", t.Id),
                ("@amount", (double)t.Amount),
                ("@name", t.CounterpartName),
                ("@remi", t.RemittanceInformation),
                ("@date", t.ExecutionDate.ToString("o")),
                ("@type", t.TransactionType),
                ("@source", sourceFile),
                ("@imported", now));
        }

        await tx.CommitAsync();
    }

    public async Task<List<TransactionRecord>> GetAllTransactionsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, amount, counterpart_name, remittance_information, execution_date, transaction_type FROM transactions ORDER BY execution_date";

        var results = new List<TransactionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TransactionRecord(
                Id: reader.GetString(0),
                Amount: (decimal)reader.GetDouble(1),
                CounterpartName: reader.GetString(2),
                RemittanceInformation: reader.GetString(3),
                ExecutionDate: DateTimeOffset.Parse(reader.GetString(4)),
                TransactionType: reader.GetString(5)));
        }

        return results;
    }

    public async Task<List<TransactionRecord>> GetMatchedTransactionsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.amount, t.counterpart_name, t.remittance_information, t.execution_date, t.transaction_type
            FROM transactions t
            INNER JOIN matched_transaction_ids m ON t.id = m.transaction_id
            ORDER BY t.execution_date
            """;

        var results = new List<TransactionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TransactionRecord(
                Id: reader.GetString(0),
                Amount: (decimal)reader.GetDouble(1),
                CounterpartName: reader.GetString(2),
                RemittanceInformation: reader.GetString(3),
                ExecutionDate: DateTimeOffset.Parse(reader.GetString(4)),
                TransactionType: reader.GetString(5)));
        }

        return results;
    }

    public async Task<List<TransactionRecord>> GetExcludedTransactionsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.amount, t.counterpart_name, t.remittance_information, t.execution_date, t.transaction_type
            FROM transactions t
            INNER JOIN excluded_transactions e ON t.id = e.transaction_id
            ORDER BY t.execution_date
            """;

        var results = new List<TransactionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TransactionRecord(
                Id: reader.GetString(0),
                Amount: (decimal)reader.GetDouble(1),
                CounterpartName: reader.GetString(2),
                RemittanceInformation: reader.GetString(3),
                ExecutionDate: DateTimeOffset.Parse(reader.GetString(4)),
                TransactionType: reader.GetString(5)));
        }

        return results;
    }

    public async Task RemoveExclusionAsync(string transactionId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await ExecuteAsync(conn, "DELETE FROM excluded_transactions WHERE transaction_id = @id", ("@id", transactionId));
    }

    public async Task DeleteTransactionAsync(string transactionId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await ExecuteAsync(conn, "DELETE FROM matched_transaction_ids WHERE transaction_id = @id", ("@id", transactionId));
        await ExecuteAsync(conn, "DELETE FROM manual_match_members WHERE transaction_id = @id", ("@id", transactionId));
        await ExecuteAsync(conn, "DELETE FROM excluded_transactions WHERE transaction_id = @id", ("@id", transactionId));
        await ExecuteAsync(conn, "DELETE FROM transactions WHERE id = @id", ("@id", transactionId));

        await tx.CommitAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await ExecuteAsync(conn, "DELETE FROM manual_match_members");
        await ExecuteAsync(conn, "DELETE FROM manual_matches");
        await ExecuteAsync(conn, "DELETE FROM matched_transaction_ids");
        await ExecuteAsync(conn, "DELETE FROM excluded_transactions");
        await ExecuteAsync(conn, "DELETE FROM processing_history");
        await ExecuteAsync(conn, "DELETE FROM transactions");
        await ExecuteAsync(conn, "DELETE FROM app_state");

        await tx.CommitAsync();
    }

    private async Task MigrateFromJsonIfNeededAsync(SqliteConnection conn)
    {
        // Check if DB already has data
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM app_state";
        var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
        if (count > 0) return;

        // Look for state.json next to the DB file
        var jsonPath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "", "state.json");
        if (!File.Exists(jsonPath)) return;

        try
        {
            await using var stream = File.OpenRead(jsonPath);
            var oldState = await JsonSerializer.DeserializeAsync<RunState>(stream, new JsonSerializerOptions { WriteIndented = true });
            if (oldState is null) return;

            await using var tx = await conn.BeginTransactionAsync();

            // Migrate scalar app state
            await SetAppStateAsync(conn, "last_run_utc", oldState.LastRunUtc?.ToString("o"));
            await SetAppStateAsync(conn, "refresh_token", oldState.RefreshToken);
            await SetAppStateAsync(conn, "last_processed_file", oldState.LastProcessedFile);

            // Migrate matched IDs
            foreach (var id in oldState.MatchedTransactionIds)
            {
                await ExecuteAsync(conn,
                    "INSERT OR IGNORE INTO matched_transaction_ids (transaction_id) VALUES (@id)",
                    ("@id", id));
            }

            // Migrate manual matches
            foreach (var mm in oldState.ManualMatches)
            {
                await using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = "INSERT INTO manual_matches (created_at) VALUES (@created_at) RETURNING id";
                insertCmd.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToString("o"));
                var matchId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

                foreach (var debitId in mm.DebitIds)
                {
                    await ExecuteAsync(conn,
                        "INSERT INTO manual_match_members (match_id, transaction_id, side) VALUES (@matchId, @txId, 'debit')",
                        ("@matchId", matchId), ("@txId", debitId));
                }
                foreach (var creditId in mm.CreditIds)
                {
                    await ExecuteAsync(conn,
                        "INSERT INTO manual_match_members (match_id, transaction_id, side) VALUES (@matchId, @txId, 'credit')",
                        ("@matchId", matchId), ("@txId", creditId));
                }
            }

            // Migrate excluded IDs
            foreach (var id in oldState.ExcludedTransactionIds)
            {
                await ExecuteAsync(conn,
                    "INSERT OR IGNORE INTO excluded_transactions (transaction_id, excluded_at) VALUES (@id, @now)",
                    ("@id", id), ("@now", DateTimeOffset.UtcNow.ToString("o")));
            }

            // Migrate history
            foreach (var run in oldState.History)
            {
                await ExecuteAsync(conn,
                    """
                    INSERT INTO processing_history (timestamp, file_name, transaction_count, auto_matched, unmatched_debits, unmatched_credits)
                    VALUES (@ts, @fn, @tc, @am, @ud, @uc)
                    """,
                    ("@ts", run.Timestamp.ToString("o")),
                    ("@fn", run.FileName),
                    ("@tc", run.TransactionCount),
                    ("@am", run.AutoMatched),
                    ("@ud", run.UnmatchedDebits),
                    ("@uc", run.UnmatchedCredits));
            }

            await tx.CommitAsync();
        }
        catch
        {
            // Migration is best-effort; don't fail startup
        }
    }

    private static async Task<string?> GetAppStateAsync(SqliteConnection conn, string key)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_state WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : (string)result;
    }

    private static async Task<DateTimeOffset?> GetAppStateDateTimeOffsetAsync(SqliteConnection conn, string key)
    {
        var value = await GetAppStateAsync(conn, key);
        return value is not null ? DateTimeOffset.Parse(value) : null;
    }

    private static async Task SetAppStateAsync(SqliteConnection conn, string key, string? value)
    {
        if (value is null) return;
        await ExecuteAsync(conn,
            "INSERT OR REPLACE INTO app_state (key, value) VALUES (@key, @value)",
            ("@key", key), ("@value", value));
    }

    private static async Task<HashSet<string>> LoadMatchedIdsAsync(SqliteConnection conn)
    {
        var ids = new HashSet<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT transaction_id FROM matched_transaction_ids";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task<List<ManualMatch>> LoadManualMatchesAsync(SqliteConnection conn)
    {
        var matches = new List<ManualMatch>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM manual_matches ORDER BY id";
        var matchIds = new List<long>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                matchIds.Add(reader.GetInt64(0));
        }

        foreach (var matchId in matchIds)
        {
            var debitIds = new List<string>();
            var creditIds = new List<string>();

            await using var memberCmd = conn.CreateCommand();
            memberCmd.CommandText = "SELECT transaction_id, side FROM manual_match_members WHERE match_id = @id";
            memberCmd.Parameters.AddWithValue("@id", matchId);

            await using var memberReader = await memberCmd.ExecuteReaderAsync();
            while (await memberReader.ReadAsync())
            {
                var txId = memberReader.GetString(0);
                var side = memberReader.GetString(1);
                if (side == "debit") debitIds.Add(txId);
                else creditIds.Add(txId);
            }

            matches.Add(new ManualMatch(debitIds, creditIds));
        }

        return matches;
    }

    private static async Task<HashSet<string>> LoadExcludedIdsAsync(SqliteConnection conn)
    {
        var ids = new HashSet<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT transaction_id FROM excluded_transactions";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task<List<ProcessingRun>> LoadHistoryAsync(SqliteConnection conn)
    {
        var history = new List<ProcessingRun>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT timestamp, file_name, transaction_count, auto_matched, unmatched_debits, unmatched_credits FROM processing_history ORDER BY id DESC LIMIT 50";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            history.Add(new ProcessingRun(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }
        return history;
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, params (string name, object value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
