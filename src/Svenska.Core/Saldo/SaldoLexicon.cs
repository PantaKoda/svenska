using Microsoft.Data.Sqlite;

namespace Svenska.Core.Saldo;

/// <summary>
/// One inflected form from SALDO. <paramref name="Pos"/> and <paramref name="Msd"/> use SALDO's
/// own codes (nn, vb, av, pm … / "sg def nom", "pres ind aktiv" …). <paramref name="Gender"/>
/// is "en", "ett" or "en/ett" for nouns (null for plural-only nouns), otherwise null. Homographs ("vara" the verb and the noun)
/// are different lemmas with different <paramref name="LemmaId"/>s.
/// </summary>
public sealed record SaldoForm(string Form, string Lemma, long LemmaId, string Pos, string? Gender, string Msd);

public interface ISaldoLexicon
{
    /// <summary>All readings of a written form. Several lemmas are possible (e.g. "var").</summary>
    IReadOnlyList<SaldoForm> Lookup(string form);

    /// <summary>All forms of a lemma, optionally limited to one SALDO part of speech.</summary>
    IReadOnlyList<SaldoForm> FormsOf(string lemma, string? pos = null);

    /// <summary>All forms of one lemma, by the id that <see cref="SaldoForm.LemmaId"/> gives.</summary>
    IReadOnlyList<SaldoForm> FormsOfLemma(long lemmaId);
}

/// <summary>Read-only access to data/saldo.db (tables lemma and form, built by tools/saldo_to_sqlite.py).</summary>
public sealed class SaldoLexicon : ISaldoLexicon, IDisposable
{
    // Left out: multi-word lemmas ("springa in" stores "sprang" as one of its forms) and
    // the forms a word only takes inside a compound ("bil-", "gatu"; msd c, ci, cm, sms).
    private const string Select = """
        SELECT f.form, l.word, l.id, l.pos, l.gender, f.msd
        FROM form f JOIN lemma l ON l.id = f.lemma_id AND l.multiword = 0
                                AND f.msd NOT IN ('c', 'ci', 'cm', 'sms')
        """;

    private readonly SqliteConnection _db;

    public SaldoLexicon(string dbPath)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        _db.Open();
    }

    public IReadOnlyList<SaldoForm> Lookup(string form)
    {
        form = form.Trim();
        var found = Query($"{Select} WHERE f.form = $a", form);
        // A capital letter may only be there because the word starts a sentence.
        var lower = form.ToLowerInvariant();
        if (lower != form)
            found.AddRange(Query($"{Select} WHERE f.form = $a", lower));
        return found;
    }

    public IReadOnlyList<SaldoForm> FormsOf(string lemma, string? pos = null) =>
        pos == null
            ? Query($"{Select} WHERE l.word = $a ORDER BY l.id, f.rowid", lemma)
            : Query($"{Select} WHERE l.word = $a AND l.pos = $b ORDER BY l.id, f.rowid", lemma, pos);

    public IReadOnlyList<SaldoForm> FormsOfLemma(long lemmaId) =>
        Query($"{Select} WHERE l.id = $a ORDER BY f.rowid", lemmaId.ToString());

    private List<SaldoForm> Query(string sql, string a, string? b = null)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$a", a);
        if (b != null)
            cmd.Parameters.AddWithValue("$b", b);
        var rows = new List<SaldoForm>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new SaldoForm(reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5)));
        return rows;
    }

    public void Dispose() => _db.Dispose();
}
