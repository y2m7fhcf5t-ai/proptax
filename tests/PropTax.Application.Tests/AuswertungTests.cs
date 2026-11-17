using System.IO.Compression;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.Application.Tests;

public class AuswertungTests : Basis
{
    private long Payout(string nr, DateOnly datum, decimal usd) => Gebucht(new BelegDaten
    {
        Belegnummer = nr, BetriebId = PropTrading, PartnerId = FundedNext, Typ = Belegtyp.Payout, Richtung = Richtung.Ausgang,
        Leistungsdatum = datum, Zahlungsdatum = datum, Betrag = new CurrencyAmount(usd, "USD"),
    });

    private long Abo(string nr = "A1") => Gebucht(new BelegDaten
    {
        Belegnummer = nr, BetriebId = PropTrading, PartnerId = IrischerAnbieter, Typ = Belegtyp.AboSoftware, Richtung = Richtung.Eingang,
        Leistungsdatum = D(2026, 2, 1), Zahlungsdatum = D(2026, 2, 1), Betrag = new CurrencyAmount(30m, "EUR"),
    });

    private long Verkauf(string nr, DateOnly d, decimal eur) => Gebucht(new BelegDaten
    {
        Belegnummer = nr, BetriebId = Onlinehandel, PartnerId = Kunde, Typ = Belegtyp.Verkauf, Richtung = Richtung.Ausgang,
        Leistungsdatum = d, Zahlungsdatum = d, Betrag = new CurrencyAmount(eur, "EUR"),
    });

    [Fact]
    public void UstVa_mit_Bezeichnungen_Formularwerten_und_Drilldown()
    {
        var c1 = Gebucht(Challenge());
        Abo();
        var v = Ab.Auswertungen.UstVa(UstZeitraum.Quartal(2026, 1));

        Assert.Equal(["46", "47", "52", "53"], v.Kennzahlen.Select(k => k.Kennzahl));
        var kz52 = v.Kennzahlen.Single(k => k.Kennzahl == "52");
        Assert.Contains("Bemessungsgrundlage", kz52.Bezeichnung);
        Assert.Equal((91.67m, 91m), (kz52.Betrag, kz52.Formularwert));
        var drill = Assert.Single(kz52.Belege);
        Assert.Equal((c1, "C1", "Funded Next", 91.67m, 99m), (drill.BelegId, drill.Belegnummer, drill.Partner, drill.Wert, drill.BetragFw));
        Assert.Contains("Steuer", v.Kennzahlen.Single(k => k.Kennzahl == "53").Bezeichnung);

        Assert.Equal(23.12m, v.Zeitraum.Zahllast);
        Assert.True(v.Zeitraum.Pflicht);
        Assert.Contains("§18 Abs. 4a", v.Zeitraum.PflichtGrund);
        Assert.Equal(D(2026, 4, 10), v.Zeitraum.Faelligkeit.Tatsaechlich);
        Assert.Equal(2, v.Belege.Count);
    }

    [Fact]
    public void Zahllast_und_Fristen_je_Quartal_oder_Monat()
    {
        Gebucht(Challenge());
        var z = Ab.Auswertungen.Zahllasten(2026);
        Assert.Equal(4, z.Count);
        Assert.True(z[0].Pflicht);
        Assert.False(z[1].Pflicht);
        Assert.Contains("keine Voranmeldung", z[1].PflichtGrund);
        Assert.Equal(D(2026, 10, 12), z[2].Faelligkeit.Tatsaechlich);
        Assert.Equal("Samstag, Sonntag", z[2].Faelligkeit.Verschiebungsgrund);

        Ab.Perioden.FestschreibenUst(UstZeitraum.Quartal(2026, 1), "VA Q1");
        Assert.True(Ab.Auswertungen.Zahllasten(2026)[0].Festgeschrieben);

        Ab.Einstellungen.SetzeBesteuerungsart(D(2026, 4, 1), Besteuerungsart.Regelbesteuerung, "Option");
        Assert.True(Ab.Auswertungen.Zahllasten(2026)[1].Pflicht);

        Ab.Einstellungen.SetzeEinstellung("ust.voranmeldungszeitraum", "Monat", D(2027, 1, 1), "Monatszahler ab 2027");
        Assert.Equal(12, Ab.Auswertungen.Zeitraeume(2027).Count);
    }

