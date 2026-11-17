using System.IO.Compression;
using System.Text.Json;
using PropTax.Domain;

namespace PropTax.Application.Tests;

public class SicherungTests : Basis
{
    private string BelegMitDokument()
    {
        var id = Gebucht(Challenge());
        var pdf = Path.Combine(Ordner, "rechnung.pdf");
        File.WriteAllText(pdf, "%PDF Rechnung");
        Ab.Dokumente.Zuordnen(id, Ab.Dokumente.Ablegen(pdf).Id);
        return pdf;
    }

    [Fact]
    public void Sichern_mit_Manifest_Hashes_und_Pruefung()
    {
        BelegMitDokument();
        var zip = Ab.Sicherungen.Sichern();
        Assert.StartsWith(Path.Combine(Ordner, "backup", "PropTax_"), zip);

        var p = Ab.Sicherungen.Pruefe(zip);
        Assert.True(p.Ok, string.Join("; ", p.Fehler));
        Assert.Equal((1, 1, 3, false), (p.Manifest!.AnzahlBelege, p.Manifest.AnzahlDokumente, p.Manifest.SchemaVersion, p.Manifest.Verschluesselt));
        Assert.Equal(2, p.Manifest.Dateien.Count);
        Assert.Contains(p.Manifest.Dateien.Keys, k => k.StartsWith("belege/", StringComparison.Ordinal));
        Assert.Single(Ab.Sicherungen.Liste());
        Assert.Contains(Ab.Protokoll.Alle(), e => e.Aktion == "SICHERUNG");

        // zweite Sicherung in derselben Sekunde bekommt einen eigenen Namen
        Assert.NotEqual(zip, Ab.Sicherungen.Sichern());
    }

    [Fact]
    public void Pruefung_erkennt_Manipulation_fremde_Dateien_und_neuere_Schemata()
    {
        BelegMitDokument();
        var zip = Ab.Sicherungen.Sichern();
        var kopie = Path.Combine(Ordner, "manipuliert.zip");
        File.Copy(zip, kopie);
        using (var z = ZipFile.Open(kopie, ZipArchiveMode.Update))
        {
            var beleg = z.Entries.First(e => e.FullName.StartsWith("belege/", StringComparison.Ordinal));
            var name = beleg.FullName;
            beleg.Delete();
            using (var w = new StreamWriter(z.CreateEntry(name).Open())) w.Write("gefälscht");
            using (var w = new StreamWriter(z.CreateEntry("extra.txt").Open())) w.Write("x");
        }
        var p = Ab.Sicherungen.Pruefe(kopie);
        Assert.False(p.Ok);
        Assert.Contains(p.Fehler, f => f.StartsWith("Prüfsumme falsch"));
        Assert.Contains(p.Fehler, f => f.Contains("extra.txt"));

        var neuer = Path.Combine(Ordner, "neuer.zip");
        File.Copy(zip, neuer);
        using (var z = ZipFile.Open(neuer, ZipArchiveMode.Update))
        {
            var e = z.GetEntry(SicherungService.ManifestName)!;
            Manifest m;
            using (var s = e.Open()) m = JsonSerializer.Deserialize<Manifest>(s)!;
            e.Delete();
            using var w = z.CreateEntry(SicherungService.ManifestName).Open();
            JsonSerializer.Serialize(w, m with { SchemaVersion = 99 });
        }
        Assert.Contains(Ab.Sicherungen.Pruefe(neuer).Fehler, f => f.Contains("neueren Programmversion"));

        var kaputt = Path.Combine(Ordner, "kaputt.zip");
        File.WriteAllText(kaputt, "kein zip");
        Assert.False(Ab.Sicherungen.Pruefe(kaputt).Ok);
        var leer = Path.Combine(Ordner, "leer.zip");
        using (ZipFile.Open(leer, ZipArchiveMode.Create)) { }
        Assert.Contains("manifest.json fehlt", Ab.Sicherungen.Pruefe(leer).Fehler[0]);
        Assert.Throws<FachlicherFehler>(() => Ab.Sicherungen.Wiederherstellen(kopie, null, "Test"));
    }

    [Fact]
    public void Automatische_Sicherungen_werden_auf_Anzahl_begrenzt()
    {
        Ab.Einstellungen.SetzeEinstellung("backup.anzahl", "3", D(2000, 1, 1), "Test");
        var manuell = Ab.Sicherungen.Sichern();
        for (var i = 0; i < 5; i++) Ab.Sicherungen.Automatisch();
        var liste = Ab.Sicherungen.Liste();
        Assert.Equal(3, liste.Count(x => x.Automatisch));
        Assert.Contains(liste, x => x.Pfad == manuell);
    }

