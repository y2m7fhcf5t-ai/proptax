using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Detail;

public class ErmittlungDetailTests
{
    [Fact]
    public void Keine_passende_Regel_ist_Fehler()
    {
        var b = Verkauf("V", "A", D(2026, 3, 1), 10m) with { PartnerKategorie = PartnerKategorie.Drittland };
        var ex = Assert.Throws<RegelwerkException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(b));
        Assert.Contains("Keine Steuerregel", ex.Message);
    }

    [Fact]
    public void Mehrdeutige_Regeln_gleicher_Prioritaet_sind_Fehler()
    {
        var rw = NeuesRegelwerk();
        var regel = rw.RegelListe.First(r => r.Code == "EINGANG_DRITTLAND_SONST_LEIST");
        rw.RegelListe.Add(regel with { Code = "DUPLIKAT" });
        var ex = Assert.Throws<RegelwerkException>(() => new Steuerermittlung(rw).Ermittle(Challenge()));
        Assert.Contains("DUPLIKAT", ex.Message);
    }

    [Fact]
    public void Hoehere_Prioritaet_gewinnt_und_Gueltigkeit_wird_beachtet()
    {
        var rw = NeuesRegelwerk();
        var regel = rw.RegelListe.First(r => r.Code == "EINGANG_DRITTLAND_SONST_LEIST");
        rw.RegelListe.Add(regel with { Code = "SPEZIAL", Prioritaet = 99, GueltigAb = D(2026, 1, 1), GueltigBis = D(2026, 2, 28) });
        var engine = new Steuerermittlung(rw);
        Assert.Equal("SPEZIAL", engine.Ermittle(Challenge(datum: D(2026, 2, 10)) with { Betrag = new CurrencyAmount(10m, "EUR") }).RegelCode);
        Assert.Equal("EINGANG_DRITTLAND_SONST_LEIST", engine.Ermittle(Challenge()).RegelCode);
    }

    [Fact]
    public void Drittland_Entstehung_spaetestens_Ablauf_Folgemonat()
    {
        var b = Challenge() with { Rechnungsdatum = D(2026, 6, 1) };
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(b);
        Assert.Equal(D(2026, 4, 30), e.Entstehungsdatum);
        Assert.Equal(UstZeitraum.Quartal(2026, 2), e.Zeitraum);
        Assert.Equal(1.1000m, e.Kurs); // Monatskurs des Entstehungsmonats
    }

    [Fact]
    public void Drittland_ohne_Rechnungsdatum_Ablauf_Folgemonat()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge() with { Rechnungsdatum = null, Zahlungsdatum = null, Betrag = new CurrencyAmount(10m, "EUR") });
        Assert.Equal(D(2026, 4, 30), e.Entstehungsdatum);
    }

    [Fact]
    public void Vorauszahlung_13b_entsteht_mit_Zahlung()
    {
        var b = AboEu(datum: D(2026, 4, 1)) with { Zahlungsdatum = D(2026, 3, 20) };
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(b);
        Assert.Equal(D(2026, 3, 20), e.Entstehungsdatum);
        Assert.Equal(UstZeitraum.Quartal(2026, 1), e.Zeitraum);
    }

    private static BelegEingabe InlandRechnung() => new()
    {
        BelegRef = "I1", Typ = Belegtyp.AboSoftware, Richtung = Richtung.Eingang, PartnerKategorie = PartnerKategorie.Inland,
        Leistungsdatum = D(2026, 3, 28), Rechnungsdatum = D(2026, 4, 2), Zahlungsdatum = D(2026, 4, 3),
        Betrag = new CurrencyAmount(119m, "EUR"), AusgewieseneUst = Money.Euro(19m),
    };

    [Fact]
    public void Inlandsrechnung_KU_ohne_Vorsteuer()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(InlandRechnung());
        Assert.Equal(Steuerschuldner.Leistender, e.Steuerschuldner);
        Assert.Equal(100m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(0m, e.Vorsteuer.Betrag);
        Assert.Empty(e.Kennzahlen);
        Assert.Equal(D(2026, 4, 2), e.Entstehungsdatum); // später aus Leistung und Rechnung
    }

    [Fact]
    public void Inlandsrechnung_Regelbesteuerung_Vorsteuer_Kz66()
    {
        var rw = NeuesRegelwerk().SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung);
        var e = new Steuerermittlung(rw).Ermittle(InlandRechnung());
        Assert.Equal(19m, e.Kz("66").Betrag);
        Assert.Equal(-19m, e.Zahllast.Betrag);
    }

    [Fact]
    public void Inlandsrechnung_ohne_ausgewiesene_USt()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(InlandRechnung() with { AusgewieseneUst = null, Rechnungsdatum = null });
        Assert.Equal(119m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(D(2026, 3, 28), e.Entstehungsdatum);
    }

    [Fact]
    public void Vereinnahmung_ohne_Zahlungsdatum_ist_Fehler() =>
        Assert.Throws<RegelwerkException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(Payout() with { Zahlungsdatum = null }));

    [Fact]
    public void Fehlender_Kurs_ist_Fehler()
    {
        var ex = Assert.Throws<RegelwerkException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge(datum: D(2026, 7, 1))));
        Assert.Contains("USD", ex.Message);
    }

    [Fact]
    public void Fehlende_Kennzahlzuordnung_ist_Fehler()
    {
        var ex = Assert.Throws<RegelwerkException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(AboEu(datum: D(2030, 1, 10))));
        Assert.Contains("2030", ex.Message);
    }

    [Fact]
    public void Begruendungskette_nennt_Regel_Kurs_und_Besteuerungsart()
    {
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge());
        Assert.Contains(e.Begruendung, s => s.Contains("EINGANG_DRITTLAND_SONST_LEIST"));
        Assert.Contains(e.Begruendung, s => s.Contains("1,08"));
        Assert.Contains(e.Begruendung, s => s.Contains("Kleinunternehmer"));
        Assert.False(e.IstManuell);
    }

    [Fact]
    public void Uebersteuerung_ohne_Begruendung_ist_Fehler() =>
        Assert.Throws<ArgumentException>(() => new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge(), new Uebersteuerung("  ")));

    [Fact]
    public void Uebersteuerung_wird_angewandt_und_markiert()
    {
        var u = new Uebersteuerung("Rechnung von irischer Niederlassung")
        {
            Steuerschuldner = Steuerschuldner.Empfaenger13bAbs1, KennzahlSchluessel = "13B_ABS1", Satz = 0.07m, Kurs = 1.1000m,
            Entstehungsdatum = D(2026, 3, 31), Steuerbarkeit = Steuerbarkeit.Steuerbar,
        };
        var e = new Steuerermittlung(NeuesRegelwerk()).Ermittle(Challenge(), u);

        Assert.True(e.IstManuell);
        Assert.Equal(u.Begruendung, e.ManuelleBegruendung);
        Assert.Equal(90.00m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(6.30m, e.Steuer.Betrag);
        Assert.Equal(90.00m, e.Kz("46").Betrag);
        Assert.Equal(0m, e.Kz("52").Betrag);
        Assert.Contains(e.Begruendung, s => s.StartsWith("Manuell"));
    }

    [Fact]
    public void Verkauf_Regelbesteuerung_Netto()
    {
        var rw = NeuesRegelwerk().SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung)
            .SetzeEinstellung("betrag_ist.Verkauf", "NETTO");
        var e = new Steuerermittlung(rw).Ermittle(Verkauf("V", "A", D(2026, 3, 1), 100m));
        Assert.Equal(100m, e.Kz("81").Betrag);
        Assert.Equal(19m, e.Steuer.Betrag);
        Assert.Equal(100m, e.Umsatz19.Betrag);
    }

    [Fact]
    public void Unbekannter_Betragsmodus_ist_Fehler()
    {
        var rw = NeuesRegelwerk().SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung)
            .SetzeEinstellung("betrag_ist.Verkauf", "IRGENDWAS");
        Assert.Throws<RegelwerkException>(() => new Steuerermittlung(rw).Ermittle(Verkauf("V", "A", D(2026, 3, 1), 100m)));
    }

    [Fact]
    public void Steuerpflichtige_Regel_ohne_Satz_ist_Fehler()
    {
        var rw = NeuesRegelwerk();
        var i = rw.RegelListe.FindIndex(r => r.Code == "EINGANG_DRITTLAND_SONST_LEIST");
        rw.RegelListe[i] = rw.RegelListe[i] with { SatzSchluessel = null };
        Assert.Throws<RegelwerkException>(() => new Steuerermittlung(rw).Ermittle(Challenge()));
    }

    [Fact]
    public void Monatlicher_Voranmeldungszeitraum_konfigurierbar()
    {
        var rw = NeuesRegelwerk().SetzeEinstellung("ust.voranmeldungszeitraum", "Monat");
        Assert.Equal(new UstZeitraum(2026, ZeitraumArt.Monat, 3), new Steuerermittlung(rw).Ermittle(Challenge()).Zeitraum);
    }
}
