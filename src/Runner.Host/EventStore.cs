using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Runner.Abstractions;

namespace Runner.Host;

public enum EventStatus
{
    /// <summary>The handlers must run (first attempt or retry).</summary>
    Pending,

    /// <summary>All matching handlers are done.</summary>
    Done,

    /// <summary>A permanent failure, or the last attempt failed.</summary>
    Failed,
}

/// <summary>One row of the local store.</summary>
public sealed record StoredEvent(
    InboxEvent Event,
    EventStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset? NextRetryAt,
    IReadOnlySet<string> DoneHandlers,
    DateTimeOffset? AckedAt);

/// <summary>
/// Local SQLite store. The event <c>id</c> is the primary key, so each event is stored one time.
/// </summary>
public sealed class EventStore
{
    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public EventStore(IOptions<InboxOptions> options, IHostEnvironment environment, TimeProvider time)
    {
        var path = options.Value.StorePath;
        if (!Path.IsPathRooted(path)) path = Path.Combine(environment.ContentRootPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        _time = time;
        Initialize();
    }

    private void Initialize()
    {
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL;");
        Execute(db, """
            CREATE TABLE IF NOT EXISTS events (
                id             INTEGER PRIMARY KEY,
                body           TEXT    NOT NULL,
                received_at    INTEGER NOT NULL,
                stored_at      INTEGER NOT NULL,
                status         TEXT    NOT NULL,
                attempts       INTEGER NOT NULL DEFAULT 0,
                last_error     TEXT,
                next_retry_at  INTEGER,
                done_handlers  TEXT    NOT NULL DEFAULT '',
                finished_at    INTEGER,
                acked_at       INTEGER,
                ack_attempts   INTEGER NOT NULL DEFAULT 0,
                next_ack_at    INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_events_due ON events (status, next_retry_at);
            CREATE INDEX IF NOT EXISTS ix_events_ack ON events (acked_at, status);
            """);
    }

    /// <summary>Stores the event. Returns false if the store has this <c>id</c> already.</summary>
    public bool TryAdd(InboxEvent e)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO events (id, body, received_at, stored_at, status, next_retry_at)
            VALUES ($id, $body, $received, $now, 'Pending', $now);
            """;
        var now = Now();
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$body", JsonSerializer.Serialize(e));
        cmd.Parameters.AddWithValue("$received", e.ReceivedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$now", now);
        return cmd.ExecuteNonQuery() == 1;
    }

    public StoredEvent? Get(long id) =>
        Query("SELECT * FROM events WHERE id = $id", ("$id", id)).FirstOrDefault();

    /// <summary>Returns the pending events whose retry time is reached, oldest id first.</summary>
    public IReadOnlyList<StoredEvent> GetDue() =>
        Query("SELECT * FROM events WHERE status = 'Pending' AND next_retry_at <= $now ORDER BY id", ("$now", Now()));

    /// <summary>Returns the earliest retry time of a pending event, or null.</summary>
    public DateTimeOffset? NextRetryTime()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(next_retry_at) FROM events WHERE status = 'Pending'";
        return cmd.ExecuteScalar() is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    }

    public void MarkHandlerDone(long id, string handler)
    {
        var row = Get(id) ?? throw new InvalidOperationException($"Event {id} is not in the store.");
        var done = new SortedSet<string>(row.DoneHandlers, StringComparer.Ordinal) { handler };
        Update(id, "done_handlers = $v", ("$v", string.Join('\n', done)));
    }

    public void MarkDone(long id, int attempts) =>
        Update(id, "status = 'Done', attempts = $a, last_error = NULL, next_retry_at = NULL, finished_at = $now, next_ack_at = $now",
            ("$a", attempts), ("$now", Now()));

    public void MarkFailed(long id, int attempts, string error) =>
        Update(id, "status = 'Failed', attempts = $a, last_error = $e, next_retry_at = NULL, finished_at = $now, next_ack_at = $now",
            ("$a", attempts), ("$e", error), ("$now", Now()));

    public void ScheduleRetry(long id, int attempts, string error, DateTimeOffset at) =>
        Update(id, "attempts = $a, last_error = $e, next_retry_at = $at",
            ("$a", attempts), ("$e", error), ("$at", at.ToUnixTimeMilliseconds()));

    /// <summary>Returns the finished events that the inbox has not acked and whose ack time is reached.</summary>
    public IReadOnlyList<StoredEvent> GetAckDue(int limit) =>
        Query("SELECT * FROM events WHERE status <> 'Pending' AND acked_at IS NULL AND next_ack_at <= $now ORDER BY id LIMIT $limit",
            ("$now", Now()), ("$limit", limit));

    /// <summary>Returns the earliest ack time of a finished event that is not acked, or null.</summary>
    public DateTimeOffset? NextAckTime()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(next_ack_at) FROM events WHERE status <> 'Pending' AND acked_at IS NULL";
        return cmd.ExecuteScalar() is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    }

    public void MarkAcked(IEnumerable<long> ids)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE events SET acked_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void ScheduleAckRetry(IEnumerable<long> ids, DateTimeOffset at)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE events SET ack_attempts = ack_attempts + 1, next_ack_at = $at WHERE id = $id";
            cmd.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Deletes acked rows whose event is older than the retention time.</summary>
    public int Prune(TimeSpan retention)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM events WHERE acked_at IS NOT NULL AND received_at < $limit";
        cmd.Parameters.AddWithValue("$limit", (_time.GetUtcNow() - retention).ToUnixTimeMilliseconds());
        return cmd.ExecuteNonQuery();
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Update(long id, string set, params (string Name, object Value)[] values)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"UPDATE events SET {set} WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private List<StoredEvent> Query(string sql, params (string Name, object Value)[] values)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        using var reader = cmd.ExecuteReader();
        var rows = new List<StoredEvent>();
        while (reader.Read())
        {
            var doneText = reader.GetString(reader.GetOrdinal("done_handlers"));
            rows.Add(new StoredEvent(
                JsonSerializer.Deserialize<InboxEvent>(reader.GetString(reader.GetOrdinal("body")))!,
                Enum.Parse<EventStatus>(reader.GetString(reader.GetOrdinal("status"))),
                reader.GetInt32(reader.GetOrdinal("attempts")),
                reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
                ReadTime(reader, "next_retry_at"),
                doneText.Length == 0 ? new HashSet<string>() : doneText.Split('\n').ToHashSet(StringComparer.Ordinal),
                ReadTime(reader, "acked_at")));
        }
        return rows;
    }

    private static DateTimeOffset? ReadTime(SqliteDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(i));
    }
}
