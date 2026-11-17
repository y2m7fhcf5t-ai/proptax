using Dapper;
using Microsoft.Data.Sqlite;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Persistence.Tests;

public sealed class TempDb : IDisposable
{
    public static readonly DateTimeOffset Jetzt = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    public TempDb()
    {
        Ordner = Path.Combine(Path.GetTempPath(), "proptax-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Ordner);
        Db = new Datenbank(Path.Combine(Ordner, "proptax.db"));
        Db.Migriere();
        using var s = Db.Sitzung();
        Startdaten.Einspielen(s, JsonRegelwerk.LadeStartwerte(), Jetzt, "Test");
        s.Commit();
    }

    public string Ordner { get; }
    public Datenbank Db { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Ordner, true);
    }
}

public class PersistenzTests : IDisposable
{
    private readonly TempDb _t = new();
    public void Dispose() => _t.Dispose();

    private static BelegZeile Beleg(string nr, long partner, string datum = "2026-03-10") => new()
    {
        Belegnummer = nr, BetriebId = 1, PartnerId = partner, Belegtyp = "ChallengeGebuehr", Richtung = "Eingang",
        Leistungsdatum = datum, Rechnungsdatum = datum, Zahlungsdatum = datum, BetragFwCent = 9900, Waehrung = "USD",
        EuerPeriode = datum[..4], Status = "Gebucht", DublettenSchluessel = nr, AngelegtAm = "2026-11-17T10:00:00Z",
    };

    private static BelegSteuerZeile Steuer(long belegId, string periode = "2026-Q1") => new()
    {
        BelegId = belegId, RegelCode = "X", Leistungsart = "SonstigeLeistung", Steuerbarkeit = "Steuerbar", Leistungsort = "Inland",
        Steuerschuldner = "Empfaenger13bAbs2Nr1", Besteuerungsart = "Kleinunternehmer", Entstehungsdatum = "2026-03-10",
        UstPeriode = periode, BmgCent = 9167, SteuerCent = 1742, KennzahlenJson = "[]", BegruendungJson = "[]",
        BerechnetAm = "2026-11-17T10:00:00Z", EngineVersion = "test",
    };

    private static void Wirft(Action a, string teil)
    {
        var ex = Assert.Throws<SqliteException>(a);
        Assert.Contains(teil, ex.Message);
    }

    [Fact]
    public void Migration_ist_idempotent_und_Seed_einmalig()
    {
        Assert.Equal(2, _t.Db.Migriere());
        using var s = _t.Db.Sitzung();
        Assert.False(Startdaten.Einspielen(s, JsonRegelwerk.LadeStartwerte(), TempDb.Jetzt, "Test"));
        Assert.Equal(["Onlinehandel", "Prop-Trading"], s.Stammdaten.Betriebe().Select(b => b.Name));
        var prop = s.Stammdaten.Partner().Where(p => p.IstPropFirma == 1).ToList();
        Assert.Equal(5, prop.Count);
        Assert.All(prop, p => Assert.Null(p.Kategorie));
        Assert.Equal(9, s.Stammdaten.EuerKategorien().Count);
        Assert.Equal("INITIALISIERUNG", Assert.Single(s.Protokoll.Alle()).Aktion);
    }

    [Fact]
    public void Regelwerk_aus_DB_entspricht_Startwerten_und_rechnet_Fall1()
    {
        using var s = _t.Db.Sitzung();
        s.Regelwerk.SpeichereKurs("usd", 2026, 3, 1.0800m, Kursquelle.Bmf, null);
        var rw = s.Regelwerk.Laden();
        var start = JsonRegelwerk.LadeStartwerte();
        Assert.Equal(start.RegelListe.Count, rw.RegelListe.Count);
        Assert.Equal(start.RegelListe[0].Belegtypen, rw.RegelListe[0].Belegtypen);
        Assert.Equal(start.Kennzahlen.Count, rw.Kennzahlen.Count);
        Assert.Equal(100_000m, rw.Grenzwert19(2026).GrenzeLaufendesJahr.Betrag);
        Assert.Equal("Quartal", rw.Einstellung("ust.voranmeldungszeitraum", new DateOnly(2026, 1, 1)));
        Assert.Equal(0.16m, rw.Satz("STANDARD", new DateOnly(2020, 8, 1)));

        var e = new Steuerermittlung(rw).Ermittle(new BelegEingabe
        {
            BelegRef = "C1", Typ = Belegtyp.ChallengeGebuehr, Richtung = Richtung.Eingang, PartnerKategorie = PartnerKategorie.Drittland,
            Rechnungsdatum = new(2026, 3, 10), Leistungsdatum = new(2026, 3, 10), Betrag = new CurrencyAmount(99m, "USD"),
        });
        Assert.Equal(17.42m, e.Zahllast.Betrag);
        Assert.Equal(91.67m, e.Kz("52").Betrag);
    }

