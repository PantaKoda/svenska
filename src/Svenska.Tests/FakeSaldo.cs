using Svenska.Core.Saldo;

namespace Svenska.Tests;

/// <summary>A handful of SALDO entries in memory: "lemma pos gender: form=msd form=msd …".</summary>
public sealed class FakeSaldo : ISaldoLexicon
{
    private readonly List<SaldoForm> _forms = [];

    public static FakeSaldo Shop() => new FakeSaldo()
        .Add("mjölk", "nn", "en", "mjölk=sg indef nom", "mjölken=sg def nom")
        .Add("ost", "nn", "en", "ost=sg indef nom", "osten=sg def nom", "ostar=pl indef nom")
        .Add("bröd", "nn", "ett", "bröd=sg indef nom", "brödet=sg def nom", "bröd=pl indef nom")
        .Add("kvitto", "nn", "ett", "kvitto=sg indef nom", "kvittot=sg def nom")
        .Add("krona", "nn", "en", "krona=sg indef nom", "kronan=sg def nom", "kronor=pl indef nom")
        .Add("kosta", "vb", null, "kosta=inf aktiv", "kostar=pres ind aktiv", "kostade=pret ind aktiv")
        .Add("finnas", "vb", null, "finnas=inf s-form", "finns=pres ind s-form")
        .Add("vara", "vb", null, "vara=inf aktiv", "är=pres ind aktiv", "var=pret ind aktiv")
        .Add("var", "ab", null, "var=invar")
        .Add("vad", "pn", null, "vad=nom")
        .Add("jag", "pn", null, "jag=nom")
        .Add("inte", "ab", null, "inte=invar")
        .Add("en", "al", null, "en=sg u indef", "ett=sg n indef")
        .Add("den", "pn", null, "den=sg u nom", "det=sg n nom")
        .Add("banan", "nn", "en", "banan=sg indef nom", "bananen=sg def nom");

    public FakeSaldo Add(string lemma, string pos, string? gender, params string[] forms)
    {
        var id = _forms.Select(f => f.LemmaId).DefaultIfEmpty().Max() + 1;
        foreach (var form in forms)
        {
            var parts = form.Split('=');
            _forms.Add(new SaldoForm(parts[0], lemma, id, pos, gender, parts[1]));
        }
        return this;
    }

    public IReadOnlyList<SaldoForm> Lookup(string form) =>
        _forms.Where(f => f.Form == form.Trim() || f.Form == form.Trim().ToLowerInvariant()).ToList();

    public IReadOnlyList<SaldoForm> FormsOf(string lemma, string? pos = null) =>
        _forms.Where(f => f.Lemma == lemma && (pos == null || f.Pos == pos)).ToList();
}
