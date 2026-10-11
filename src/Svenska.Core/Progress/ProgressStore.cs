using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Svenska.Core.Progress;

/// <summary>
/// The learner's progress in a SQLite file (progress.db in the user's app-data folder):
/// which items were answered right or wrong, and the review cards.
/// </summary>
public sealed class ProgressStore : IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS item_result (
            key    TEXT PRIMARY KEY,   -- "<unit or theme id>:<item hash>"
            source TEXT NOT NULL,      -- unit id, or "theme-<pack>"
            right  INTEGER NOT NULL,
            wrong  INTEGER NOT NULL,
            last   TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_item_result_source ON item_result(source);
        CREATE TABLE IF NOT EXISTS card (
            key     TEXT PRIMARY KEY,  -- "word:<lemma>" or an item key
            box     INTEGER NOT NULL,
            due     TEXT NOT NULL,
            reviews INTEGER NOT NULL,
            lapses  INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_card_due ON card(due);
        """;

    private readonly SqliteConnection _db;

    /// <param name="path">The database file; it and its folder are created if missing.</param>
    public ProgressStore(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Execute(Schema);
    }

    // ---- item results ----

    public void RecordResult(string source, string key, bool correct, DateTime now) =>
        Execute("""
            INSERT INTO item_result (key, source, right, wrong, last) VALUES ($key, $source, $right, $wrong, $now)
            ON CONFLICT(key) DO UPDATE SET right = right + $right, wrong = wrong + $wrong, last = $now
            """, ("$key", key), ("$source", source), ("$right", correct ? 1 : 0), ("$wrong", correct ? 0 : 1), ("$now", Text(now)));

    /// <summary>The keys of a unit's or theme's items that were answered right at least once.</summary>
    public HashSet<string> RightKeys(string source) =>
        Query("SELECT key FROM item_result WHERE source = $source AND right > 0", r => r.GetString(0), ("$source", source)).ToHashSet();

    /// <summary>The units and themes the learner has answered anything in.</summary>
    public HashSet<string> StartedSources() =>
        Query("SELECT DISTINCT source FROM item_result", r => r.GetString(0)).ToHashSet();

    // ---- review cards ----

    public CardState? GetCard(string key) =>
        Query("SELECT box, due, reviews, lapses FROM card WHERE key = $key", ReadCard, ("$key", key)).FirstOrDefault();

    public void SaveCard(string key, CardState state) =>
        Execute("""
            INSERT INTO card (key, box, due, reviews, lapses) VALUES ($key, $box, $due, $reviews, $lapses)
            ON CONFLICT(key) DO UPDATE SET box = $box, due = $due, reviews = $reviews, lapses = $lapses
            """, ("$key", key), ("$box", state.Box), ("$due", Text(state.Due)), ("$reviews", state.Reviews), ("$lapses", state.Lapses));

    public void DeleteCard(string key) => Execute("DELETE FROM card WHERE key = $key", ("$key", key));

    /// <summary>The cards that are due, the longest overdue first.</summary>
    public List<(string Key, CardState State)> DueCards(DateTime now) =>
        Query("SELECT box, due, reviews, lapses, key FROM card WHERE due <= $now ORDER BY due, key",
            r => (r.GetString(4), ReadCard(r)), ("$now", Text(now)));

    public int CardCount() => Query("SELECT COUNT(*) FROM card", r => r.GetInt32(0)).Single();

    // ---- plumbing ----

    private static CardState ReadCard(SqliteDataReader r) =>
        new(r.GetInt32(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            r.GetInt32(2), r.GetInt32(3));

    // ISO 8601 in UTC sorts as text in time order.
    private static string Text(DateTime time) => time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return rows;
    }

    private SqliteCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    public void Dispose() => _db.Dispose();
}