    [Fact]
    public void Wiederherstellen_setzt_Daten_und_Belege_zurueck_mit_Sicherheitskopie()
    {
        var pdf = BelegMitDokument();
        var zip = Ab.Sicherungen.Sichern();
        Gebucht(Challenge("C2"));
        var belegDatei = Directory.GetFiles(Ab.BelegOrdner, "*", SearchOption.AllDirectories).Single();
        File.Delete(belegDatei);

        var kopie = Ab.Sicherungen.Wiederherstellen(zip, null, "Fehlbedienung rückgängig");
        Assert.Contains("vor_wiederherstellung_", kopie);
        Assert.True(File.Exists(belegDatei));

        var neu = Arbeitsbereich.Oeffne(Ordner, "Tester");
        Assert.Equal(["C1"], neu.Belege.Liste().Select(b => b.Daten.Belegnummer));
        Assert.Empty(neu.Dokumente.PruefeIntegritaet());
        Assert.True(neu.Protokoll.PruefeKette());
        Assert.Equal("WIEDERHERSTELLUNG", neu.Protokoll.Alle()[^1].Aktion);
        Assert.Throws<FachlicherFehler>(() => neu.Sicherungen.Wiederherstellen(zip, null, " "));

        // Sicherheitskopie enthält den Stand vor der Wiederherstellung (2 Belege)
        Assert.Equal(2, neu.Sicherungen.Pruefe(kopie).Manifest!.AnzahlBelege);
        _ = pdf;
    }

    [Fact]
    public void Verschluesselung_aktivieren_oeffnen_aendern_entfernen()
    {
        Gebucht(Challenge());
        Ab.Sicherungen.Sichern();                                              // alte, unverschlüsselte Sicherung
        Assert.Throws<FachlicherFehler>(() => Ab.Sicherheit.Aktivieren("kurz", "kurz"));
        Assert.Throws<FachlicherFehler>(() => Ab.Sicherheit.Aktivieren("lang-genug-1", "anders-12345"));

        var sicherung = Ab.Sicherheit.Aktivieren("lang-genug-1", "lang-genug-1");
        Assert.True(Ab.Sicherheit.IstVerschluesselt);
        Assert.True(Ab.Sicherungen.Pruefe(sicherung).Manifest!.Verschluesselt);
        Assert.Single(Ab.Belege.Liste());                                      // weiter nutzbar im selben Arbeitsbereich
        Assert.Throws<FachlicherFehler>(() => Ab.Sicherheit.Aktivieren("lang-genug-1", "lang-genug-1"));

        Assert.Equal("Die Datenbank ist verschlüsselt. Bitte Passwort eingeben.",
            Assert.Throws<PasswortErforderlichException>(() => Arbeitsbereich.Oeffne(Ordner, "Tester")).Message);
        Assert.Throws<PasswortErforderlichException>(() => Arbeitsbereich.Oeffne(Ordner, "Tester", passwort: "falsch"));
        var geoeffnet = Arbeitsbereich.Oeffne(Ordner, "Tester", passwort: "lang-genug-1");
        Assert.Single(geoeffnet.Belege.Liste());

        Assert.Equal(1, geoeffnet.Sicherungen.LoescheUnverschluesselte("nach Verschlüsselung"));
        Assert.All(geoeffnet.Sicherungen.Liste(), s => Assert.True(s.Manifest.Verschluesselt));

        Assert.Throws<FachlicherFehler>(() => geoeffnet.Sicherheit.Aendern("falsch", "neues-passwort-2", "neues-passwort-2"));
        geoeffnet.Sicherheit.Aendern("lang-genug-1", "neues-passwort-2", "neues-passwort-2");
        Assert.Throws<PasswortErforderlichException>(() => Arbeitsbereich.Oeffne(Ordner, "Tester", passwort: "lang-genug-1"));
        var mitNeu = Arbeitsbereich.Oeffne(Ordner, "Tester", passwort: "neues-passwort-2");

        Assert.Throws<FachlicherFehler>(() => mitNeu.Sicherheit.Entfernen("neues-passwort-2", ""));
        mitNeu.Sicherheit.Entfernen("neues-passwort-2", "Rechner mit BitLocker");
        Assert.False(mitNeu.Sicherheit.IstVerschluesselt);
        Assert.Throws<FachlicherFehler>(() => mitNeu.Sicherheit.Entfernen("x", "y"));
        Assert.Throws<FachlicherFehler>(() => mitNeu.Sicherheit.Aendern("x", "y", "y"));
        var aktionen = Arbeitsbereich.Oeffne(Ordner, "Tester").Protokoll.Alle().Select(e => e.Aktion).ToList();
        Assert.Contains("VERSCHLUESSELUNG_AKTIVIERT", aktionen);
        Assert.Contains("PASSWORT_GEAENDERT", aktionen);
        Assert.Contains("VERSCHLUESSELUNG_ENTFERNT", aktionen);
        Assert.DoesNotContain(Arbeitsbereich.Oeffne(Ordner, "Tester").Protokoll.Alle(), e => (e.Neu ?? "").Contains("genug") || e.Begruendung.Contains("genug"));
    }

    [Fact]
    public void Verschluesselte_Sicherung_wiederherstellen_nur_mit_Passwort()
    {
        Gebucht(Challenge());
        var zip = Ab.Sicherheit.Aktivieren("lang-genug-1", "lang-genug-1");
        Gebucht(Challenge("C2"));
        var ex = Assert.Throws<PasswortErforderlichException>(() => Ab.Sicherungen.Wiederherstellen(zip, "falsch", "Test"));
        Assert.Contains("Passwort", ex.Message);
        Assert.Equal(2, Ab.Belege.Liste().Count);                               // nichts ersetzt
        Ab.Sicherungen.Wiederherstellen(zip, "lang-genug-1", "Test");
        Assert.Single(Arbeitsbereich.Oeffne(Ordner, "Tester", passwort: "lang-genug-1").Belege.Liste());
    }
}
