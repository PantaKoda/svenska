using Svenska.Core.Answers;
using Svenska.Core.Content;
using Svenska.Core.Exercises;
using Svenska.Core.Saldo;
using Svenska.Core.Text;

namespace Svenska.Tests;

public class AnswerCheckerTests
{
    private readonly AnswerChecker _checker = new(FakeSaldo.Shop());

    [Theory]
    [InlineData("Var finns mjölken?")]
    [InlineData("var finns mjölken")]
    [InlineData("  Var   finns mjölken ?  ")]
    [InlineData("Var finns mjölken.")]
    public void Spacing_final_punctuation_and_the_first_capital_do_not_matter(string given)
    {
        Assert.True(_checker.Check(given, "Var finns mjölken?").IsCorrect);
    }

    [Fact]
    public void A_comma_does_not_matter()
    {
        Assert.True(_checker.Check("nej tack jag behöver inte kvittot", "Nej tack, jag behöver inte kvittot.").IsCorrect);
    }

    [Fact]
    public void Accepted_alternatives_are_correct()
    {
        Assert.True(_checker.Check("Vad kostar brödet", "Vad kostar osten?", ["Vad kostar brödet?"]).IsCorrect);
    }

    [Fact]
    public void Capital_inside_the_sentence_matters()
    {
        Assert.False(_checker.Check("Var finns Mjölken?", "Var finns mjölken?").IsCorrect);
    }

    [Fact]
    public void Missing_diacritic_is_named_and_is_not_correct()
    {
        var result = _checker.Check("Var finns mjolken?", "Var finns mjölken?");

        Assert.Equal(AnswerKind.MissingDiacritic, result.Kind);
        Assert.False(result.IsCorrect);
        Assert.Contains("å, ä and ö", result.Feedback);
        Assert.Contains("mjölken", result.Feedback);
        Assert.Equal([true, true, false], result.Words.Select(w => w.Right));
    }

    [Fact]
    public void Right_word_in_the_wrong_form_is_explained_with_the_form_name()
    {
        var result = _checker.Check("mjölk", "mjölken");

        Assert.Equal(AnswerKind.WrongForm, result.Kind);
        Assert.Equal("Right word, wrong form: expected mjölken (definite).", result.Feedback);
    }

    [Fact]
    public void Wrong_form_inside_a_sentence_is_found()
    {
        var result = _checker.Check("Vad kosta osten?", "Vad kostar osten?");

        Assert.Equal(AnswerKind.WrongForm, result.Kind);
        Assert.Contains("kostar (present)", result.Feedback);
        Assert.Equal([true, false, true], result.Words.Select(w => w.Right));
    }

    [Fact]
    public void Wrong_words_are_listed()
    {
        var result = _checker.Check("Var finns osten?", "Var finns mjölken?");

        Assert.Equal(AnswerKind.Wrong, result.Kind);
        Assert.Equal("Wrong or missing: mjölken.", result.Feedback);
        Assert.Equal("Var finns mjölken?", result.Expected);
        Assert.Equal(["Var", "finns", "mjölken"], result.Words.Select(w => w.Word));
    }

    [Fact]
    public void Missing_and_extra_words_are_told_apart()
    {
        Assert.Equal("Missing: inte.", _checker.Check("Mjölken finns där", "Mjölken finns inte där.").Feedback);
        Assert.Equal("Too many words: inte.", _checker.Check("Mjölken finns inte där", "Mjölken finns där.").Feedback);
    }

    [Fact]
    public void Right_words_in_the_wrong_order_are_wrong()
    {
        var result = _checker.Check("Mjölken inte finns där", "Mjölken finns inte där.");

        Assert.Equal(AnswerKind.Wrong, result.Kind);
        Assert.Equal("The words are right, but not the order.", result.Feedback);
    }

    [Fact]
    public void Works_without_a_word_database()
    {
        var result = new AnswerChecker().Check("mjölk", "mjölken");

        Assert.Equal(AnswerKind.Wrong, result.Kind);
    }

    [Fact]
    public void Tokenizer_splits_words_and_numbers_and_drops_punctuation()
    {
        Assert.Equal(["Nej", "tack", "det", "kostar", "19,90", "kronor"],
            Tokenizer.Tokens("Nej tack, det kostar 19,90 kronor!"));
        Assert.Equal(["Var", "finns"], Tokenizer.Tokens("Var finns ___?"));
        Assert.True(Tokenizer.IsNumber("87"));
        Assert.False(Tokenizer.IsNumber("åtta"));
    }

    [Fact]
    public void Inflect_reads_forms_from_saldo_and_prefers_the_packs_own_forms()
    {
        var saldo = FakeSaldo.Shop();
        var ost = new Word { Lemma = "ost", Pos = "noun", Gender = "en" };
        var finnas = new Word { Lemma = "finnas", Pos = "verb" };
        var own = new Word { Lemma = "wifi", Pos = "noun", Gender = "ett", Forms = new() { ["def"] = "wifit" } };

        Assert.Equal(["osten"], WordForms.Inflect(ost, "def", saldo));
        Assert.Equal(["ostar"], WordForms.Inflect(ost, "pl", saldo));
        Assert.Equal(["finns"], WordForms.Inflect(finnas, "pres", saldo));
        Assert.Equal(["wifit"], WordForms.Inflect(own, "def", saldo));
        Assert.Empty(WordForms.Inflect(ost, "pres", saldo));
        Assert.Empty(WordForms.Inflect(own, "pl", saldo));
    }

    [Fact]
    public void Shuffled_tokens_are_the_same_words_in_another_order_every_time()
    {
        var first = ExerciseText.ShuffledTokens("Jag betalar inte med kort.", []);
        var second = ExerciseText.ShuffledTokens("Jag betalar inte med kort.", []);

        Assert.Equal(first, second);
        Assert.NotEqual(["jag", "betalar", "inte", "med", "kort"], first);
        Assert.Equal(["betalar", "inte", "jag", "kort", "med"], first.Order());
        Assert.Contains("Göteborg", ExerciseText.ShuffledTokens("Göteborg är där.", ["Göteborg"]));
    }

    [Fact]
    public void Expected_answer_and_full_sentence_per_exercise_type()
    {
        var cloze = new ExerciseItem { Type = "cloze", Sv = "Var finns ___?", Answer = "smöret" };
        var dictation = new ExerciseItem { Type = "dictation", Sv = "Var finns mjölken?" };

        Assert.Equal("smöret", ExerciseText.Expected(cloze));
        Assert.Equal("Var finns smöret?", ExerciseText.FullSentence(cloze));
        Assert.Equal("Var finns mjölken?", ExerciseText.Expected(dictation));
    }
}
