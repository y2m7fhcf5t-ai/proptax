using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PropTax.Persistence;

namespace PropTax.Application;

public sealed record DokumentInfo(long Id, string Sha256, string Dateiname, string RelativerPfad, long Groesse, string Mime, bool Neu);

public sealed record Zuordnungsvorschlag(DokumentInfo Dokument, long BelegId, string Belegnummer, int Punkte, string Grund);

/// <summary>Belegablage im Datenordner (<c>belege/xx/sha256.ext</c>), inhaltsadressiert und dublettenfrei.</summary>
public sealed partial class DokumentService(Arbeitsbereich ab)
{
    private static readonly Dictionary<string, string> Mimes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".xml"] = "application/xml",
    };

    public DokumentInfo Ablegen(string quellpfad)
    {
        var ext = Path.GetExtension(quellpfad).ToLowerInvariant();
        if (!Mimes.TryGetValue(ext, out var mime)) throw new FachlicherFehler($"Dateityp {ext} wird nicht unterstützt (PDF, PNG, JPG, XML).");
        var sha = Hash(quellpfad);
        using var s = ab.Sitzung();
        if (s.Dokumente.NachSha(sha) is { } vorhanden) return Info(vorhanden, false);

        var relativ = Path.Combine("belege", sha[..2], sha + ext);
        var ziel = Path.Combine(ab.Datenordner, relativ);
        Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
        File.Copy(quellpfad, ziel, overwrite: true);
        var zeile = new DokumentZeile
        {
            Sha256 = sha, Dateiname = Path.GetFileName(quellpfad), RelativerPfad = relativ.Replace('\\', '/'),
            Groesse = new FileInfo(ziel).Length, Mime = mime, ImportiertAm = Werte.Zeit(ab.Jetzt),
        };
        zeile.Id = s.Dokumente.Einfuegen(zeile);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "dokument", zeile.Id.ToString(CultureInfo.InvariantCulture), "ANLAGE", null, null,
            $"{zeile.Dateiname} | {sha}", "Belegablage");
        s.Commit();
        return Info(zeile, true);
    }

    public IReadOnlyList<DokumentInfo> ImportiereOrdner(string ordner, bool rekursiv = false) =>
        Directory.EnumerateFiles(ordner, "*", rekursiv ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => Mimes.ContainsKey(Path.GetExtension(f)))
            .Order(StringComparer.Ordinal)
            .Select(Ablegen)
            .ToList();

    public IReadOnlyList<DokumentInfo> Unzugeordnet()
    {
        using var s = ab.Sitzung();
        return s.Dokumente.Unzugeordnet().Select(d => Info(d, false)).ToList();
    }

    public IReadOnlyList<DokumentInfo> ZuBeleg(long belegId)
    {
        using var s = ab.Sitzung();
        return s.Dokumente.ZuBeleg(belegId).Select(d => Info(d, false)).ToList();
    }

    public string AbsoluterPfad(DokumentInfo d) => Path.Combine(ab.Datenordner, d.RelativerPfad);

    public void Zuordnen(long belegId, long dokumentId)
    {
        using var s = ab.Sitzung();
        var b = s.Belege.Laden(belegId) ?? throw new FachlicherFehler($"Beleg {belegId} nicht gefunden.");
        var d = s.Dokumente.Laden(dokumentId) ?? throw new FachlicherFehler($"Dokument {dokumentId} nicht gefunden.");
        if (s.Dokumente.Verknuepfen(belegId, dokumentId))
            s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "beleg", belegId.ToString(CultureInfo.InvariantCulture), "DOKUMENT", null, null,
                $"{d.Dateiname} | {d.Sha256}", "Zuordnung zu " + b.Belegnummer);
        s.Commit();
    }

    /// <summary>
    /// Vorschläge für nicht zugeordnete Dokumente: Belegnummer im Dateinamen (100), Datum + Betrag (60), nur Datum (30).
    /// </summary>
    public IReadOnlyList<Zuordnungsvorschlag> Vorschlaege(int maxJeDokument = 3)
    {
        using var s = ab.Sitzung();
        var belege = s.Belege.Alle().Where(b => b.StornoVonId is null).ToList();
        var ergebnis = new List<Zuordnungsvorschlag>();
        foreach (var d in s.Dokumente.Unzugeordnet())
        {
            var name = Normalisiere(Path.GetFileNameWithoutExtension(d.Dateiname));
            var daten = DatenImNamen(d.Dateiname);
            var kandidaten = new List<Zuordnungsvorschlag>();
            foreach (var b in belege)
            {
                var nr = Normalisiere(b.Belegnummer);
                if (nr.Length >= 3 && name.Contains(nr, StringComparison.Ordinal))
                {
                    kandidaten.Add(new(Info(d, false), b.Id, b.Belegnummer, 100, "Belegnummer im Dateinamen"));
                    continue;
                }
                var datumTreffer = new[] { b.Leistungsdatum, b.Rechnungsdatum, b.Zahlungsdatum }.Any(x => x is not null && daten.Contains(x!));
                if (!datumTreffer) continue;
                var betrag = Werte.AusCent(b.BetragFwCent);
                var betragTreffer = name.Contains(Normalisiere(betrag.ToString("0.00", CultureInfo.InvariantCulture)), StringComparison.Ordinal);
                kandidaten.Add(new(Info(d, false), b.Id, b.Belegnummer, betragTreffer ? 60 : 30, betragTreffer ? "Datum und Betrag" : "Datum"));
            }
            ergebnis.AddRange(kandidaten.OrderByDescending(k => k.Punkte).ThenBy(k => k.BelegId).Take(maxJeDokument));
        }
        return ergebnis;
    }

    /// <summary>Prüft alle abgelegten Dateien gegen ihren SHA-256. Liefert fehlende oder veränderte Dokumente.</summary>
    public IReadOnlyList<DokumentInfo> PruefeIntegritaet()
    {
        using var s = ab.Sitzung();
        return s.Dokumente.Alle().Where(d =>
        {
            var pfad = Path.Combine(ab.Datenordner, d.RelativerPfad);
            return !File.Exists(pfad) || Hash(pfad) != d.Sha256;
        }).Select(d => Info(d, false)).ToList();
    }

    private static string Hash(string pfad)
    {
        using var f = File.OpenRead(pfad);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    private static DokumentInfo Info(DokumentZeile d, bool neu) => new(d.Id, d.Sha256, d.Dateiname, d.RelativerPfad, d.Groesse, d.Mime, neu);

    private static string Normalisiere(string s) => NichtAlnum().Replace(s.ToLowerInvariant(), "");

    private static HashSet<string> DatenImNamen(string name)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in DatumIso().Matches(name)) set.Add($"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}");
        foreach (Match m in DatumDe().Matches(name)) set.Add($"{m.Groups[3].Value}-{m.Groups[2].Value}-{m.Groups[1].Value}");
        return set;
    }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NichtAlnum();
    [GeneratedRegex(@"(20\d{2})-?(0[1-9]|1[0-2])-?(0[1-9]|[12]\d|3[01])")]
    private static partial Regex DatumIso();
    [GeneratedRegex(@"(0[1-9]|[12]\d|3[01])\.(0[1-9]|1[0-2])\.(20\d{2})")]
    private static partial Regex DatumDe();
}
