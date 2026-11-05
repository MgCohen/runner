using System.Globalization;
using System.Text;
using Handling.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Handling.Infrastructure;

// The local SQLite store: the events, the pending items (one for each event and matching handler), the handler results
// and the last source list of the inbox. The event ID is the primary key, so each event is stored one time.
public sealed class HandlingStore {
    readonly string connection;
    readonly TimeProvider time;

    public HandlingStore(IConfiguration configuration, TimeProvider time) : this(Path.Combine(StorePaths.DataDir(configuration), "inbox.db"), time) { }

    public HandlingStore(string path, TimeProvider time) {
        this.time = time;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL;");
        Execute(db, """
            CREATE TABLE IF NOT EXISTS events (
                id          INTEGER PRIMARY KEY,
                source      TEXT    NOT NULL,
                delivery_id TEXT    NOT NULL,
                type        TEXT,
                action      TEXT,
                received_at INTEGER NOT NULL,
                payload     TEXT    NOT NULL,
                status      TEXT    NOT NULL,
                attempts    INTEGER NOT NULL DEFAULT 0,
                finished_at INTEGER,
                acked_at    INTEGER,
                next_ack_at INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_events_order ON events (received_at DESC, id DESC);
            CREATE TABLE IF NOT EXISTS pending (
                event_id      INTEGER NOT NULL,
                handler_key   TEXT    NOT NULL,
                attempts      INTEGER NOT NULL DEFAULT 0,
                next_retry_at INTEGER NOT NULL,
                received_at   INTEGER NOT NULL,
                PRIMARY KEY (event_id, handler_key)
            );
            CREATE INDEX IF NOT EXISTS ix_pending_lane ON pending (handler_key, received_at, event_id);
            CREATE TABLE IF NOT EXISTS results (
                event_id    INTEGER NOT NULL,
                handler_key TEXT    NOT NULL,
                kind        TEXT    NOT NULL,
                reason      TEXT,
                duration_ms INTEGER NOT NULL,
                attempt     INTEGER NOT NULL,
                final       INTEGER NOT NULL,
                at          INTEGER NOT NULL,
                PRIMARY KEY (event_id, handler_key)
            );
            CREATE TABLE IF NOT EXISTS sources (
                name     TEXT    PRIMARY KEY,
                position INTEGER NOT NULL
            );
            """);
    }

