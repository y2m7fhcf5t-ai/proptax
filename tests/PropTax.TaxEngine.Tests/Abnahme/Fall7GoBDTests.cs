using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.Domain.Werte;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Abnahme;

public class Fall7GoBDTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private readonly Protokoll _protokoll = new();
    private readonly Belegaenderung _aenderung;
    private readonly Buchung _beleg = new("C1", D(2026, 3, 10), new CurrencyAmount(99m, "USD"), "Challenge");

    public Fall7GoBDTests() =>
        _aenderung = new Belegaenderung(new Quartalsperioden(UstZeitraum.Quartal(2026, 1)), _protokoll, "Inhaber", new FesteZeit(Jetzt));

    [Fact]
    public void Fall7_Aenderung_in_festgeschriebener_Periode_wird_abgelehnt()
    {
        var ex = Assert.Throws<PeriodeFestgeschriebenException>(() =>
            _aenderung.Aendern(_beleg, _beleg with { Betrag = new CurrencyAmount(89m, "USD") }, "Tippfehler"));
        Assert.Equal(D(2026, 3, 10), ex.Datum);
        Assert.Empty(_protokoll.Eintraege);
    }

    [Fact]
    public void Fall7_Storno_in_offener_Periode_mit_Protokolleintrag()
    {
        var storno = _aenderung.Stornieren(_beleg, D(2026, 4, 2), "Falscher Betrag");

        Assert.Equal(-99m, storno.Betrag.Betrag);
        Assert.Equal("USD", storno.Betrag.Waehrung);
        Assert.Equal("C1", storno.StornoVon);
        Assert.Equal(D(2026, 4, 2), storno.Datum);

        var e = Assert.Single(_protokoll.Eintraege);
        Assert.Equal("STORNO", e.Aktion);
        Assert.Equal("C1", e.EntitaetId);
        Assert.Equal("Inhaber", e.Benutzer);
        Assert.Equal(Jetzt, e.Zeitpunkt);
        Assert.Equal("Falscher Betrag", e.Begruendung);
        Assert.True(_protokoll.PruefeKette());
    }

    [Fact]
    public void Storno_in_festgeschriebene_Periode_ist_unzulaessig() =>
        Assert.Throws<PeriodeFestgeschriebenException>(() => _aenderung.Stornieren(_beleg, D(2026, 3, 31), "x"));

    [Fact]
    public void Korrektur_erzeugt_Storno_und_Neubeleg()
    {
        var (storno, neu) = _aenderung.Korrigieren(_beleg,
            _beleg with { BelegRef = "C1-K", Datum = D(2026, 4, 2), Betrag = new CurrencyAmount(89m, "USD") }, "Rabatt");

        Assert.Equal("C1", storno.StornoVon);
        Assert.Equal(D(2026, 4, 2), storno.Datum);
        Assert.Equal("C1", neu.KorrekturVon);
        Assert.Equal(2, _protokoll.Eintraege.Count);
        Assert.Equal(["STORNO", "KORREKTUR"], _protokoll.Eintraege.Select(p => p.Aktion));
        Assert.True(_protokoll.PruefeKette());
    }

    [Fact]
    public void Aenderung_in_offener_Periode_protokolliert_jedes_Feld()
    {
        var offen = _beleg with { Datum = D(2026, 4, 10) };
        _aenderung.Aendern(offen, offen with { Betrag = new CurrencyAmount(89m, "USD"), Text = "Reset" }, "Korrektur Erfassung");

        Assert.Equal(2, _protokoll.Eintraege.Count);
        var betrag = _protokoll.Eintraege.Single(p => p.Feld == "Betrag");
        Assert.Equal("99 USD", betrag.Alt);
        Assert.Equal("89 USD", betrag.Neu);
        Assert.Equal("AENDERUNG", betrag.Aktion);
    }

    [Fact]
    public void Aenderung_mit_Zieldatum_in_festgeschriebener_Periode_ist_unzulaessig()
    {
        var offen = _beleg with { Datum = D(2026, 4, 10) };
        Assert.Throws<PeriodeFestgeschriebenException>(() => _aenderung.Aendern(offen, offen with { Datum = D(2026, 3, 1) }, "x"));
    }

    [Fact]
    public void Erfassen_in_festgeschriebener_Periode_ist_unzulaessig_sonst_Protokoll()
    {
        Assert.Throws<PeriodeFestgeschriebenException>(() => _aenderung.Erfassen(_beleg, "Nacherfassung"));
        _aenderung.Erfassen(_beleg with { Datum = D(2026, 5, 1) }, "Erfassung");
        Assert.Equal("ANLAGE", Assert.Single(_protokoll.Eintraege).Aktion);
    }

    [Fact]
    public void Begruendung_ist_Pflicht()
    {
        Assert.Throws<ArgumentException>(() => _aenderung.Stornieren(_beleg, D(2026, 4, 2), " "));
        Assert.Throws<ArgumentException>(() => _aenderung.Erfassen(_beleg, ""));
    }

    [Fact]
    public void Manipulation_der_Kette_wird_erkannt()
    {
        _aenderung.Stornieren(_beleg, D(2026, 4, 2), "a");
        _aenderung.Erfassen(_beleg with { Datum = D(2026, 5, 1) }, "b");
        var manipuliert = _protokoll.Eintraege.ToList();
        manipuliert[0] = manipuliert[0] with { Begruendung = "geändert" };

        Assert.True(Protokoll.PruefeKette(_protokoll.Eintraege));
        Assert.False(Protokoll.PruefeKette(manipuliert));
        Assert.False(Protokoll.PruefeKette([manipuliert[1]])); // Lücke am Anfang
        Assert.Throws<InvalidOperationException>(() => new Protokoll(manipuliert));
    }

    [Fact]
    public void Geladenes_Protokoll_setzt_Kette_fort()
    {
        _aenderung.Stornieren(_beleg, D(2026, 4, 2), "a");
        var geladen = new Protokoll(_protokoll.Eintraege);
        var e = geladen.Anhaengen(Jetzt, "Inhaber", "beleg", "C2", "ANLAGE", null, null, null, "neu");

        Assert.Equal(2, e.Nr);
        Assert.Equal(_protokoll.Eintraege[0].Hash, e.PrevHash);
        Assert.True(geladen.PruefeKette());
    }
}
