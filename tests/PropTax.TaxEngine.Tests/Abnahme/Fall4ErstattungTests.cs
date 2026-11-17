using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Abnahme;

public class Fall4ErstattungTests
{
    private static BelegEingabe Refund(BelegEingabe original, SteuerErgebnis ergebnis, decimal usd = 99.00m, string waehrung = "USD") => new()
    {
        BelegRef = "R1", Betrieb = original.Betrieb, Typ = Belegtyp.Erstattung, Richtung = Richtung.Eingang,
        PartnerKategorie = original.PartnerKategorie, Leistungsdatum = D(2026, 4, 5), Zahlungsdatum = D(2026, 4, 5),
        Betrag = new CurrencyAmount(usd, waehrung), Bezug = new Erstattungsbezug(original, ergebnis),
    };

    [Fact]
    public void Fall4_Refund_im_Folgemonat_negativ_im_Erstattungszeitraum()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var original = Challenge();
        var oe = engine.Ermittle(original);
        var r = engine.Ermittle(Refund(original, oe));

        Assert.Equal(UstZeitraum.Quartal(2026, 2), r.Zeitraum);
        Assert.Equal(D(2026, 4, 5), r.Entstehungsdatum);
        Assert.Equal(-91.67m, r.Bemessungsgrundlage.Betrag);
        Assert.Equal(-17.42m, r.Steuer.Betrag);
        Assert.Equal(-91.67m, r.Kz("52").Betrag);
        Assert.Equal(-17.42m, r.Kz("53").Betrag);
        Assert.Equal(-17.42m, r.Zahllast.Betrag);
        Assert.Equal(1.0800m, r.Kurs); // Startwert erstattung.kurs = ORIGINAL

        var alle = new[] { oe, r };
        Assert.Equal(17.42m, UstVaAggregation.Berechne(alle, UstZeitraum.Quartal(2026, 1)).Zahllast.Betrag);
        Assert.Equal(-17.42m, UstVaAggregation.Berechne(alle, UstZeitraum.Quartal(2026, 2)).Zahllast.Betrag);
    }

    [Fact]
    public void Fall4_Variante_Kurs_des_Erstattungsmonats()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("erstattung.kurs", "ERSTATTUNGSMONAT");
        var engine = new Steuerermittlung(rw);
        var original = Challenge();
        var r = engine.Ermittle(Refund(original, engine.Ermittle(original)));

        Assert.Equal(1.1000m, r.Kurs);
        Assert.Equal(-90.00m, r.Bemessungsgrundlage.Betrag);
        Assert.Equal(-17.10m, r.Steuer.Betrag);
    }

    [Fact]
    public void Fall4_Regelbesteuerung_Vorsteuer_wird_mitkorrigiert()
    {
        var rw = NeuesRegelwerk().SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung);
        var engine = new Steuerermittlung(rw);
        var original = Challenge();
        var r = engine.Ermittle(Refund(original, engine.Ermittle(original)));

        Assert.Equal(-17.42m, r.Kz("67").Betrag);
        Assert.Equal(0m, r.Zahllast.Betrag);
    }

    [Fact]
    public void Teilerstattung_wird_anteilig_korrigiert()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var original = Challenge();
        var r = engine.Ermittle(Refund(original, engine.Ermittle(original), usd: 49.50m));
        Assert.Equal(-45.83m, r.Bemessungsgrundlage.Betrag); // 49,50 / 1,08 = 45,833…
        Assert.Equal(-8.71m, r.Steuer.Betrag);               // 45,83 × 19 % = 8,7077
    }

    [Fact]
    public void Erstattung_ohne_Bezug_ist_Fehler()
    {
        var b = Challenge() with { Typ = Belegtyp.Erstattung };
        Assert.Throws<RegelwerkException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(b));
    }

    [Fact]
    public void Erstattung_in_anderer_Waehrung_ist_Fehler()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var original = Challenge();
        var refund = Refund(original, engine.Ermittle(original), 90m, "EUR");
        Assert.Throws<RegelwerkException>(() => engine.Ermittle(refund));
    }

    [Fact]
    public void Erstattung_mit_unbekannter_Ursprungsregel_ist_Fehler()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var original = Challenge();
        var oe = engine.Ermittle(original) with { RegelCode = "GIBT_ES_NICHT" };
        Assert.Throws<RegelwerkException>(() => engine.Ermittle(Refund(original, oe)));
    }

    [Fact]
    public void Erstattung_EUR_Abo_ohne_Kurs()
    {
        var engine = new Steuerermittlung(NeuesRegelwerk());
        var abo = AboEu();
        var r = engine.Ermittle(new BelegEingabe
        {
            BelegRef = "R2", Typ = Belegtyp.Erstattung, Richtung = Richtung.Eingang, PartnerKategorie = PartnerKategorie.Eu,
            Leistungsdatum = D(2026, 3, 1), Betrag = new CurrencyAmount(30m, "EUR"),
            Bezug = new Erstattungsbezug(abo, engine.Ermittle(abo)),
        });
        Assert.Null(r.Kurs);
        Assert.Equal(-5.70m, r.Kz("47").Betrag);
        Assert.Equal(new Money(-30m), r.Bemessungsgrundlage);
    }
}