    [Fact]
    public void Kurs_upsert_und_Einstellung_mit_Stichtag()
    {
        using var s = _t.Db.Sitzung();
        s.Regelwerk.SpeichereKurs("USD", 2026, 3, 1.08m, Kursquelle.Bmf, null);
        s.Regelwerk.SpeichereKurs("USD", 2026, 3, 1.09m, Kursquelle.Bmf, null);
        s.Regelwerk.SetzeEinstellung("ust.payout_behandlung", "STEUERBAR", new DateOnly(2026, 7, 1));
        s.Regelwerk.SetzeBesteuerungsart(new DateOnly(2026, 9, 15), Besteuerungsart.Regelbesteuerung, "Grenze");
        var rw = s.Regelwerk.Laden();
        Assert.Equal(1.09m, rw.Kurs("USD", 2026, 3, Kursquelle.Bmf));
        Assert.Single(s.Regelwerk.Kurse());
        Assert.Equal("NICHT_STEUERBAR", rw.Einstellung("ust.payout_behandlung", new DateOnly(2026, 6, 30)));
        Assert.Equal("STEUERBAR", rw.Einstellung("ust.payout_behandlung", new DateOnly(2026, 7, 1)));
        Assert.Equal(Besteuerungsart.Regelbesteuerung, rw.Besteuerungsart(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void Cent_Konvertierung()
    {
        Assert.Equal(9167, Werte.Cent(91.67m));
        Assert.Equal(-1742, Werte.Cent(Money.Euro(-17.42m)));
        Assert.Equal(91.67m, Werte.AusCent(9167));
        Assert.Throws<ArgumentException>(() => Werte.Cent(1.005m));
        Assert.Null(Werte.Cent((Money?)null));
        Assert.Null(Werte.MoneyAusCent((long?)null));
        Assert.Equal("1.0800", Werte.Dez(1.0800m));
        Assert.Null(Werte.DezOpt(null));
        Assert.Null(Werte.DatumOpt(null));
        Assert.Equal(new DateOnly(2026, 3, 1), Werte.Datum("2026-03-01"));
    }

    [Fact]
    public void Protokoll_ist_unveraenderlich_und_Manipulation_wird_erkannt()
    {
        using (var s = _t.Db.Sitzung())
        {
            s.Protokoll.Anhaengen(TempDb.Jetzt, "Test", "beleg", "1", "ANLAGE", null, null, "x", "a");
            s.Protokoll.Anhaengen(TempDb.Jetzt, "Test", "beleg", "1", "AENDERUNG", "Betrag", "1", "2", "b");
            s.Commit();
        }
        using var c = _t.Db.Oeffne();
        Wirft(() => c.Execute("UPDATE protokoll SET begruendung='x' WHERE nr=2"), "unveränderlich");
        Wirft(() => c.Execute("DELETE FROM protokoll WHERE nr=2"), "unveränderlich");

        using (var s = _t.Db.Sitzung())
        {
            Assert.True(s.Protokoll.PruefeKette());
            Assert.Equal(2, s.Protokoll.Zu("beleg", "1").Count);
        }

        // Angreifer mit Dateizugriff entfernt den Trigger und ändert einen Eintrag → Kette bricht.
        c.Execute("DROP TRIGGER protokoll_kein_update; UPDATE protokoll SET neu='3' WHERE nr=3");
        using (var s = _t.Db.Sitzung()) Assert.False(s.Protokoll.PruefeKette());
    }

    [Fact]
    public void Festgeschriebene_Periode_sperrt_Belege_und_Steuerergebnisse()
    {
        long id, offen;
        using (var s = _t.Db.Sitzung())
        {
            id = s.Belege.Einfuegen(Beleg("C1", 1));
            s.Belege.SpeichereSteuer(Steuer(id));
            offen = s.Belege.Einfuegen(Beleg("C2", 1, "2026-04-02"));
            s.Belege.SpeichereSteuer(Steuer(offen, "2026-Q2"));
            s.Perioden.Festschreiben(PeriodenRepository.Ust, "2026-Q1", "2026-11-17T10:00:00Z", "Test");
            s.Commit();
        }
        using var c = _t.Db.Oeffne();
        Wirft(() => c.Execute("UPDATE beleg SET notiz='x' WHERE id=@id", new { id }), "festgeschriebener Periode");
        Wirft(() => c.Execute("UPDATE beleg_steuer SET steuer_cent=0 WHERE beleg_id=@id", new { id }), "festgeschriebener Periode");
        Wirft(() => c.Execute("DELETE FROM beleg_steuer WHERE beleg_id=@id", new { id }), "festgeschriebener Periode");
        Wirft(() => c.Execute("DELETE FROM beleg WHERE id=@id", new { id }), "nicht gelöscht");
        Wirft(() => c.Execute("UPDATE periode SET status='OFFEN'"), "nicht aufgehoben");
        Wirft(() => c.Execute("DELETE FROM periode"), "nicht gelöscht");

        // Neues Steuerergebnis in festgeschriebene Periode verboten; offene Periode weiterhin änderbar.
        Wirft(() => c.Execute("UPDATE beleg_steuer SET ust_periode='2026-Q1' WHERE beleg_id=@offen", new { offen }) ,"");
        c.Execute("UPDATE beleg SET notiz='ok' WHERE id=@offen", new { offen });
        using var s2 = _t.Db.Sitzung();
        Assert.Throws<SqliteException>(() => s2.Belege.SpeichereSteuer(Steuer(offen, "2026-Q1")));
        Assert.True(s2.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, "2026-Q1"));
        Assert.False(s2.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, "2026-Q2"));
        Assert.Single(s2.Perioden.Alle());
    }

    [Fact]
    public void Festgeschriebenes_EUER_Jahr_sperrt_Neuanlage_und_Verschieben()
    {
        long id;
        using (var s = _t.Db.Sitzung())
        {
            s.Perioden.Festschreiben(PeriodenRepository.Euer, "2025", "2026-11-17T10:00:00Z", "Test");
            id = s.Belege.Einfuegen(Beleg("C1", 1).Als(z => z.Status = "Entwurf"));
            s.Commit();
        }
        using var c = _t.Db.Oeffne();
        Wirft(() => c.Execute("UPDATE beleg SET euer_periode='2025' WHERE id=@id", new { id }), "festgeschrieben");
        using var s2 = _t.Db.Sitzung();
        Assert.Throws<SqliteException>(() => s2.Belege.Einfuegen(Beleg("C9", 1, "2025-12-01")));
    }

    [Fact]
    public void Manuelle_Uebersteuerung_ohne_Begruendung_verletzt_CHECK()
    {
        using var s = _t.Db.Sitzung();
        var id = s.Belege.Einfuegen(Beleg("C1", 1));
        Assert.Throws<SqliteException>(() => s.Belege.SpeichereSteuer(Steuer(id).Mit(z => z.IstManuell = 1)));
        s.Belege.SpeichereSteuer(Steuer(id).Mit(z => { z.IstManuell = 1; z.ManuelleBegruendung = "Grund"; }));
        Assert.Equal("Grund", s.Belege.Steuer(id)!.ManuelleBegruendung);
    }

    [Fact]
    public void Entwurf_ist_loeschbar_gebuchter_nicht()
    {
        using var s = _t.Db.Sitzung();
        var e = s.Belege.Einfuegen(Beleg("E1", 1).Als(z => z.Status = "Entwurf"));
        s.Belege.Loeschen(e);
        Assert.Null(s.Belege.Laden(e));
        var g = s.Belege.Einfuegen(Beleg("G1", 1));
        Assert.Throws<SqliteException>(() => s.Belege.Loeschen(g));
    }

    [Fact]
    public void Repositories_Grundfunktionen()
    {
        using var s = _t.Db.Sitzung();
        var st = s.Stammdaten;
        var pid = st.SpeicherePartner(new PartnerZeile { Name = "Rise Works", LandIso = "US", Kategorie = "Drittland" });
        Assert.Equal(pid, st.PartnerNachName("rise works ")!.Id);
        var p = st.Partner(pid)!; p.Notiz = "n"; st.SpeicherePartner(p);
        Assert.Equal("n", st.Partner(pid)!.Notiz);
        var b = st.Betrieb(1)!; b.Kurzzeichen = "X"; st.SpeichereBetrieb(b);
        Assert.Equal("X", st.Betrieb(1)!.Kurzzeichen);
        Assert.True(st.UnternehmenId() > 0);

        var vid = st.SpeichereVorlage(new VorlageZeile { Name = "Challenge 99", PartnerId = pid, Belegtyp = "ChallengeGebuehr", Richtung = "Eingang", BetragFwCent = 9900, Waehrung = "USD" });
        var v = st.Vorlagen().Single(); v.BetragFwCent = 8900; st.SpeichereVorlage(v);
        Assert.Equal(8900, st.Vorlagen().Single(x => x.Id == vid).BetragFwCent);

        st.SpeichereProfil("Bank", "{}");
        st.SpeichereProfil("Bank", "{\"a\":1}");
        Assert.Equal("{\"a\":1}", Assert.Single(st.Profile()).ProfilJson);

        var bid = s.Belege.Einfuegen(Beleg("C1", pid));
        Assert.True(s.Belege.NummerVergeben("C1"));
        Assert.Equal(bid, s.Belege.NachNummer("C1")!.Id);
        Assert.Single(s.Belege.Dubletten("C1"));
        Assert.Empty(s.Belege.Dubletten("C1", bid));
        Assert.Single(s.Belege.NachStatus("Gebucht"));
        Assert.Single(s.Belege.Alle());
        Assert.Null(s.Belege.StornoZu(bid));

        var did = s.Dokumente.Einfuegen(new DokumentZeile { Sha256 = "ab", Dateiname = "r.pdf", RelativerPfad = "belege/ab/ab.pdf", Groesse = 1, Mime = "application/pdf", ImportiertAm = "x" });
        Assert.Single(s.Dokumente.Unzugeordnet());
        Assert.True(s.Dokumente.Verknuepfen(bid, did));
        Assert.False(s.Dokumente.Verknuepfen(bid, did));
        Assert.Empty(s.Dokumente.Unzugeordnet());
        Assert.Equal(did, s.Dokumente.ZuBeleg(bid).Single().Id);
        Assert.Equal(did, s.Dokumente.NachSha("ab")!.Id);
        Assert.NotNull(s.Dokumente.Laden(did));
        Assert.Single(s.Dokumente.Alle());

        var lid = s.Importe.Einfuegen(new ImportLaufZeile { Art = "BELEGE", Datei = "a.csv", DateiSha256 = "ff", Zeitpunkt = "x" });
        s.Importe.Abschliessen(lid, 3, 1, 0);
        Assert.Equal(3, s.Importe.NachDateiHash("BELEGE", "ff").Single().AnzahlOk);
        Assert.Single(s.Importe.Alle());
    }
}

internal static class ZeilenExt
{
    public static BelegSteuerZeile Mit(this BelegSteuerZeile z, Action<BelegSteuerZeile> a) { a(z); return z; }
}

internal static class BelegZeileExt
{
    public static BelegZeile Als(this BelegZeile z, Action<BelegZeile> a) { a(z); return z; }
}