    [Fact]
    public void Grenzmonitor_mit_berechnetem_und_manuellem_Vorjahr_und_Umschaltung()
    {
        Verkauf("V0", D(2025, 6, 1), 30_000m);
        Verkauf("V1", D(2026, 3, 1), 60_000m);
        Verkauf("V2", D(2026, 9, 15), 45_000m);

        var g = Ab.Auswertungen.Grenze(2026);
        Assert.Equal(30_000m, g.Vorjahresumsatz);
        Assert.False(g.VorjahrManuell);
        Assert.True(g.Status.VorjahrUeberschritten);
        Assert.Equal(D(2026, 1, 1), g.Status.Vorschlag!.AbDatum);
        Assert.Equal(0.8m, g.Warnschwelle);

        Ab.Auswertungen.SetzeVorjahresumsatz(2026, 20_000m, "laut Vorjahreserklärung");
        g = Ab.Auswertungen.Grenze(2026);
        Assert.True(g.VorjahrManuell);
        Assert.Equal(20_000m, g.Vorjahresumsatz);
        Assert.Equal(D(2026, 9, 15), g.Status.Vorschlag!.AbDatum);
        Assert.Equal(2, g.Status.Warnungen.Count);

        Assert.Equal(D(2026, 9, 15), Ab.Auswertungen.UebernehmeUmschaltung(2026, "Grenze überschritten"));
        Assert.Contains(Ab.Einstellungen.BesteuerungsartHistorie(), h => h.Ab == D(2026, 9, 15) && h.Art == Besteuerungsart.Regelbesteuerung);
        Assert.Throws<FachlicherFehler>(() => Ab.Auswertungen.UebernehmeUmschaltung(2025, "x"));            // 2025: kein Vorschlag
        Assert.Throws<PropTax.TaxEngine.Regelwerk.RegelwerkException>(() => Ab.Auswertungen.Grenze(2027)); // keine Grenzwerte gepflegt
    }

    [Fact]
    public void EUER_Zahlungsprinzip_Kurse_Kursdifferenz_Storno_Erstattung_USt()
    {
        Ab.Kurse.Setze(new KursDaten("USD", 2026, 5, 1.0800m, Kursquelle.Bmf));
        var c1 = Gebucht(Challenge("C1"));                                                                  // Zahlung 10.03.
        Gebucht(Challenge("C2", D(2026, 3, 20)) with { Zahlungsdatum = D(2026, 4, 5) });                    // USt März, Zahlung April
        Payout("P1", D(2026, 5, 15), 2000m);
        Ab.Belege.Storniere(c1, D(2026, 4, 2), "Fehlbuchung");
        var c3 = Gebucht(Challenge("C3", D(2026, 4, 8)));
        Gebucht(Challenge("R3", D(2026, 4, 20)) with { Typ = Belegtyp.Erstattung, BezugBelegId = c3 });
        Abo();
        Gebucht(Challenge("C9", D(2026, 3, 25)).Mit(D(2026, 6, 1)));

        var e = Ab.Auswertungen.Euer(2026);
        var c2 = e.Positionen.Single(p => p.Belegnummer == "C2");
        Assert.Equal((90.00m, 91.67m, -1.67m), (c2.BetragEur, c2.UstEur!.Value, c2.Kursdifferenz));
        Assert.StartsWith("BMF 04/2026", c2.Quelle);
        var storno = e.Positionen.Single(p => p.Belegnummer == "C1-S");                                       // Zahlung April → Aprilkurs
        Assert.Equal((-90.00m, -91.67m, 1.67m), (storno.BetragEur, storno.UstEur!.Value, storno.Kursdifferenz));
        Assert.Equal("Einnahme", e.Positionen.Single(p => p.Belegnummer == "R3").Art);
        Assert.Equal(1851.85m, e.Positionen.Single(p => p.Belegnummer == "P1").BetragEur);
        Assert.Contains(e.Hinweise, h => h.Contains("C9") && h.Contains("06/2026"));
        Assert.DoesNotContain(e.Positionen, p => p.Belegnummer == "C9");

        // USt Q1 (KU): §13b-Steuer aus C1, C2, C9 (Rechnung März) und Abo = 3 × 17,42 + 5,70 → Ausgabe zum 10.04.2026.
        // C9 fehlt nur in der EÜR (kein Juni-Kurs), umsatzsteuerlich gehört er in Q1.
        var ustQ1 = e.Positionen.Single(p => p.Belegnummer == "USt 2026-Q1");
        Assert.Equal((57.96m, "Ausgabe", D(2026, 4, 10)), (ustQ1.BetragEur, ustQ1.Art, ustQ1.Datum));
        Assert.Equal(e.Einnahmen - e.Ausgaben, e.Gewinn);
        Assert.Equal(e.Positionen.Sum(p => p.Kursdifferenz), e.Kursdifferenzen);
        Assert.Equal(0m, e.Positionen.Single(p => p.Belegnummer == "P1").Kursdifferenz);
        Assert.Contains(e.Hinweise, h => h.Contains("ohne Zeile der Anlage EÜR"));

        var kat = Ab.Stammdaten.EuerKategorien().Single(k => k.Code == "PROP_GEBUEHR");
        Ab.Stammdaten.SetzeEuerZeile(2026, kat.Id, "Zeile X (Nutzer)", "laut Steuerberater");
        Assert.Equal("Zeile X (Nutzer)", Ab.Auswertungen.Euer(2026).Zeilen.Single(z => z.KategorieId == kat.Id).AnlageZeile);
        Assert.Equal("Zeile X (Nutzer)", Ab.Stammdaten.EuerZeilen(2026).Single(z => z.KategorieId == kat.Id).Zeile);
        Ab.Stammdaten.SetzeEuerZeile(2026, kat.Id, null, "entfernt");
        Assert.Null(Ab.Stammdaten.EuerZeilen(2026).Single(z => z.KategorieId == kat.Id).Zeile);

        var nurOnline = Ab.Auswertungen.Euer(2026, Onlinehandel);
        Assert.Equal("Onlinehandel", nurOnline.Betrieb);
        Assert.Empty(nurOnline.Positionen);
    }

