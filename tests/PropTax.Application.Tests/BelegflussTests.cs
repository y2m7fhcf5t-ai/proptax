using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.Application.Tests;

public class BelegflussTests : Basis
{
    [Fact]
    public void Erneutes_Oeffnen_spielt_keine_Startdaten_doppelt_ein()
    {
        var ab2 = Arbeitsbereich.Oeffne(Ordner, "Tester");
        Assert.Equal(3, ab2.SchemaVersion);
        Assert.Equal(2, ab2.Stammdaten.Betriebe().Count);
        Assert.Equal(5, ab2.Stammdaten.Partner().Count(p => p.IstPropFirma));
        Assert.True(File.Exists(Path.Combine(Ordner, Arbeitsbereich.DateinameDb)));
        Assert.True(Directory.Exists(ab2.BelegOrdner));
    }

    [Fact]
    public void Partner_ohne_Kategorie_nur_als_Entwurf()
    {
        var iq = Ab.Stammdaten.Partner().Single(p => p.Name == "IQ Capital");
        Assert.Null(iq.Kategorie);
        var id = Ab.Belege.ErfasseEntwurf(Challenge() with { PartnerId = iq.Id });
        var ex = Assert.Throws<FachlicherFehler>(() => Ab.Belege.Buche(id));
        Assert.Contains("keine Kategorie", ex.Message);
        Assert.Equal(BelegStatus.Entwurf, Ab.Belege.Laden(id).Status);

        Ab.Stammdaten.SpeicherePartner(iq with { LandIso = "us" });
        Assert.Equal(PartnerKategorie.Drittland, Ab.Stammdaten.Partner(iq.Id)!.Kategorie);
        Assert.Equal(17.42m, Ab.Belege.Buche(id).Zahllast.Betrag);
        Assert.Contains(Ab.Protokoll.Zu("partner", iq.Id.ToString()), p => p.Feld == "LandIso" && p.Neu == "US");
    }

