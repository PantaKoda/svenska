using Microsoft.Data.Sqlite;
using Svenska.Core.Saldo;

namespace Svenska.Tests;

public sealed class SaldoLexiconTests : IDisposable
{
    // Same tables as tools/saldo_to_sqlite.py creates.
    private const string Fixture = """
        CREATE TABLE lemma (id INTEGER PRIMARY KEY, word TEXT NOT NULL, pos TEXT, pos_name TEXT,
                            gender TEXT, paradigm TEXT, multiword INTEGER);
        CREATE TABLE form (lemma_id INTEGER REFERENCES lemma(id), form TEXT NOT NULL, msd TEXT);
        INSERT INTO lemma VALUES
          (1, 'bil',      'nn', 'noun',        'en',  'nn_2u_stol',    0),
          (2, 'äpple',    'nn', 'noun',        'ett', 'nn_4n_bi',      0),
          (3, 'vara',     'vb', 'verb',        NULL,  'vb_4m_vara',    0),
          (4, 'var',      'ab', 'adverb',      NULL,  'ab_i_inte',     0),
          (5, 'vara',     'nn', 'noun',        'en',  'nn_1u_flicka',  0),
          (6, 'Göteborg', 'pm', 'proper name', NULL,  'pm_nlg_göteborg', 0),
          (7, 'ta upp',   'vbm', 'verb',       NULL,  'vbm_4m1_ta',    1);
        INSERT INTO form VALUES
          (1, 'bil', 'sg indef nom'), (1, 'bilen', 'sg def nom'),
          (1, 'bilar', 'pl indef nom'), (1, 'bilarna', 'pl def nom'),
          (2, 'äpple', 'sg indef nom'), (2, 'äpplet', 'sg def nom'),
          (3, 'vara', 'inf aktiv'), (3, 'var', 'pret ind aktiv'),
          (4, 'var', 'invar'),
          (5, 'vara', 'sg indef nom'),
          (6, 'Göteborg', 'nom'),
          (1, 'bil-', 'ci'), (7, 'tar', 'pres ind aktiv 1:1-2'), (7, 'upp', 'pres ind aktiv 1:2-2');
        CREATE INDEX idx_lemma_word ON lemma(word);
        CREATE INDEX idx_form_form  ON form(form);
        CREATE INDEX idx_form_lemma ON form(lemma_id);
        """;

    private readonly TempDir _dir = new();
    private readonly SaldoLexicon _saldo;

    public SaldoLexiconTests()
    {
        var path = Path.Combine(_dir.Path, "saldo.db");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = Fixture;
            cmd.ExecuteNonQuery();
        }
        _saldo = new SaldoLexicon(path);
    }

    public void Dispose()
    {
        _saldo.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Lookup_finds_lemma_gender_and_form_description()
    {
        var hit = Assert.Single(_saldo.Lookup("bilen"));

        Assert.Equal("bil", hit.Lemma);
        Assert.Equal("nn", hit.Pos);
        Assert.Equal("en", hit.Gender);
        Assert.Equal("sg def nom", hit.Msd);
    }

    [Fact]
    public void Lookup_returns_every_lemma_of_an_ambiguous_form()
    {
        var lemmas = _saldo.Lookup("var").Select(f => f.Lemma).Order().ToList();

        Assert.Equal(["var", "vara"], lemmas);
    }

    [Fact]
    public void Lookup_accepts_a_sentence_initial_capital_also_on_å_ä_ö()
    {
        Assert.Equal("äpple", Assert.Single(_saldo.Lookup("Äpplet")).Lemma);
        Assert.Equal("bil", Assert.Single(_saldo.Lookup(" Bilar ")).Lemma);
    }

    [Fact]
    public void Lookup_keeps_å_ä_ö_distinct_from_a_and_o()
    {
        Assert.Empty(_saldo.Lookup("applet"));
    }

    [Fact]
    public void Lookup_finds_a_single_word_proper_name()
    {
        var hit = Assert.Single(_saldo.Lookup("Göteborg"));

        Assert.Equal("pm", hit.Pos);
        Assert.Null(hit.Gender);
        Assert.Empty(_saldo.Lookup("göteborg"));
    }

    [Fact]
    public void FormsOf_lists_the_forms_of_a_lemma_and_can_filter_by_part_of_speech()
    {
        Assert.Equal(["bil", "bilen", "bilar", "bilarna"], _saldo.FormsOf("bil").Select(f => f.Form));
        Assert.Equal(["vara", "var"], _saldo.FormsOf("vara", "vb").Select(f => f.Form));
        Assert.Empty(_saldo.FormsOf("finnsinte"));
    }

    [Fact]
    public void Lookup_leaves_out_compound_forms_and_the_parts_of_multi_word_lemmas()
    {
        Assert.Empty(_saldo.Lookup("bil-"));
        Assert.Empty(_saldo.Lookup("tar"));
        Assert.Empty(_saldo.FormsOf("ta upp"));
    }

    [Fact]
    public void Real_database_answers_when_it_has_been_built()
    {
        var content = Svenska.Core.Content.ContentLoader.FindContentDir(AppContext.BaseDirectory);
        var path = Path.Combine(content!, "..", "data", "saldo.db");
        if (!File.Exists(path))
            return; // built locally by tools/saldo_to_sqlite.py, not in the repository

        using var saldo = new SaldoLexicon(path);

        // "mjölken" is also the definite form of "mjölke" (milt), so there are two readings.
        Assert.Contains(saldo.Lookup("mjölken"), f => f is { Lemma: "mjölk", Gender: "en", Msd: "sg def nom" });
        Assert.Contains(saldo.Lookup("sprang"), f => f.Lemma == "springa");
        Assert.DoesNotContain(saldo.Lookup("sprang"), f => f.Lemma.Contains(' '));
        Assert.Contains(saldo.FormsOf("hus", "nn"), f => f is { Form: "huset", Gender: "ett" });
    }

    [Fact]
    public void FormsOf_keeps_homographs_apart_by_lemma_id()
    {
        var ids = _saldo.FormsOf("vara").Select(f => f.LemmaId).Distinct().ToList();

        Assert.Equal([3L, 5L], ids);
    }
}
