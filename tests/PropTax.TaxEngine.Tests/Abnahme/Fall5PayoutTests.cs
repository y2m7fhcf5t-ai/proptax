using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Abnahme;

/// <summary>2.000 USD @ 1,08 = 1.851,85 EUR. Behandlung konfigurierbar (O-04).</summary>
public class Fall5PayoutTests
{
    private static Money Grenzsumme(SteuerErgebnis e) =>
        Kleinunternehmergrenze.Pruefe(UmsatzPosition.Aus([e]), 2026, Money.Null, NeuesRegelwerk()).Summe;

    [Fact]
    public void Fall5_nicht_steuerbar_Kz45_und_kein_19_Umsatz()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Payout());

        Assert.Equal(Steuerbarkeit.NichtSteuerbar, e.Steuerbarkeit);
        Assert.Equal(1851.85m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(1851.85m, e.Kz("45").Betrag);
        Assert.Equal(Money.Null, e.Umsatz19);
        Assert.Equal(0m, e.Zahllast.Betrag);
        Assert.Equal(Money.Null, Grenzsumme(e));
    }

    [Fact]
    public void Fall5_steuerbar_KU_zaehlt_zur_19_Grenze_ohne_Steuer()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("ust.payout_behandlung", "STEUERBAR");
        var e = new Steuerermittlung(rw).Ermittle(Payout());

        Assert.Equal(Steuerbarkeit.Steuerbar, e.Steuerbarkeit);
        Assert.Equal(1851.85m, e.Umsatz19.Betrag);
        Assert.Empty(e.Kennzahlen);
        Assert.Equal(0m, e.Steuer.Betrag);
        Assert.Equal(0m, e.Zahllast.Betrag);
        Assert.Equal(1851.85m, Grenzsumme(e).Betrag);
    }

    [Fact]
    public void Fall5_steuerbar_Regelbesteuerung_Brutto()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("ust.payout_behandlung", "STEUERBAR")
            .SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung);
        var e = new Steuerermittlung(rw).Ermittle(Payout());

        Assert.Equal(1556.18m, e.Kz("81").Betrag);
        Assert.Equal(295.67m, e.Steuer.Betrag);
        Assert.Equal(295.67m, e.Zahllast.Betrag);
        Assert.Equal(0m, e.Kz("45").Betrag);
    }

    [Fact]
    public void Fall5_steuerbar_Regelbesteuerung_Netto()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("ust.payout_behandlung", "STEUERBAR")
            .SetzeEinstellung("betrag_ist.Payout", "NETTO")
            .SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung);
        var e = new Steuerermittlung(rw).Ermittle(Payout());

        Assert.Equal(1851.85m, e.Kz("81").Betrag);
        Assert.Equal(351.85m, e.Steuer.Betrag);
    }

    [Fact]
    public void Fall5_kein_Leistungsaustausch_ohne_jede_Wirkung()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("ust.payout_behandlung", "KEIN_LEISTUNGSAUSTAUSCH");
        var e = new Steuerermittlung(rw).Ermittle(Payout());

        Assert.Equal(Steuerbarkeit.KeinLeistungsaustausch, e.Steuerbarkeit);
        Assert.Empty(e.Kennzahlen);
        Assert.Equal(Money.Null, e.Umsatz19);
        Assert.Equal(Money.Null, e.Bemessungsgrundlage);
    }
}