    [Fact]
    public void EUER_Bankkurs_und_unbezahlte_Belege()
    {
        Ab.Einstellungen.SetzeEinstellung("euer.kurs_quelle", "Bank", D(2026, 1, 1), "tatsächlicher Kurs");
        Gebucht(Challenge("C1") with { Bankkurs = 1.05m });
        Gebucht(Challenge("C2"));
        Gebucht(Challenge("C3") with { Zahlungsdatum = null });
        var e = Ab.Auswertungen.Euer(2026);
        Assert.Equal((94.29m, 2.62m), (e.Positionen.Single(p => p.Belegnummer == "C1").BetragEur, e.Positionen.Single(p => p.Belegnummer == "C1").Kursdifferenz));
        Assert.Contains(e.Hinweise, h => h.Contains("ohne Bankkurs"));
        Assert.Contains(e.Hinweise, h => h.Contains("ohne Zahlungsdatum"));
    }

    [Fact]
    public void EUER_USt_Zahlung_10_Tage_Regel()
    {
        Ab.Kurse.Setze(new KursDaten("USD", 2025, 12, 1.0500m, Kursquelle.Bmf));
        Gebucht(Challenge("C1", D(2025, 12, 15)));                              // Q4/2025, fällig Mo 12.01.2026 → 2026
        Assert.Contains(Ab.Auswertungen.Euer(2026).Positionen, p => p.Belegnummer == "USt 2025-Q4");
        Assert.DoesNotContain(Ab.Auswertungen.Euer(2025).Positionen, p => p.Belegnummer == "USt 2025-Q4");

        Ab.Einstellungen.SetzeEinstellung("ust.va_frist_tag", "5", D(2025, 1, 1), "Test: Fälligkeit 05.01.");
        Assert.Contains(Ab.Auswertungen.Euer(2025).Positionen, p => p.Belegnummer == "USt 2025-Q4" && p.Datum == D(2026, 1, 5));

        Ab.Einstellungen.SetzeEinstellung("euer.ust_zahlung", "FAELLIGKEIT", D(2025, 1, 1), "ohne 10-Tage-Regel");
        Assert.Contains(Ab.Auswertungen.Euer(2026).Positionen, p => p.Belegnummer == "USt 2025-Q4");
    }

    [Fact]
    public void Dashboard_je_Firma_und_Monat()
    {
        Ab.Kurse.Setze(new KursDaten("USD", 2026, 5, 1.0800m, Kursquelle.Bmf));
        var c1 = Gebucht(Challenge("C1"));
        Gebucht(Challenge("C2", D(2026, 3, 20), 149m));
        Gebucht(Challenge("R1", D(2026, 4, 5)) with { Typ = Belegtyp.Erstattung, BezugBelegId = c1 });
        Payout("P1", D(2026, 5, 15), 2000m);
        Abo();

        var d = Ab.Auswertungen.Dashboard(2026);
        var fn = Assert.Single(d.JeFirma);
        Assert.Equal("Funded Next", fn.Bezeichnung);
        Assert.Equal(137.96m, fn.Gebuehren);                 // 91,67 + 137,96 − 91,67
        Assert.Equal(1851.85m, fn.Payouts);
        Assert.Equal(26.21m, fn.UstLast);                     // 17,42 + 26,21 − 17,42
        Assert.Equal(1851.85m - 137.96m - 26.21m, fn.Netto);
        Assert.Equal((2, 1), (fn.AnzahlGebuehren, fn.AnzahlPayouts));
        Assert.Equal(12, d.JeMonat.Count);
        Assert.Equal(229.63m, d.JeMonat[2].Gebuehren);       // März
        Assert.Equal(-91.67m, d.JeMonat[3].Gebuehren);       // April (Erstattung)
        Assert.Equal("März", d.JeMonat[2].Bezeichnung);
        Assert.Equal(fn.Netto, d.Summe.Netto);
        Assert.Empty(Ab.Auswertungen.Dashboard(2026, IrischerAnbieter).JeFirma);
    }