    [Fact]
    public void Fall1_ueber_alle_Schichten_mit_Snapshot()
    {
        var id = Ab.Belege.ErfasseEntwurf(Challenge());
        var vorschau = Ab.Belege.Vorschau(id);
        Assert.Equal(BelegStatus.Entwurf, Ab.Belege.Laden(id).Status);
        var e = Ab.Belege.Buche(id);

        Assert.Equal(vorschau.Zahllast, e.Zahllast);
        var a = Ab.Belege.Laden(id);
        Assert.Equal(BelegStatus.Gebucht, a.Status);
        Assert.Equal(91.67m, a.Steuer!.Kz("52").Betrag);
        Assert.Equal(17.42m, a.Steuer.Kz("53").Betrag);
        Assert.Equal(UstZeitraum.Quartal(2026, 1), a.Steuer.Zeitraum);
        Assert.Equal(e.Begruendung, a.Steuer.Begruendung);
        Assert.Equal("Prop-Trading", a.BetriebName);

        var va = UstVaAggregation.Berechne(Ab.Belege.Steuerergebnisse(), UstZeitraum.Quartal(2026, 1));
        Assert.Equal(17.42m, va.Zahllast.Betrag);
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.Buche(id));
        Assert.True(Ab.Protokoll.PruefeKette());
    }

    [Fact]
    public void Vorschau_fuer_ungespeicherte_Daten()
    {
        var e = Ab.Belege.Vorschau(Challenge());
        Assert.Equal(17.42m, e.Steuer.Betrag);
        Assert.Empty(Ab.Belege.Liste());
    }

    [Fact]
    public void Fall3_EU_Abo_und_Fall4_Erstattung_im_Folgequartal()
    {
        var abo = Gebucht(new BelegDaten
        {
            Belegnummer = "A1", BetriebId = PropTrading, PartnerId = IrischerAnbieter, Typ = Belegtyp.AboSoftware, Richtung = Richtung.Eingang,
            Leistungsdatum = D(2026, 2, 1), Zahlungsdatum = D(2026, 2, 1), Betrag = new CurrencyAmount(30m, "EUR"),
        });
        Assert.Equal(5.70m, Ab.Belege.Laden(abo).Steuer!.Kz("47").Betrag);

        var c = Gebucht(Challenge());
        var r = Ab.Belege.ErfasseEntwurf(Challenge("R1", D(2026, 4, 5)) with { Typ = Belegtyp.Erstattung, BezugBelegId = c });
        var e = Ab.Belege.Buche(r);
        Assert.Equal(UstZeitraum.Quartal(2026, 2), e.Zeitraum);
        Assert.Equal(-17.42m, e.Steuer.Betrag);
        Assert.Equal(-91.67m, e.Kz("52").Betrag);
    }

    [Fact]
    public void Erstattung_ohne_Bezug_oder_auf_Entwurf_ist_Fehler()
    {
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("R1") with { Typ = Belegtyp.Erstattung }));
        var entwurf = Ab.Belege.ErfasseEntwurf(Challenge());
        var r = Ab.Belege.ErfasseEntwurf(Challenge("R1", D(2026, 4, 5)) with { Typ = Belegtyp.Erstattung, BezugBelegId = entwurf });
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.Buche(r));
    }

    [Fact]
    public void Fall7_Festschreibung_Storno_Korrektur_ueber_Datenbank()
    {
        var c1 = Gebucht(Challenge("C1"));
        var entwurf = Ab.Belege.ErfasseEntwurf(Challenge("C2"));
        Assert.Throws<FachlicherFehler>(() => Ab.Perioden.FestschreibenUst(UstZeitraum.Quartal(2026, 1), "VA Q1 abgegeben"));
        Ab.Belege.LoescheEntwurf(entwurf, "doppelt erfasst");
        Ab.Perioden.FestschreibenUst(UstZeitraum.Quartal(2026, 1), "VA Q1 abgegeben");
        Assert.True(Ab.Perioden.IstUstFestgeschrieben(UstZeitraum.Quartal(2026, 1)));

        // Änderung und Neubuchung im festgeschriebenen Quartal sind unzulässig.
        Assert.Throws<PeriodeFestgeschriebenException>(() => Ab.Belege.AendereGebucht(c1, Challenge("C1", usd: 89m), "Tippfehler"));
        var spaet = Ab.Belege.ErfasseEntwurf(Challenge("C3", D(2026, 3, 30)));
        Assert.Throws<PeriodeFestgeschriebenException>(() => Ab.Belege.Buche(spaet));
        Assert.Throws<PeriodeFestgeschriebenException>(() => Ab.Belege.Storniere(c1, D(2026, 3, 31), "x"));

        // Korrektur: Storno im April (Q2) + neuer Beleg.
        var (stornoId, neuId) = Ab.Belege.Korrigiere(c1, Challenge("C1-K", D(2026, 4, 2), 89m), D(2026, 4, 2), "Rabatt nachträglich");
        var storno = Ab.Belege.Laden(stornoId);
        Assert.Equal(c1, storno.StornoVonId);
        Assert.Equal(-99m, storno.Daten.Betrag.Betrag);
        Assert.Equal(-17.42m, storno.Steuer!.Steuer.Betrag);
        Assert.Equal(UstZeitraum.Quartal(2026, 2), storno.Steuer.Zeitraum);
        Assert.True(Ab.Belege.Laden(c1).IstStorniert);
        Assert.Equal(c1, Ab.Belege.Laden(neuId).KorrekturVonId);
        Assert.Equal(15.37m, Ab.Belege.Laden(neuId).Steuer!.Steuer.Betrag); // 89 / 1,10 = 80,91; × 19 % = 15,37 (Aprilkurs)

        Assert.Throws<FachlicherFehler>(() => Ab.Belege.Storniere(c1, D(2026, 4, 3), "nochmal"));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.Storniere(stornoId, D(2026, 4, 3), "Storno vom Storno"));
        var aktionen = Ab.Protokoll.Zu("beleg", c1.ToString()).Select(p => p.Aktion).ToList();
        Assert.Contains("STORNO", aktionen);
        Assert.Contains("KORREKTUR", aktionen);
        Assert.True(Ab.Protokoll.PruefeKette());
    }

    [Fact]
    public void Gebuchten_Beleg_in_offener_Periode_aendern()
    {
        var id = Gebucht(Challenge());
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.AendereGebucht(id, Challenge(usd: 89m), " "));
        var e = Ab.Belege.AendereGebucht(id, Challenge(usd: 108m), "Betrag laut Rechnung");
        Assert.Equal(100.00m, e.Bemessungsgrundlage.Betrag);
        Assert.Equal(19.00m, Ab.Belege.Laden(id).Steuer!.Steuer.Betrag);
        var p = Ab.Protokoll.Zu("beleg", id.ToString());
        Assert.Contains(p, x => x.Feld == "Betrag" && x.Alt == "99 USD" && x.Neu == "108 USD");
        Assert.Contains(p, x => x.Aktion == "NEUBERECHNUNG");
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.AendereEntwurf(id, Challenge()));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.LoescheEntwurf(id, "weg"));
    }

    [Fact]
    public void Entwurf_bearbeiten_und_Validierung()
    {
        var id = Ab.Belege.ErfasseEntwurf(Challenge());
        Ab.Belege.AendereEntwurf(id, Challenge() with { Notiz = "Account 12345" });
        Assert.Equal("Account 12345", Ab.Belege.Laden(id).Daten.Notiz);
        Assert.Contains(Ab.Protokoll.Zu("beleg", id.ToString()), p => p.Feld == "Notiz");

        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge()));                       // Nummer doppelt
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("X") with { Betrag = new CurrencyAmount(0m, "USD") }));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("X") with { Betrag = new CurrencyAmount(1m, "US") }));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("X") with { PartnerId = 999 }));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("X") with { BetriebId = 999 }));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge(" ")));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.ErfasseEntwurf(Challenge("X") with { AusgewieseneUst = Money.Euro(-1m) }));
        Assert.Throws<ArgumentException>(() => Ab.Belege.ErfasseEntwurf(Challenge("X", usd: 1.005m)));
        var zweiter = Ab.Belege.ErfasseEntwurf(Challenge("C2"));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.AendereEntwurf(zweiter, Challenge("C1")));
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.Laden(999));
    }

    [Fact]
    public void Dubletten_werden_markiert_aber_nicht_blockiert()
    {
        var a = Ab.Belege.ErfasseEntwurf(Challenge("C1"));
        var b = Ab.Belege.ErfasseEntwurf(Challenge("C2"));
        Assert.True(Ab.Belege.Laden(a).MoeglicheDublette);
        Assert.True(Ab.Belege.Laden(b).MoeglicheDublette);
        Assert.Equal(2, Ab.Belege.MoeglicheDubletten(Challenge("C3")).Count);
        Assert.Single(Ab.Belege.MoeglicheDubletten(Challenge("C1"), a));
    }

    [Fact]
    public void Sammelbuchung_meldet_Fehler_je_Beleg()
    {
        var ok = Ab.Belege.ErfasseEntwurf(Challenge("C1"));
        var ohneKurs = Ab.Belege.ErfasseEntwurf(Challenge("C2", D(2026, 8, 1)));
        var fehler = Ab.Belege.BucheAlle([ok, ohneKurs]);
        Assert.Equal(BelegStatus.Gebucht, Ab.Belege.Laden(ok).Status);
        Assert.Contains("USD", Assert.Single(fehler).Value);
    }

    [Fact]
    public void Mehrfachbearbeitung_EUER_Kategorie()
    {
        var kat = Ab.Stammdaten.EuerKategorien().Single(k => k.Code == "PROP_GEBUEHR").Id;
        var a = Gebucht(Challenge("C1"));
        var b = Gebucht(Challenge("C2", D(2026, 4, 2)));
        Ab.Perioden.FestschreibenUst(UstZeitraum.Quartal(2026, 1), "abgegeben");
        var fehler = Ab.Belege.SetzeEuerKategorie([a, b], kat, "Zuordnung");
        Assert.Contains("festgeschrieben", Assert.Single(fehler).Value);
        Assert.Equal(kat, Ab.Belege.Laden(b).Daten.EuerKategorieId);
        Assert.Null(Ab.Belege.Laden(a).Daten.EuerKategorieId);
    }

    [Fact]
    public void Neuberechnung_nach_Kursaenderung_ausser_manuell_und_festgeschrieben()
    {
        var a = Gebucht(Challenge("C1"));
        var m = Ab.Belege.ErfasseEntwurf(Challenge("C2", usd: 50m));
        Ab.Belege.Buche(m, new Uebersteuerung("Kurs laut Bankbeleg") { Kurs = 1.2500m });
        Assert.True(Ab.Belege.Laden(m).Steuer!.IstManuell);
        Assert.Contains(Ab.Protokoll.Zu("beleg", m.ToString()), p => p.Aktion == "UEBERSTEUERUNG" && p.Neu!.Contains("Kurs=1.25"));

        Ab.Kurse.Setze(new KursDaten("USD", 2026, 3, 1.1000m, Kursquelle.Bmf), "BMF-Korrektur");
        Assert.Equal(1, Ab.Belege.NeuBerechnen("Kurs März korrigiert"));
        Assert.Equal(90.00m, Ab.Belege.Laden(a).Steuer!.Bemessungsgrundlage.Betrag);
        Assert.Equal(40.00m, Ab.Belege.Laden(m).Steuer!.Bemessungsgrundlage.Betrag);
        Assert.Equal(0, Ab.Belege.NeuBerechnen("nochmal"));
    }

    [Fact]
    public void Liste_mit_Filtern_und_Belegnummernvorschlag()
    {
        Gebucht(Challenge("C1"));
        Ab.Belege.ErfasseEntwurf(Challenge("C2", D(2026, 4, 2)) with { Notiz = "Reset Konto 7" });
        Ab.Belege.ErfasseEntwurf(new BelegDaten
        {
            Belegnummer = "V1", BetriebId = Onlinehandel, PartnerId = Kunde, Typ = Belegtyp.Verkauf, Richtung = Richtung.Ausgang,
            Leistungsdatum = D(2026, 5, 1), Zahlungsdatum = D(2026, 5, 1), Betrag = new CurrencyAmount(50m, "EUR"),
        });
        Assert.Equal(3, Ab.Belege.Liste().Count);
        Assert.Single(Ab.Belege.Liste(new BelegFilter { Status = BelegStatus.Gebucht }));
        Assert.Single(Ab.Belege.Liste(new BelegFilter { BetriebId = Onlinehandel }));
        Assert.Equal(2, Ab.Belege.Liste(new BelegFilter { PartnerId = FundedNext }).Count);
        Assert.Single(Ab.Belege.Liste(new BelegFilter { Von = D(2026, 4, 1), Bis = D(2026, 4, 30) }));
        Assert.Single(Ab.Belege.Liste(new BelegFilter { Suchtext = "konto 7" }));
        Assert.Single(Ab.Belege.Liste(new BelegFilter { Suchtext = "endkunde" }));
        Assert.Equal("B-2026-00001", Ab.Belege.NaechsteBelegnummer(D(2026, 1, 1)));
    }

    [Fact]
    public void Vorlagen_und_Stammdaten()
    {
        var v = Ab.Stammdaten.SpeichereVorlage(new VorlageDaten
        {
            Name = "FN Challenge 99", BetriebId = PropTrading, PartnerId = FundedNext, Typ = Belegtyp.ChallengeGebuehr,
            Richtung = Richtung.Eingang, Betrag = new CurrencyAmount(99m, "USD"),
        });
        var d = Ab.Stammdaten.AusVorlage(v, D(2026, 3, 12), "C7");
        Assert.Equal(17.42m, Ab.Belege.Vorschau(d).Steuer.Betrag);
        Assert.Single(Ab.Stammdaten.Vorlagen());
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.AusVorlage(999, D(2026, 1, 1), "x"));
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeichereVorlage(new VorlageDaten { Name = " " }));

        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "funded next" }));
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "X", LandIso = "USA" }));
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "" }));
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Id = 999, Name = "Neu" }));
        var uk = Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "UK Firm", LandIso = "GB", Kategorie = PartnerKategorie.Drittland });
        Assert.Equal(PartnerKategorie.Drittland, Ab.Stammdaten.Partner(uk)!.Kategorie);
        Assert.Null(Ab.Stammdaten.Partner(9999));

        var bid = Ab.Stammdaten.SpeichereBetrieb(new BetriebDaten(0, "Beratung", "BR", true));
        Ab.Stammdaten.SpeichereBetrieb(new BetriebDaten(bid, "Beratung & Coaching", "BC", true));
        Assert.Contains(Ab.Stammdaten.Betriebe(), b => b.Name == "Beratung & Coaching");
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeichereBetrieb(new BetriebDaten(0, "", null, true)));
        Assert.Throws<FachlicherFehler>(() => Ab.Stammdaten.SpeichereBetrieb(new BetriebDaten(999, "X", null, true)));
        Assert.Equal(9, Ab.Stammdaten.EuerKategorien().Count);
    }

    [Fact]
    public void Einstellungen_Besteuerungsart_und_Payout()
    {
        Ab.Einstellungen.SetzeEinstellung(EinstellungenService.PayoutBehandlung, "STEUERBAR", D(2026, 7, 1), "Steuerberater-Empfehlung");
        Assert.Equal("NICHT_STEUERBAR", Ab.Einstellungen.Wert(EinstellungenService.PayoutBehandlung, D(2026, 6, 30)));
        Assert.Equal("STEUERBAR", Ab.Einstellungen.Wert(EinstellungenService.PayoutBehandlung, D(2026, 7, 1)));
        Assert.Throws<FachlicherFehler>(() => Ab.Einstellungen.SetzeEinstellung(EinstellungenService.PayoutBehandlung, "QUATSCH", D(2026, 1, 1), "x"));
        Assert.Throws<FachlicherFehler>(() => Ab.Einstellungen.SetzeEinstellung("a", "b", D(2026, 1, 1), ""));

        Ab.Einstellungen.SetzeBesteuerungsart(D(2026, 1, 1), Besteuerungsart.Regelbesteuerung, "Option nach §19 Abs. 3");
        Assert.Equal(2, Ab.Einstellungen.BesteuerungsartHistorie().Count);
        Assert.Equal(0m, Ab.Belege.Vorschau(Challenge()).Zahllast.Betrag);
        Assert.Contains(Ab.Protokoll.Alle(), p => p.Entitaet == "besteuerungsart" && p.Neu == "Regelbesteuerung");
    }

    [Fact]
    public void EUER_Festschreibung_und_Uebersicht()
    {
        Ab.Belege.ErfasseEntwurf(Challenge("C1", D(2025, 12, 1)));
        Assert.Throws<FachlicherFehler>(() => Ab.Perioden.FestschreibenEuer(2025, "Jahresabschluss"));
        Ab.Perioden.FestschreibenEuer(2024, "Jahresabschluss");
        Assert.Throws<FachlicherFehler>(() => Ab.Perioden.FestschreibenEuer(2024, " "));
        var p = Assert.Single(Ab.Perioden.Festgeschriebene());
        Assert.Equal(("EUER", "2024", true, "Tester"), (p.Art, p.Schluessel, p.Festgeschrieben, p.Von));
    }
}
