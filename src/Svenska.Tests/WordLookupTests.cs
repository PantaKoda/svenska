using Svenska.Core.Content;
using Svenska.Core.Reference;
using Svenska.Core.Saldo;

namespace Svenska.Tests;

public class WordLookupTests
{
    private static Course Course() => new()
    {
        Packs =
        [
            new ThemePack
            {
                Theme = "shop",
                Words =
                [
                    new Word { Lemma = "ost", Pos = "noun", Gender = "en", Gloss = "cheese" },
                    new Word { Lemma = "kosta", Pos = "verb", Gloss = "to cost; past: cost; pp: cost" },
                    new Word { Lemma = "var", Pos = "adv", Gloss = "where" },
                    new Word { Lemma = "wifi", Pos = "noun", Gender = "ett", Gloss = "wifi", Forms = new() { ["def"] = "wifit" } },
                ],
            },
        ],
        Units = [new Unit { Id = "a1-04", Title = "I matbutiken", NewWords = ["ost", "kosta"] }],
    };

    private static FakeSaldo Saldo() => FakeSaldo.Shop()
        .Add("ost", "nn", "en", "osts=sg indef gen", "ostarna=pl def nom")
        .Add("stor", "av", null, "stor=pos indef sg u nom", "stort=pos indef sg n nom", "stora=pos indef pl nom",
            "större=komp nom", "störst=super indef nom", "störste=super def sg masc nom");

    private readonly WordLookup _lookup = new(Course(), Saldo());

    [Fact]
    public void Any_form_of_a_noun_gives_lemma_article_meaning_unit_and_forms()
    {
        var entry = Assert.Single(_lookup.Lookup("osten").Entries);

        Assert.Equal("en ost", entry.Headword);
        Assert.Equal("noun", entry.PartOfSpeech);
        Assert.Equal("cheese", entry.Gloss);
        Assert.Equal("A1-04 I matbutiken", entry.TaughtIn);
        Assert.Equal(new FormRow("indefinite", "ost"), entry.Forms[0]);
        Assert.Equal(new FormRow("definite", "osten"), entry.Forms[1]);
        Assert.Equal(new FormRow("plural", "ostar"), entry.Forms[2]);
    }

    [Fact]
    public void A_verb_shows_its_tenses_and_the_gloss_without_the_irregular_english_forms()
    {
        var entry = Assert.Single(_lookup.Lookup("kostade").Entries);

        Assert.Equal("kosta", entry.Headword);
        Assert.Equal("to cost", entry.Gloss);
        Assert.Contains(new FormRow("present", "kostar"), entry.Forms);
        Assert.Contains(new FormRow("past", "kostade"), entry.Forms);
    }

    [Fact]
    public void A_verb_with_only_s_forms_still_shows_its_tenses()
    {
        var entry = Assert.Single(_lookup.Lookup("finns").Entries);

        Assert.Equal(new FormRow("infinitive", "finnas"), entry.Forms[0]);
        Assert.Equal(new FormRow("present", "finns"), entry.Forms[1]);
    }

    [Fact]
    public void An_ambiguous_form_gives_every_reading_with_the_pack_word_first()
    {
        var entries = _lookup.Lookup("var").Entries;

        Assert.Equal(["var", "vara"], entries.Select(e => e.Lemma));   // the base form that was typed comes first
        Assert.Equal("where", entries[0].Gloss);
        Assert.Null(entries[1].Gloss);   // vara (verb) is not in this course's packs
        Assert.Null(entries[1].TaughtIn);
    }

    [Fact]
    public void An_adjective_shows_agreement_and_comparison()
    {
        var entry = Assert.Single(_lookup.Lookup("större").Entries);

        Assert.Equal("stor", entry.Headword);
        Assert.Contains(new FormRow("with ett-words", "stort"), entry.Forms);
        Assert.Contains(new FormRow("comparative", "större"), entry.Forms);
        Assert.Contains(new FormRow("other forms", "störste"), entry.Forms);
    }

    [Fact]
    public void A_word_typed_without_å_ä_ö_gets_suggestions()
    {
        var result = _lookup.Lookup("brod");

        Assert.Empty(result.Entries);
        Assert.Equal(["bröd"], result.Suggestions);
    }

    [Fact]
    public void A_pack_word_that_saldo_does_not_have_is_found_by_lemma_or_own_form()
    {
        Assert.Equal("ett wifi", Assert.Single(_lookup.Lookup("wifit").Entries).Headword);
        Assert.Equal(new FormRow("definite", "wifit"), Assert.Single(_lookup.Lookup("wifi").Entries).Forms[0]);
    }

    [Fact]
    public void Nothing_found_and_empty_input()
    {
        Assert.Empty(_lookup.Lookup("xyzzy").Entries);
        Assert.Empty(_lookup.Lookup("xyzzy").Suggestions);
        Assert.Empty(_lookup.Lookup("  ").Entries);
    }

    [Fact]
    public void Real_database_and_course_when_built()
    {
        var content = ContentLoader.FindContentDir(AppContext.BaseDirectory)!;
        var path = Path.Combine(content, "..", "data", "saldo.db");
        if (!File.Exists(path))
            return;
        using var saldo = new SaldoLexicon(path);
        var lookup = new WordLookup(ContentLoader.Load(content).Course, saldo);

        var bil = lookup.Lookup("bilarna").Entries[0];
        Assert.Equal("en bil", bil.Headword);
        Assert.Equal(["bil", "bilen", "bilar", "bilarna"], bil.Forms.Take(4).Select(f => f.Forms));

        var köpa = lookup.Lookup("köpte").Entries[0];
        Assert.Equal("to buy", köpa.Gloss);
        Assert.Contains(new FormRow("supine (har …)", "köpt"), köpa.Forms);

        Assert.Contains("mjölk", lookup.Lookup("mjolk").Suggestions);
    }
}
