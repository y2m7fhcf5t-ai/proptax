using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PropTax.Persistence;

namespace PropTax.Application;


public sealed record Manifest(
    string Programm,
    string Version,
    DateTimeOffset Erstellt,
    int SchemaVersion,
    bool Verschluesselt,
    int AnzahlBelege,
    int AnzahlDokumente,
    Dictionary<string, string> Dateien);

public sealed record SicherungInfo(string Pfad, Manifest Manifest, bool Automatisch);

public sealed record PruefErgebnis(bool Ok, Manifest? Manifest, IReadOnlyList<string> Fehler);

/// <summary>
/// Ein-Klick-Sicherung und Wiederherstellung des Datenordners als ZIP (DB per Online-Backup-API, Belege, Manifest mit SHA-256).
/// </summary>
public sealed class SicherungService(Arbeitsbereich ab)
{
    public const string ManifestName = "manifest.json";
    private const string Auto = "auto_";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Erstellt eine Sicherung (Standard: backup/PropTax_&lt;Zeit&gt;.zip) und liefert den Pfad.</summary>
    public string Sichern(string? ziel = null, bool automatisch = false)
    {
        Directory.CreateDirectory(ab.SicherungsOrdner);
        var zeit = ab.Jetzt.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        ziel ??= NeuerName(automatisch ? $"{Auto}{zeit}" : $"PropTax_{zeit}");
        Protokolliere("SICHERUNG", Path.GetFileName(ziel), automatisch ? "automatisch beim Beenden" : "manuell");

        var tmp = Path.Combine(Path.GetTempPath(), "proptax-sich-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            ab.Db.SichernNach(tmp);
            int belege, dokumente;
            using (var s = ab.Sitzung())
            {
                belege = s.Belege.Alle().Count;
                dokumente = s.Dokumente.Alle().Count;
            }
            var dateien = new Dictionary<string, string>(StringComparer.Ordinal) { [Arbeitsbereich.DateinameDb] = Sha(tmp) };
            var belegDateien = Directory.Exists(ab.BelegOrdner)
                ? Directory.EnumerateFiles(ab.BelegOrdner, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList()
                : [];
            foreach (var f in belegDateien) dateien[Relativ(f)] = Sha(f);
            var manifest = new Manifest("PropTax", Arbeitsbereich.Version, ab.Jetzt, ab.SchemaVersion, ab.Db.MitPasswort, belege, dokumente, dateien);

            var teil = ziel + ".part";
            using (var zip = ZipFile.Open(teil, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(tmp, Arbeitsbereich.DateinameDb, CompressionLevel.Optimal);
                foreach (var f in belegDateien) zip.CreateEntryFromFile(f, Relativ(f), CompressionLevel.Optimal);
                using var m = zip.CreateEntry(ManifestName).Open();
                JsonSerializer.Serialize(m, manifest, Json);
            }
            File.Move(teil, ziel, overwrite: true);
        }
        finally
        {
            Datenbank.Freigeben();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        return ziel;
    }

    /// <summary>Automatische Sicherung (beim Beenden) inkl. Aufräumen: behält die letzten <c>backup.anzahl</c> automatischen Stände.</summary>
    public string Automatisch()
    {
        var pfad = Sichern(automatisch: true);
        var behalten = int.Parse(ab.Einstellungen.Wert("backup.anzahl", DateOnly.FromDateTime(ab.Jetzt.LocalDateTime)), CultureInfo.InvariantCulture);
        foreach (var alt in Directory.GetFiles(ab.SicherungsOrdner, Auto + "*.zip").Order(StringComparer.Ordinal).Reverse().Skip(behalten))
            File.Delete(alt);
        return pfad;
    }

    public IReadOnlyList<SicherungInfo> Liste()
    {
        if (!Directory.Exists(ab.SicherungsOrdner)) return [];
        return Directory.GetFiles(ab.SicherungsOrdner, "*.zip").Order(StringComparer.Ordinal).Reverse()
            .Select(p => (Pfad: p, M: LiesManifest(p)))
            .Where(x => x.M is not null)
            .Select(x => new SicherungInfo(x.Pfad, x.M!, Path.GetFileName(x.Pfad).StartsWith(Auto, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>Löscht Sicherungen mit unverschlüsselter Datenbank (z. B. nach Aktivierung der Verschlüsselung).</summary>
    public int LoescheUnverschluesselte(string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        var alte = Liste().Where(x => !x.Manifest.Verschluesselt).ToList();
        foreach (var x in alte) File.Delete(x.Pfad);
        if (alte.Count > 0) Protokolliere("SICHERUNG_GELOESCHT", $"{alte.Count} Datei(en)", begruendung);
        return alte.Count;
    }

    /// <summary>Prüft Manifest, Hashes und Schema-Version einer Sicherung.</summary>
    public PruefErgebnis Pruefe(string zipPfad)
    {
        var fehler = new List<string>();
        Manifest? manifest;
        try
        {
            using var zip = ZipFile.OpenRead(zipPfad);
            manifest = LiesManifest(zip);
            if (manifest is null) return new PruefErgebnis(false, null, ["Keine PropTax-Sicherung (manifest.json fehlt)."]);
            var eintraege = zip.Entries.Where(e => e.FullName != ManifestName && !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName);
            foreach (var (pfad, sha) in manifest.Dateien)
            {
                if (!eintraege.TryGetValue(pfad, out var e)) { fehler.Add($"Fehlt: {pfad}"); continue; }
                using var st = e.Open();
                if (Convert.ToHexStringLower(SHA256.HashData(st)) != sha) fehler.Add($"Prüfsumme falsch: {pfad}");
            }
            fehler.AddRange(eintraege.Keys.Where(k => !manifest.Dateien.ContainsKey(k)).Select(k => $"Nicht im Manifest: {k}"));
            if (manifest.SchemaVersion > Arbeitsbereich.UnterstuetzteSchemaVersion)
                fehler.Add($"Sicherung stammt von einer neueren Programmversion (Schema {manifest.SchemaVersion} > {Arbeitsbereich.UnterstuetzteSchemaVersion}).");
        }
        catch (InvalidDataException) { return new PruefErgebnis(false, null, ["Datei ist kein gültiges ZIP-Archiv."]); }
        return new PruefErgebnis(fehler.Count == 0, manifest, fehler);
    }

    /// <summary>
    /// Stellt eine Sicherung wieder her: Prüfung → (bei Verschlüsselung) Passwortprüfung → Sicherheitskopie des aktuellen
    /// Stands → Ersetzen von DB und Belegen → Migration → Kettenprüfung → Protokolleintrag.
    /// Liefert den Pfad der Sicherheitskopie. Danach muss die Anwendung neu gestartet werden.
    /// </summary>
    public string Wiederherstellen(string zipPfad, string? passwort, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        var pruefung = Pruefe(zipPfad);
        if (!pruefung.Ok) throw new FachlicherFehler("Sicherung ungültig: " + string.Join("; ", pruefung.Fehler.Take(3)));

        var tmp = Path.Combine(Path.GetTempPath(), "proptax-wh-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipPfad, tmp);
        try
        {
            var neueDb = new Datenbank(Path.Combine(tmp, Arbeitsbereich.DateinameDb), pruefung.Manifest!.Verschluesselt ? passwort : null);
            if (!neueDb.Lesbar())
                throw new PasswortErforderlichException("Die Sicherung ist verschlüsselt – Passwort fehlt oder ist falsch.");
            Datenbank.Freigeben();

            var sicherheitskopie = Sichern(NeuerName($"vor_wiederherstellung_{ab.Jetzt.ToLocalTime():yyyyMMdd_HHmmss}"));
            Datenbank.Freigeben();

            File.Copy(Path.Combine(tmp, Arbeitsbereich.DateinameDb), ab.DbPfad, overwrite: true);
            foreach (var w in new[] { "-wal", "-shm", "-journal" }) if (File.Exists(ab.DbPfad + w)) File.Delete(ab.DbPfad + w);
            if (Directory.Exists(ab.BelegOrdner)) Directory.Delete(ab.BelegOrdner, true);
            var belegQuelle = Path.Combine(tmp, "belege");
            if (Directory.Exists(belegQuelle)) Directory.Move(belegQuelle, ab.BelegOrdner);
            else Directory.CreateDirectory(ab.BelegOrdner);

            ab.Db = new Datenbank(ab.DbPfad, pruefung.Manifest.Verschluesselt ? passwort : null);
            ab.Db.Migriere(ab.Zeit);
            using (var s = ab.Sitzung())
            {
                if (!s.Protokoll.PruefeKette()) throw new FachlicherFehler("Wiederhergestellt, aber die Protokollkette ist verletzt – bitte prüfen!");
                s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "datenbank", "1", "WIEDERHERSTELLUNG", null,
                    Path.GetFileName(sicherheitskopie), $"{Path.GetFileName(zipPfad)} (Stand {pruefung.Manifest.Erstellt:dd.MM.yyyy HH:mm})", begruendung);
                s.Commit();
            }
            Datenbank.Freigeben();
            return sicherheitskopie;
        }
        finally
        {
            Datenbank.Freigeben();
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    private string NeuerName(string basis)
    {
        var pfad = Path.Combine(ab.SicherungsOrdner, basis + ".zip");
        for (var n = 2; File.Exists(pfad); n++) pfad = Path.Combine(ab.SicherungsOrdner, $"{basis}_{n}.zip");
        return pfad;
    }

    private string Relativ(string datei) => Path.GetRelativePath(ab.Datenordner, datei).Replace('\\', '/');

    private static string Sha(string datei)
    {
        using var f = File.OpenRead(datei);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    private static Manifest? LiesManifest(string zipPfad)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPfad);
            return LiesManifest(zip);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException) { return null; }
    }

    private static Manifest? LiesManifest(ZipArchive zip)
    {
        var e = zip.GetEntry(ManifestName);
        if (e is null) return null;
        using var s = e.Open();
        return JsonSerializer.Deserialize<Manifest>(s);
    }

    private void Protokolliere(string aktion, string id, string begruendung)
    {
        using var s = ab.Sitzung();
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "sicherung", id, aktion, null, null, null, begruendung);
        s.Commit();
    }
}

/// <summary>Verschlüsselung der Datenbank (SQLite3 Multiple Ciphers). Das Passwort wird nie gespeichert oder protokolliert.</summary>
public sealed class SicherheitService(Arbeitsbereich ab)
{
    public const int MindestLaenge = 10;

    public bool IstVerschluesselt => ab.IstVerschluesselt;

    /// <summary>Verschlüsselt die Datenbank und legt sofort eine verschlüsselte Sicherung an (Pfad als Rückgabe).</summary>
    public string Aktivieren(string passwort, string wiederholung)
    {
        if (IstVerschluesselt) throw new FachlicherFehler("Die Datenbank ist bereits verschlüsselt.");
        Pruefe(passwort, wiederholung);
        Protokolliere("VERSCHLUESSELUNG_AKTIVIERT");
        ab.Db = ab.Db.Umschluesseln(passwort);
        return ab.Sicherungen.Sichern();
    }

    public void Aendern(string altesPasswort, string neu, string wiederholung)
    {
        if (!IstVerschluesselt) throw new FachlicherFehler("Die Datenbank ist nicht verschlüsselt.");
        PruefeAlt(altesPasswort);
        Pruefe(neu, wiederholung);
        ab.Db = ab.Db.Umschluesseln(neu);
        Protokolliere("PASSWORT_GEAENDERT");
    }

    public void Entfernen(string altesPasswort, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        if (!IstVerschluesselt) throw new FachlicherFehler("Die Datenbank ist nicht verschlüsselt.");
        PruefeAlt(altesPasswort);
        Protokolliere("VERSCHLUESSELUNG_ENTFERNT", begruendung);
        ab.Db = ab.Db.Umschluesseln(null);
    }

    private void PruefeAlt(string alt)
    {
        if (!new Datenbank(ab.DbPfad, alt).Lesbar()) throw new FachlicherFehler("Das bisherige Passwort ist falsch.");
        Datenbank.Freigeben();
    }

    private static void Pruefe(string passwort, string wiederholung)
    {
        if (string.IsNullOrEmpty(passwort) || passwort.Length < MindestLaenge)
            throw new FachlicherFehler($"Das Passwort muss mindestens {MindestLaenge} Zeichen lang sein.");
        if (passwort != wiederholung) throw new FachlicherFehler("Passwort und Wiederholung stimmen nicht überein.");
    }

    private void Protokolliere(string aktion, string begruendung = "Datenschutz")
    {
        using var s = ab.Sitzung();
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "datenbank", "1", aktion, null, null, null, begruendung);
        s.Commit();
    }
}
