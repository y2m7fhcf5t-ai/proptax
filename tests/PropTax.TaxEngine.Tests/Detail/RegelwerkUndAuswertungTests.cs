using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Detail;

public class RegelwerkUndAuswertungTests
{
    [Fact]
    public void Startwerte_Status_Saetze_validiert_Rest_ungeprueft()
    {
        var rw = JsonRegelwerk.LadeStartwerte();
        Assert.NotEmpty(rw.RegelListe);
        Assert.All(rw.RegelListe, r => Assert.Equal(PruefStatus.Ungeprueft, r.Status));
        Assert.All(rw.Kennzahlen, k => Assert.Equal(PruefStatus.Ungeprueft, k.Status));
        Assert.All(rw.Grenzwerte19, g => Assert.Equal(PruefStatus.Ungeprueft, g.Status));
        Assert.All(rw.Steuersaetze, s => Assert.Equal(PruefStatus.Validiert, s.Status));
        Assert.Empty(rw.Kurse);
    }

    [Theory]
    [InlineData(2020, 6, 30, "0.19")]
    [InlineData(2020, 7, 1, "0.16")]
    [InlineData(2020, 12, 31, "0.16")]
    [InlineData(2021, 1, 1, "0.19")]
    public void Steuersatz_datumsabhaengig(int j, int m, int t, string satz) =>
        Assert.Equal(decimal.Parse(satz, System.Globalization.CultureInfo.InvariantCulture), JsonRegelwerk.LadeStartwerte().Satz("STANDARD", D(j, m, t)));

    [Fact]
    public void Fehlende_Regelwerksdaten_sind_Fehler()
    {
        var rw = JsonRegelwerk.LadeStartwerte();
        Assert.Throws<RegelwerkException>(() => rw.Satz("GIBTS_NICHT", D(2026, 1, 1)));
        Assert.Throws<RegelwerkException>(() => rw.Grenzwert19(2030));
        Assert.Throws<RegelwerkException>(() => rw.Einstellung("gibts.nicht", D(2026, 1, 1)));
        Assert.Throws<RegelwerkException>(() => rw.Kurs("CHF", 2026, 1, Kursquelle.Bmf));
        rw.Besteuerungsarten.Clear();
        Assert.Throws<RegelwerkException>(() => rw.Besteuerungsart(D(2026, 1, 1)));
    }

    [Fact]
    public void Einstellung_juengster_Stichtag_gewinnt()
    {
        var rw = JsonRegelwerk.LadeStartwerte()
            .SetzeEinstellung("x", "alt", D(2025, 1, 1))
            .SetzeEinstellung("x", "neu", D(2026, 7, 1));
        Assert.Equal("alt", rw.Einstellung("x", D(2026, 6, 30)));
        Assert.Equal("neu", rw.Einstellung("x", D(2026, 7, 1)));
    }

    [Fact]
    public void Kurs_ueberschreiben_ersetzt_bestehenden()
    {
        var rw = JsonRegelwerk.LadeStartwerte().SetzeKurs("USD", 2026, 1, 1.05m).SetzeKurs("USD", 2026, 1, 1.06m);
        Assert.Single(rw.Kurse);
        Assert.Equal(1.06m, rw.Kurs("USD", 2026, 1, Kursquelle.Bmf));
    }

    [Fact]
    public void Lade_aus_Json()
    {
        var rw = JsonRegelwerk.Lade("""{"einstellungen":[{"schluessel":"a","wert":"b","ab":"2026-01-01"}]}""");
        Assert.Equal("b", rw.Einstellung("a", D(2026, 1, 1)));
        Assert.Empty(rw.RegelListe);
    }

    [Fact]
    public void Formularrundung_kaufmaennisch_und_negative_Werte()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var original = Challenge();
        var oe = engine.Ermittle(original);
        var r = engine.Ermittle(new BelegEingabe
        {
            BelegRef = "R", Typ = Belegtyp.Erstattung, Richtung = Richtung.Eingang, PartnerKategorie = PartnerKategorie.Drittland,
            Leistungsdatum = D(2026, 4, 5), Zahlungsdatum = D(2026, 4, 5), Betrag = new CurrencyAmount(99m, "USD"),
            Bezug = new Erstattungsbezug(original, oe),
        });
        Assert.Equal(92m, UstVaAggregation.Berechne([oe], UstZeitraum.Quartal(2026, 1), FormularRundung.Kaufmaennisch).Kz("52")!.Formularwert);
        Assert.Equal(-91m, UstVaAggregation.Berechne([r], UstZeitraum.Quartal(2026, 2)).Kz("52")!.Formularwert);
    }

    [Fact]
    public void Aggregation_filtert_Zeitraum_und_summiert()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var e = new[]
        {
            engine.Ermittle(Challenge("C1")),
            engine.Ermittle(Challenge("C2")),
            engine.Ermittle(AboEu("A1")),
            engine.Ermittle(Challenge("C3", D(2026, 4, 2))),
        };
        var va = UstVaAggregation.Berechne(e, UstZeitraum.Quartal(2026, 1));
        Assert.Equal(3, va.Belege.Count);
        Assert.Equal(183.34m, va.Betrag("52").Betrag);
        Assert.Equal(34.84m, va.Betrag("53").Betrag);
        Assert.Equal(40.54m, va.Zahllast.Betrag);
        Assert.Equal(Money.Null, va.Betrag("99"));
        Assert.Null(va.Kz("99"));
        Assert.Equal(["46", "47", "52", "53"], va.Kennzahlen.Select(k => k.Kennzahl));
    }

    [Fact]
    public void UstZeitraum_Bildung_und_Anzeige()
    {
        Assert.Equal("2026-Q2", UstZeitraum.Aus(D(2026, 6, 30), ZeitraumArt.Quartal).ToString());
        Assert.Equal("2026-03", UstZeitraum.Aus(D(2026, 3, 1), ZeitraumArt.Monat).ToString());
        Assert.Equal("2026", UstZeitraum.Aus(D(2026, 3, 1), ZeitraumArt.Jahr).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => UstZeitraum.Aus(D(2026, 3, 1), (ZeitraumArt)9));
    }

    [Fact]
    public void Money_und_CurrencyAmount_Randfaelle()
    {
        Assert.Equal("1.234,50 €", Money.Euro(1234.5m).ToString());
        Assert.Equal(Money.Euro(-5m), -Money.Euro(5m));
        Assert.Equal(Money.Euro(1m), Money.Euro(3m) - Money.Euro(2m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurrencyAmount(1m, "USD").InEuro(0m));
        Assert.Equal(Money.Euro(5m), new CurrencyAmount(5m, "EUR").InEuro(1.5m));
    }
}

public class LaenderTests
{
    [Theory]
    [InlineData("DE", PartnerKategorie.Inland)]
    [InlineData("ie", PartnerKategorie.Eu)]
    [InlineData("AE", PartnerKategorie.Drittland)]
    [InlineData("GB", PartnerKategorie.Drittland)]
    [InlineData("US", PartnerKategorie.Drittland)]
    public void Kategorie_aus_Land(string land, PartnerKategorie erwartet) => Assert.Equal(erwartet, Laender.KategorieAus(land));

    [Fact]
    public void Ohne_Land_ungeklaert() => Assert.Null(Laender.KategorieAus(" "));
}
