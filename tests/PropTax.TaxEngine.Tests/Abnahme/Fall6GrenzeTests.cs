using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Abnahme;

public class Fall6GrenzeTests
{
    private static SteuerErgebnis[] Umsaetze(Steuerermittlung engine, bool mitDrittem = true)
    {
        var belege = new List<BelegEingabe>
        {
            Verkauf("V1", "Onlinehandel", D(2026, 3, 1), 60_000m),
            Verkauf("V2", "Prop-Trading", D(2026, 6, 1), 25_000m),
        };
        if (mitDrittem) belege.Add(Verkauf("V3", "Prop-Trading", D(2026, 9, 15), 16_000m));
        return belege.Select(b => engine.Ermittle(b)).ToArray();
    }

    [Fact]
    public void Fall6_Ueberschreitung_ueber_beide_Betriebe_Warnung_und_Umschaltvorschlag()
    {
        var rw = NeuesRegelwerk();
        var ergebnisse = Umsaetze(new Steuerermittlung(rw));
        var s = Kleinunternehmergrenze.Pruefe(UmsatzPosition.Aus(ergebnisse), 2026, Money.Euro(20_000m), rw);

        Assert.Equal(101_000m, s.Summe.Betrag);
        Assert.Equal(100_000m, s.Grenze.Betrag);
        Assert.Equal(1.01m, s.Quote);
        Assert.False(s.VorjahrUeberschritten);
        Assert.Equal(60_000m, s.JeBetrieb["Onlinehandel"].Betrag);
        Assert.Equal(41_000m, s.JeBetrieb["Prop-Trading"].Betrag);

        Assert.Collection(s.Warnungen,
            w => { Assert.Equal(GrenzStufe.Warnschwelle, w.Stufe); Assert.Equal("V2", w.BelegRef); Assert.Equal(85_000m, w.SummeNachBeleg.Betrag); },
            w => { Assert.Equal(GrenzStufe.Ueberschritten, w.Stufe); Assert.Equal("V3", w.BelegRef); });

        Assert.NotNull(s.Vorschlag);
        Assert.Equal(D(2026, 9, 15), s.Vorschlag!.AbDatum);
        Assert.Equal("V3", s.Vorschlag.BelegRef);

        // Umschaltung übernehmen: der überschreitende Umsatz unterliegt bereits der Regelbesteuerung
        rw.SetzeBesteuerungsart(s.Vorschlag.AbDatum, Besteuerungsart.Regelbesteuerung);
        var v3 = new Steuerermittlung(rw).Ermittle(Verkauf("V3", "Prop-Trading", D(2026, 9, 15), 16_000m));
        Assert.Equal(Besteuerungsart.Regelbesteuerung, v3.Besteuerungsart);
        Assert.Equal(13_445.38m, v3.Kz("81").Betrag);
        Assert.Equal(2_554.62m, v3.Steuer.Betrag);
    }

    [Fact]
    public void Nur_Warnschwelle_ohne_Vorschlag()
    {
        var rw = NeuesRegelwerk();
        var s = Kleinunternehmergrenze.Pruefe(UmsatzPosition.Aus(Umsaetze(new Steuerermittlung(rw), false)), 2026, Money.Null, rw);
        Assert.Single(s.Warnungen);
        Assert.Null(s.Vorschlag);
    }

    [Fact]
    public void KU_Umsaetze_erscheinen_nicht_in_Kennzahlen()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Verkauf("V1", "Onlinehandel", D(2026, 3, 1), 1_000m));
        Assert.Empty(e.Kennzahlen);
        Assert.Equal(1_000m, e.Umsatz19.Betrag);
        Assert.Equal(0m, e.Steuer.Betrag);
    }

    [Fact]
    public void Vorjahresgrenze_ueberschritten_KU_ab_Jahresbeginn_ausgeschlossen()
    {
        var rw = NeuesRegelwerk();
        var s = Kleinunternehmergrenze.Pruefe([], 2026, Money.Euro(25_000.01m), rw);
        Assert.True(s.VorjahrUeberschritten);
        Assert.Equal(D(2026, 1, 1), s.Vorschlag!.AbDatum);
        Assert.Null(s.Vorschlag.BelegRef);
    }

    [Fact]
    public void Positionen_anderer_Jahre_werden_ignoriert()
    {
        var rw = NeuesRegelwerk();
        var s = Kleinunternehmergrenze.Pruefe(
            [new UmsatzPosition("X", "A", D(2025, 12, 31), Money.Euro(99_000m))], 2026, Money.Null, rw);
        Assert.Equal(Money.Null, s.Summe);
        Assert.Empty(s.Warnungen);
    }
}