    // Stores a new event with one pending item for each handler. Returns false for an ID that the store has.
    // An event with no handler is Done at once.
    public bool TryAdd(EventRecord e, IReadOnlyCollection<string> handlers) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        var now = Now();
        var added = Run(db, tx, """
            INSERT OR IGNORE INTO events (id, source, delivery_id, type, action, received_at, payload, status, finished_at, next_ack_at)
            VALUES ($id, $source, $delivery, $type, $action, $received, $payload, $status, $finished, $finished)
            """,
            ("$id", e.Id), ("$source", e.Source), ("$delivery", e.DeliveryId), ("$type", e.Type), ("$action", e.Action),
            ("$received", e.ReceivedAt.ToUnixTimeMilliseconds()), ("$payload", e.Payload),
            ("$status", handlers.Count == 0 ? nameof(EventStatus.Done) : nameof(EventStatus.Pending)),
            ("$finished", handlers.Count == 0 ? now : null)) == 1;
        if (added) {
            foreach (var handler in handlers) {
                Run(db, tx, "INSERT INTO pending (event_id, handler_key, next_retry_at, received_at) VALUES ($id, $key, $now, $received)",
                    ("$id", e.Id), ("$key", handler), ("$now", now), ("$received", e.ReceivedAt.ToUnixTimeMilliseconds()));
            }
        }
        tx.Commit();
        return added;
    }

    public EventStatus? StatusOf(long id) {
        using var db = Open();
        return Scalar(db, "SELECT status FROM events WHERE id = $id", ("$id", id)) is string status ? Enum.Parse<EventStatus>(status) : null;
    }

    public StoredEvent? Get(long id) {
        using var db = Open();
        var e = Events(db, "SELECT * FROM events WHERE id = $id", ("$id", id)).FirstOrDefault();
        if (e is null) {
            return null;
        }
        var results = Results(db, id);
        var keys = Strings(db, "SELECT handler_key FROM pending WHERE event_id = $id UNION SELECT handler_key FROM results WHERE event_id = $id", ("$id", id));
        return e with { Handlers = [.. keys.Order(StringComparer.Ordinal)], Results = results };
    }

    // The oldest item of the lane whose retry time is reached: by received time, then event ID.
    public PendingItem? NextDue(string handler) {
        using var db = Open();
        return Pending(db, "SELECT * FROM pending WHERE handler_key = $key AND next_retry_at <= $now ORDER BY received_at, event_id LIMIT 1",
            ("$key", handler), ("$now", Now())).FirstOrDefault();
    }

    public DateTimeOffset? NextRetry(string handler) {
        using var db = Open();
        return Scalar(db, "SELECT MIN(next_retry_at) FROM pending WHERE handler_key = $key", ("$key", handler)) is long ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    }

    public PendingItem? PendingOf(long id, string handler) {
        using var db = Open();
        return Pending(db, "SELECT * FROM pending WHERE event_id = $id AND handler_key = $key", ("$id", id), ("$key", handler)).FirstOrDefault();
    }

    public void Reschedule(long id, string handler, int attempts, DateTimeOffset at) {
        using var db = Open();
        Run(db, null, "UPDATE pending SET attempts = $a, next_retry_at = $at WHERE event_id = $id AND handler_key = $key",
            ("$a", attempts), ("$at", at.ToUnixTimeMilliseconds()), ("$id", id), ("$key", handler));
    }

    public void RemovePending(long id, string handler) {
        using var db = Open();
        Run(db, null, "DELETE FROM pending WHERE event_id = $id AND handler_key = $key", ("$id", id), ("$key", handler));
    }

    // Writes the result row of a handler (one row for each event and handler) and the attempt count of the event.
    public void Record(HandlerResult result) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Run(db, tx, """
            INSERT OR REPLACE INTO results (event_id, handler_key, kind, reason, duration_ms, attempt, final, at)
            VALUES ($id, $key, $kind, $reason, $ms, $attempt, $final, $at)
            """,
            ("$id", result.EventId), ("$key", result.HandlerKey), ("$kind", result.Kind.ToString()), ("$reason", result.Reason),
            ("$ms", (long)result.Duration.TotalMilliseconds), ("$attempt", result.Attempt), ("$final", result.Final ? 1 : 0),
            ("$at", result.At.ToUnixTimeMilliseconds()));
        if (result.Kind != ResultKind.Cancelled) {
            Run(db, tx, "UPDATE events SET attempts = MAX(attempts, $a) WHERE id = $id", ("$a", result.Attempt), ("$id", result.EventId));
        }
        tx.Commit();
    }

    public void MarkFinal(long id, string handler) {
        using var db = Open();
        Run(db, null, "UPDATE results SET final = 1 WHERE event_id = $id AND handler_key = $key", ("$id", id), ("$key", handler));
    }

    // If the event has no pending item, sets Done (no final failure) or Failed, and returns it. Else returns null.
    public EventStatus? FinishIfComplete(long id) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if ((long)Scalar(db, "SELECT COUNT(*) FROM pending WHERE event_id = $id", ("$id", id), tx)! > 0) {
            return null;
        }
        var failed = (long)Scalar(db, "SELECT COUNT(*) FROM results WHERE event_id = $id AND kind = 'Failed' AND final = 1", ("$id", id), tx)! > 0;
        var status = failed ? EventStatus.Failed : EventStatus.Done;
        var now = Now();
        Run(db, tx, "UPDATE events SET status = $s, finished_at = $now, next_ack_at = $now WHERE id = $id AND status = 'Pending'",
            ("$s", status.ToString()), ("$now", now), ("$id", id));
        tx.Commit();
        return status;
    }

    // The finished events that the inbox has not acked and whose ack time is reached.
    public IReadOnlyList<long> AckDue(int limit) {
        using var db = Open();
        return Longs(db, "SELECT id FROM events WHERE status <> 'Pending' AND acked_at IS NULL AND next_ack_at <= $now ORDER BY id LIMIT $limit",
            ("$now", Now()), ("$limit", limit));
    }

    public DateTimeOffset? NextAckTime() {
        using var db = Open();
        return Scalar(db, "SELECT MIN(next_ack_at) FROM events WHERE status <> 'Pending' AND acked_at IS NULL") is long ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    }

    public void MarkAcked(IEnumerable<long> ids) => ForEach(ids, "UPDATE events SET acked_at = $now WHERE id = $id", ("$now", Now()));

    // A duplicate of a finished event: the ack runs again.
    public void AckAgain(long id) => ForEach([id], "UPDATE events SET acked_at = NULL, next_ack_at = $now WHERE id = $id AND status <> 'Pending'", ("$now", Now()));

    public void ScheduleAckRetry(IEnumerable<long> ids, DateTimeOffset at) =>
        ForEach(ids, "UPDATE events SET next_ack_at = $at WHERE id = $id", ("$at", at.ToUnixTimeMilliseconds()));

    // Deletes the finished events received before the time, with their results. Pending events stay.
    public int Prune(DateTimeOffset before) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        var limit = before.ToUnixTimeMilliseconds();
        Run(db, tx, "DELETE FROM results WHERE event_id IN (SELECT id FROM events WHERE status <> 'Pending' AND received_at < $limit)", ("$limit", limit));
        var count = Run(db, tx, "DELETE FROM events WHERE status <> 'Pending' AND received_at < $limit", ("$limit", limit));
        tx.Commit();
        return count;
    }

    public IReadOnlyList<string> Sources() {
        using var db = Open();
        return Strings(db, "SELECT name FROM sources ORDER BY position");
    }

    public void SaveSources(IReadOnlyList<string> names) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Run(db, tx, "DELETE FROM sources");
        for (var i = 0; i < names.Count; i++) {
            Run(db, tx, "INSERT OR IGNORE INTO sources (name, position) VALUES ($n, $p)", ("$n", names[i]), ("$p", i));
        }
        tx.Commit();
    }

    // The event count of each source in the store.
    public IReadOnlyDictionary<string, int> SourceCounts() {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT source, COUNT(*) FROM events GROUP BY source");
        using var reader = cmd.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    // The event count, the final failure count and the newest event of each handler.
    public HandlerStats StatsOf(string handler) {
        using var db = Open();
        const string Ids = "SELECT event_id FROM pending WHERE handler_key = $key UNION SELECT event_id FROM results WHERE handler_key = $key";
        var count = (long)Scalar(db, $"SELECT COUNT(*) FROM ({Ids})", ("$key", handler))!;
        var failed = (long)Scalar(db, "SELECT COUNT(*) FROM results WHERE handler_key = $key AND kind = 'Failed' AND final = 1", ("$key", handler))!;
        var last = Events(db, $"SELECT * FROM events WHERE id IN ({Ids}) ORDER BY received_at DESC, id DESC LIMIT 1", ("$key", handler)).FirstOrDefault();
        return new HandlerStats(handler, (int)count, (int)failed, last?.Event);
    }

    public EventPage List(EventQuery query) {
        using var db = Open();
        var total = (int)(long)Scalar(db, "SELECT COUNT(*) FROM events")!;
        var (where, values) = Where(query, skip: null);
        var matching = (int)(long)Scalar(db, $"SELECT COUNT(*) FROM events e WHERE {where}", values)!;
        var page = new List<(object Name, object? Value)>(values.Select(v => ((object)v.Name, v.Value)));
        var sql = new StringBuilder($"SELECT * FROM events e WHERE {where}");
        if (query.Before is { } before) {
            sql.Append(" AND (e.received_at < (SELECT received_at FROM events WHERE id = $before) OR (e.received_at = (SELECT received_at FROM events WHERE id = $before) AND e.id < $before))");
            page.Add(("$before", before));
        }
        sql.Append(" ORDER BY e.received_at DESC, e.id DESC LIMIT $limit");
        page.Add(("$limit", query.Limit + 1));
        var rows = Events(db, sql.ToString(), [.. page.Select(p => ((string)p.Name, p.Value))]);
        var events = rows.Take(query.Limit).Select(r => new EventRow(r.Event.Id, r.Event.Source, r.Event.TypeText, r.Event.ReceivedAt, r.State,
            Strings(db, "SELECT handler_key FROM pending WHERE event_id = $id UNION SELECT handler_key FROM results WHERE event_id = $id ORDER BY 1", ("$id", r.Event.Id)))).ToList();
        var facets = new Dictionary<string, IReadOnlyDictionary<string, int>> {
            ["state"] = Facet(db, query, "state", StateSql),
            ["source"] = Facet(db, query, "source", "e.source"),
            ["handler"] = HandlerFacet(db, query),
        };
        return new EventPage(events, matching, total, facets, rows.Count > query.Limit);
    }

    const string StateSql = "CASE WHEN e.status = 'Done' THEN 'done' WHEN e.status = 'Failed' THEN 'failed' WHEN e.attempts = 0 THEN 'new' ELSE 'pending' END";

    const string HandlersOf = "(SELECT handler_key FROM pending WHERE event_id = e.id UNION SELECT handler_key FROM results WHERE event_id = e.id)";

    static Dictionary<string, int> Facet(SqliteConnection db, EventQuery query, string group, string column) {
        var (where, values) = Where(query, skip: group);
        using var cmd = Command(db, null, $"SELECT {column}, COUNT(*) FROM events e WHERE {where} GROUP BY 1", values);
        return Counts(cmd);
    }

    static Dictionary<string, int> HandlerFacet(SqliteConnection db, EventQuery query) {
        var (where, values) = Where(query, skip: "handler");
        using var cmd = Command(db, null, $"""
            SELECT h.handler_key, COUNT(*) FROM events e
            JOIN (SELECT event_id, handler_key FROM pending UNION SELECT event_id, handler_key FROM results) h ON h.event_id = e.id
            WHERE {where} GROUP BY 1
            """, values);
        return Counts(cmd);
    }

    static Dictionary<string, int> Counts(SqliteCommand cmd) {
        using var reader = cmd.ExecuteReader();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (reader.Read()) {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    // The filter of a query as SQL. skip: a group to leave out, for its facet counts.
    static (string Sql, (string Name, object? Value)[] Values) Where(EventQuery query, string? skip) {
        var parts = new List<string> { "1 = 1" };
        var values = new List<(string, object?)>();
        void In(string group, string column, IReadOnlyList<string> items) {
            if (skip == group || items.Count == 0) {
                return;
            }
            var names = items.Select((v, i) => {
                var name = $"${group}{i}";
                values.Add((name, v));
                return name;
            });
            parts.Add($"{column} IN ({string.Join(", ", names)})");
        }
        In("state", StateSql, query.States);
        In("source", "e.source", query.Sources);
        if (skip != "handler" && query.Handlers.Count > 0) {
            var names = query.Handlers.Select((v, i) => {
                var name = $"$handler{i}";
                values.Add((name, v));
                return name;
            });
            parts.Add($"EXISTS (SELECT 1 FROM {HandlersOf} h WHERE h.handler_key IN ({string.Join(", ", names)}))");
        }
        if (!string.IsNullOrWhiteSpace(query.Text)) {
            parts.Add("lower('#' || e.id || ' ' || e.source || ' ' || coalesce(e.type, '') || coalesce('.' || e.action, '')) LIKE $text ESCAPE '\\'");
            var text = query.Text.Trim().ToLowerInvariant().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            values.Add(("$text", $"%{text}%"));
        }
        return (string.Join(" AND ", parts), [.. values]);
    }

    long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();

    SqliteConnection Open() {
        var db = new SqliteConnection(connection);
        db.Open();
        Execute(db, "PRAGMA busy_timeout = 5000;");
        return db;
    }

    void ForEach(IEnumerable<long> ids, string sql, params (string Name, object? Value)[] values) {
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var id in ids) {
            Run(db, tx, sql, [.. values, ("$id", id)]);
        }
        tx.Commit();
    }

    static void Execute(SqliteConnection db, string sql) {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values) {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in values) {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd;
    }

    static int Run(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values) {
        using var cmd = Command(db, tx, sql, values);
        return cmd.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection db, string sql, params (string Name, object? Value)[] values) => Scalar(db, sql, values, null);

    static object? Scalar(SqliteConnection db, string sql, (string Name, object? Value) value, SqliteTransaction? tx) => Scalar(db, sql, [value], tx);

    static object? Scalar(SqliteConnection db, string sql, (string Name, object? Value)[] values, SqliteTransaction? tx) {
        using var cmd = Command(db, tx, sql, values);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    static List<string> Strings(SqliteConnection db, string sql, params (string Name, object? Value)[] values) {
        using var cmd = Command(db, null, sql, values);
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    static List<long> Longs(SqliteConnection db, string sql, params (string Name, object? Value)[] values) {
        using var cmd = Command(db, null, sql, values);
        using var reader = cmd.ExecuteReader();
        var list = new List<long>();
        while (reader.Read()) {
            list.Add(reader.GetInt64(0));
        }
        return list;
    }

    static List<PendingItem> Pending(SqliteConnection db, string sql, params (string Name, object? Value)[] values) {
        using var cmd = Command(db, null, sql, values);
        using var reader = cmd.ExecuteReader();
        var list = new List<PendingItem>();
        while (reader.Read()) {
            list.Add(new PendingItem(reader.GetInt64(reader.GetOrdinal("event_id")), reader.GetString(reader.GetOrdinal("handler_key")),
                reader.GetInt32(reader.GetOrdinal("attempts")), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("next_retry_at")))));
        }
        return list;
    }

    static List<HandlerResult> Results(SqliteConnection db, long id) {
        using var cmd = Command(db, null, "SELECT * FROM results WHERE event_id = $id ORDER BY handler_key", ("$id", id));
        using var reader = cmd.ExecuteReader();
        var list = new List<HandlerResult>();
        while (reader.Read()) {
            list.Add(new HandlerResult(id, reader.GetString(reader.GetOrdinal("handler_key")),
                Enum.Parse<ResultKind>(reader.GetString(reader.GetOrdinal("kind"))),
                reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
                TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("duration_ms"))),
                reader.GetInt32(reader.GetOrdinal("attempt")), reader.GetInt32(reader.GetOrdinal("final")) == 1,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("at")))));
        }
        return list;
    }

    static List<StoredEvent> Events(SqliteConnection db, string sql, params (string Name, object? Value)[] values) {
        using var cmd = Command(db, null, sql, values);
        using var reader = cmd.ExecuteReader();
        var list = new List<StoredEvent>();
        while (reader.Read()) {
            string? Text(string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));
            DateTimeOffset? Time(string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal(column)));
            var e = new EventRecord(reader.GetInt64(reader.GetOrdinal("id")), Text("source")!, Text("delivery_id")!, Text("type"), Text("action"),
                Time("received_at")!.Value, Text("payload")!);
            list.Add(new StoredEvent(e, Enum.Parse<EventStatus>(Text("status")!), reader.GetInt32(reader.GetOrdinal("attempts")), Time("acked_at"), [], []));
        }
        return list;
    }

    // For the views: the count as text with the invariant culture.
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
