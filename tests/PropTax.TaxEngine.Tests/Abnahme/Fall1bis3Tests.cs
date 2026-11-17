using PropTax.Domain;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Abnahme;

public class Fall1bis3Tests
{
    [Fact]
    public void Fall1_KU_Challenge_99_USD_VAE()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge());

        Assert.Equal(Leistungsart.SonstigeLeistung, e.Leistungsart);
        Assert.Equal(Steuerbarkeit.Steuerbar, e.Steuerbarkeit);
        Assert.Equal(Steuerschuldner.Empfaenger13bAbs2Nr1, e.Steuerschuldner);
        Assert.Equal(Besteuerungsart.Kleinunternehmer, e.Besteuerungsart);
        Assert.Equal(1.0800m, e.Kurs);
        Assert.Equal(91.67m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(17.42m, e.Steuer.Betrag);
        Assert.Equal(91.67m, e.Kz("52").Betrag);
        Assert.Equal(17.42m, e.Kz("53").Betrag);
        Assert.Equal(0m, e.Kz("67").Betrag);
        Assert.Equal(0m, e.Vorsteuer.Betrag);
        Assert.Equal(17.42m, e.Zahllast.Betrag);
        Assert.Equal(UstZeitraum.Quartal(2026, 1), e.Zeitraum);

        var va = UstVaAggregation.Berechne([e], UstZeitraum.Quartal(2026, 1));
        Assert.Equal(17.42m, va.Zahllast.Betrag);
        Assert.Same(e, Assert.Single(va.Kz("53")!.Belege));
    }

    [Fact]
    public void Fall2_Regelbesteuerung_Zahllast_null()
    {
        var rw = NeuesRegelwerk().SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung);
        var e = new Steuerermittlung(rw).Ermittle(Challenge());

        Assert.Equal(91.67m, e.Kz("52").Betrag);
        Assert.Equal(17.42m, e.Kz("53").Betrag);
        Assert.Equal(17.42m, e.Kz("67").Betrag);
        Assert.Equal(0.00m, e.Zahllast.Betrag);

        var va = UstVaAggregation.Berechne([e], UstZeitraum.Quartal(2026, 1));
        Assert.Equal(0.00m, va.Zahllast.Betrag);
        Assert.Equal(17.42m, va.Steuer.Betrag);
        Assert.Equal(17.42m, va.Vorsteuer.Betrag);
        // Formularansicht: BMG in vollen Euro, Steuer mit Cent (O-08)
        Assert.Equal(91m, va.Kz("52")!.Formularwert);
        Assert.Equal(17.42m, va.Kz("53")!.Formularwert);
        Assert.Equal(17.42m, va.Kz("67")!.Formularwert);
    }

    [Fact]
    public void Fall3_KU_Abo_30_EUR_von_EU_Unternehmer_IE()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(AboEu());

        Assert.Equal(Steuerschuldner.Empfaenger13bAbs1, e.Steuerschuldner);
        Assert.Null(e.Kurs);
        Assert.Equal(30.00m, e.Kz("46").Betrag);
        Assert.Equal(5.70m, e.Kz("47").Betrag);
        Assert.Equal(0m, e.Kz("67").Betrag);
        Assert.Equal(5.70m, e.Zahllast.Betrag);
    }
}