    [Fact]
    public void Nachweis_nur_Prop_Firmen_mit_Hinweisen_und_Exporten()
    {
        var c1 = Gebucht(Challenge("C1"));
        Gebucht(Challenge("C2", D(2026, 3, 12), 149m));
        Abo();
        Ab.Belege.Storniere(c1, D(2026, 4, 2), "Fehlbuchung");
        var datei = Path.Combine(Ordner, "rechnung C1.pdf");
        File.WriteAllText(datei, "%PDF");
        Ab.Dokumente.Zuordnen(c1, Ab.Dokumente.Ablegen(datei).Id);
        Ab.Stammdaten.SetzeUnternehmen("Max Trader");
        Assert.Equal("Max Trader", Ab.Stammdaten.Unternehmen());

        var n = Ab.Auswertungen.Nachweis(2026);
        Assert.Equal(["C1", "C2", "C1-S"], n.Zeilen.Select(z => z.Belegnummer));
        Assert.Equal("Max Trader", n.Unternehmen);
        Assert.Equal(["rechnung C1.pdf"], n.Zeilen[0].Belegdateien);
        Assert.Equal("Storno zu C1", n.Zeilen[2].Hinweis);
        Assert.Equal("52/53", n.Zeilen[0].Kennzahlen);
        Assert.Equal(("AE", 1.08m, 17.42m), (n.Zeilen[0].Land, n.Zeilen[0].Kurs!.Value, n.Zeilen[0].Steuer13b));
        Assert.Equal(137.96m, n.SummeEur);

        var pdf = Path.Combine(Ordner, "n.pdf");
        var xlsx = Path.Combine(Ordner, "n.xlsx");
        var zip = Path.Combine(Ordner, "n.zip");
        Ab.Auswertungen.ExportiereNachweis(2026, pdf, ExportFormat.Pdf);
        Ab.Auswertungen.ExportiereNachweis(2026, xlsx, ExportFormat.Xlsx);
        Ab.Auswertungen.ExportiereNachweis(2026, zip, ExportFormat.Zip);
        Assert.True(new FileInfo(pdf).Length > 1000 && new FileInfo(xlsx).Length > 1000);
        using (var z = ZipFile.OpenRead(zip))
            Assert.Contains(z.Entries, x => x.FullName == "belege/C1_rechnung C1.pdf");
        Assert.Throws<FachlicherFehler>(() => Ab.Auswertungen.ExportiereNachweis(2026, Path.Combine(Ordner, "n.csv"), ExportFormat.Csv));
        Assert.Equal(3, Ab.Protokoll.Alle().Count(p => p.Aktion == "EXPORT"));
    }

    [Fact]
    public void Tabellenexporte_VA_und_EUER()
    {
        Gebucht(Challenge());
        var va = Ab.Auswertungen.UstVaTabelle(UstZeitraum.Quartal(2026, 1));
        Assert.Equal("Zahllast", va.Zeilen[^1][1]);
        var euer = Ab.Auswertungen.EuerTabelle(2026);
        Assert.Contains(euer.Zeilen, z => (string?)z[1] == "Gewinn/Verlust");
        var x = Path.Combine(Ordner, "va.xlsx");
        var c = Path.Combine(Ordner, "euer.csv");
        Ab.Auswertungen.ExportiereTabelle(va, x, ExportFormat.Xlsx);
        Ab.Auswertungen.ExportiereTabelle(euer, c, ExportFormat.Csv);
        Assert.Contains("Gewinn/Verlust", File.ReadAllText(c));
        Assert.Throws<FachlicherFehler>(() => Ab.Auswertungen.ExportiereTabelle(va, x, ExportFormat.Pdf));
    }
}

internal static class BelegDatenExt
{
    public static BelegDaten Mit(this BelegDaten d, DateOnly zahlung) => d with { Zahlungsdatum = zahlung };
}
