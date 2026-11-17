using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.Persistence;

namespace PropTax.Application;

public enum CsvFeld { Belegnummer, Leistungsdatum, Rechnungsdatum, Zahlungsdatum, Betrag, Waehrung, Partner, Belegtyp, Notiz }

/// <summary>Speicherbares Spalten-Mapping für den Beleg-Import.</summary>
public sealed record CsvProfil
{
    public char Trennzeichen { get; init; } = ';';
    public char Dezimalzeichen { get; init; } = ',';
    public string Datumsformat { get; init; } = "dd.MM.yyyy";
    public bool HatKopfzeile { get; init; } = true;
    /// <summary>Zielfeld → Spaltenname (bei Kopfzeile) oder 1-basierte Spaltennummer als Text.</summary>
    public Dictionary<CsvFeld, string> Zuordnung { get; init; } = [];
    public long BetriebId { get; init; }
    public Belegtyp StandardTyp { get; init; } = Belegtyp.ChallengeGebuehr;
    public string StandardWaehrung { get; init; } = "EUR";
    public long? StandardPartnerId { get; init; }
    public long? EuerKategorieId { get; init; }
}

public sealed record CsvAnalyse(char Trennzeichen, char Dezimalzeichen, string? Datumsformat, IReadOnlyList<string> Spalten, IReadOnlyList<string[]> Beispielzeilen);

public sealed record ImportZeile(int Zeile, BelegDaten? Daten, IReadOnlyList<string> Fehler, bool IstDublette, string? Hinweis)
{
    public bool Importierbar => Daten is not null && Fehler.Count == 0;
}

public sealed record ImportVorschau(IReadOnlyList<ImportZeile> Zeilen, bool DateiBereitsImportiert);

public sealed record ImportErgebnis(long ImportLaufId, int Ok, int Dubletten, int Fehler, IReadOnlyList<long> BelegIds);

/// <summary>Generischer CSV-Import von Belegen als Entwürfe (Analyse → Mapping → Vorschau → Import).</summary>
public sealed class ImportService(Arbeitsbereich ab)
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public CsvAnalyse Analysiere(string text)
    {
        var t = Csv.ErkenneTrennzeichen(text);
        var zeilen = Csv.Lies(text, t);
        var kopf = zeilen.FirstOrDefault() ?? [];
        var daten = zeilen.Skip(1).Take(50).ToList();
        var werte = daten.SelectMany(z => z).ToList();
        return new CsvAnalyse(t, Csv.ErkenneDezimalzeichen(werte), Csv.ErkenneDatumsformat(werte), kopf, daten.Take(10).ToList());
    }

    public IReadOnlyList<string> Profile()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.Profile().Select(p => p.Name).ToList();
    }

    public CsvProfil? LadeProfil(string name)
    {
        using var s = ab.Sitzung();
        var p = s.Stammdaten.Profil(name);
        return p is null ? null : JsonSerializer.Deserialize<CsvProfil>(p.ProfilJson, Json);
    }

    public void SpeichereProfil(string name, CsvProfil profil)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new FachlicherFehler("Profilname fehlt.");
        using var s = ab.Sitzung();
        s.Stammdaten.SpeichereProfil(name.Trim(), JsonSerializer.Serialize(profil, Json));
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "csv_profil", name.Trim(), "SPEICHERN", null, null, null, "Import-Profil");
        s.Commit();
    }

    public ImportVorschau Vorschau(string text, CsvProfil profil)
    {
        using var s = ab.Sitzung();
        return Pruefe(s, text, profil, 0);
    }

    /// <summary>
    /// Importiert alle fehlerfreien Zeilen als Entwürfe. Mögliche Dubletten werden übersprungen, außer
    /// <paramref name="dublettenUebernehmen"/> ist gesetzt. Eine bereits importierte Datei erfordert <paramref name="dateiErneut"/>.
    /// </summary>
    public ImportErgebnis Importiere(string text, string dateiname, CsvProfil profil, bool dublettenUebernehmen = false,
        bool dateiErneut = false, string? profilSpeichernAls = null)
    {
        if (profilSpeichernAls is not null) SpeichereProfil(profilSpeichernAls, profil);
        using var s = ab.Sitzung();
        var sha = Sha(text);
        if (!dateiErneut && s.Importe.NachDateiHash("BELEGE", sha).Count > 0)
            throw new FachlicherFehler($"Die Datei {Path.GetFileName(dateiname)} wurde bereits importiert.");
        var profilId = profilSpeichernAls is null ? null : s.Stammdaten.Profil(profilSpeichernAls.Trim())?.Id;
        var lauf = s.Importe.Einfuegen(new ImportLaufZeile
        {
            Art = "BELEGE", ProfilId = profilId, Datei = Path.GetFileName(dateiname), DateiSha256 = sha, Zeitpunkt = Werte.Zeit(ab.Jetzt),
        });
        var vorschau = Pruefe(s, text, profil, lauf);
        var ids = new List<long>();
        int dubletten = 0, fehler = 0;
        var belege = ab.Belege;
        foreach (var z in vorschau.Zeilen)
        {
            if (!z.Importierbar) { fehler++; continue; }
            if (z.IstDublette && !dublettenUebernehmen) { dubletten++; continue; }
            ids.Add(belege.ErfasseEntwurf(s, z.Daten!, lauf));
        }
        s.Importe.Abschliessen(lauf, ids.Count, dubletten, fehler);
        s.Commit();
        return new ImportErgebnis(lauf, ids.Count, dubletten, fehler, ids);
    }

    private ImportVorschau Pruefe(Sitzung s, string text, CsvProfil p, long lauf)
    {
        var zeilen = Csv.Lies(text, p.Trennzeichen);
        var kopf = p.HatKopfzeile && zeilen.Count > 0 ? zeilen[0] : [];
        var daten = p.HatKopfzeile ? zeilen.Skip(1) : zeilen;
        var index = p.Zuordnung.ToDictionary(kv => kv.Key, kv => Spalte(kopf, kv.Value));
        var partner = s.Stammdaten.Partner();
        var schluesselImFile = new HashSet<string>();
        var nummernImFile = new HashSet<string>(StringComparer.Ordinal);
        var ergebnis = new List<ImportZeile>();
        var nr = p.HatKopfzeile ? 1 : 0;

        foreach (var z in daten)
        {
            nr++;
            var f = new List<string>();
            string? Wert(CsvFeld feld) => index.TryGetValue(feld, out var i) && i >= 0 && i < z.Length && z[i].Length > 0 ? z[i] : null;
            DateOnly? Datum(CsvFeld feld)
            {
                var w = Wert(feld);
                if (w is null) return null;
                if (Csv.TryDatum(w, p.Datumsformat, out var d)) return d;
                f.Add($"{feld}: „{w}“ passt nicht zu {p.Datumsformat}.");
                return null;
            }

            var leistung = Datum(CsvFeld.Leistungsdatum);
            var rechnung = Datum(CsvFeld.Rechnungsdatum);
            var zahlung = Datum(CsvFeld.Zahlungsdatum);
            var datum = leistung ?? rechnung ?? zahlung;
            if (datum is null && f.Count == 0) f.Add("Kein Datum.");

            decimal betrag = 0m;
            var bw = Wert(CsvFeld.Betrag);
            if (bw is null) f.Add("Kein Betrag.");
            else if (!Csv.TryBetrag(bw, p.Dezimalzeichen, out betrag)) f.Add($"Betrag „{bw}“ nicht lesbar.");
            else if (betrag == 0m) f.Add("Betrag ist 0.");
            betrag = Math.Abs(betrag); // Vorzeichen aus Bankexporten; Richtung ergibt sich aus dem Belegtyp.

            var waehrung = (Wert(CsvFeld.Waehrung) ?? p.StandardWaehrung).Trim().ToUpperInvariant();
            if (waehrung.Length != 3) f.Add($"Währung „{waehrung}“ ungültig.");

            long? partnerId = p.StandardPartnerId;
            if (Wert(CsvFeld.Partner) is { } pn)
            {
                var treffer = partner.FirstOrDefault(x => string.Equals(x.Name, pn.Trim(), StringComparison.OrdinalIgnoreCase));
                if (treffer is null) f.Add($"Partner „{pn}“ unbekannt (in Stammdaten anlegen).");
                else partnerId = treffer.Id;
            }
            if (partnerId is null && f.All(x => !x.StartsWith("Partner", StringComparison.Ordinal))) f.Add("Kein Partner.");

            var typ = p.StandardTyp;
            if (Wert(CsvFeld.Belegtyp) is { } tw)
            {
                if (Enum.TryParse<Belegtyp>(tw, true, out var t)) typ = t;
                else f.Add($"Belegtyp „{tw}“ unbekannt.");
            }
            if (typ == Belegtyp.Erstattung) f.Add("Erstattungen bitte einzeln mit Bezug auf den Ursprungsbeleg erfassen.");

            var belegnummer = Wert(CsvFeld.Belegnummer)?.Trim()
                ?? $"IMP{lauf.ToString(CultureInfo.InvariantCulture)}-{nr.ToString("0000", CultureInfo.InvariantCulture)}";
            if (Wert(CsvFeld.Belegnummer) is not null && (s.Belege.NummerVergeben(belegnummer) || !nummernImFile.Add(belegnummer)))
                f.Add($"Belegnummer {belegnummer} bereits vorhanden.");

            if (f.Count > 0) { ergebnis.Add(new ImportZeile(nr, null, f, false, null)); continue; }

            var d = new BelegDaten
            {
                Belegnummer = belegnummer, BetriebId = p.BetriebId, PartnerId = partnerId!.Value, Typ = typ, Richtung = BelegDaten.RichtungFuer(typ),
                Rechnungsdatum = rechnung ?? datum, Leistungsdatum = leistung ?? datum!.Value, Zahlungsdatum = zahlung ?? datum,
                Betrag = new CurrencyAmount(betrag, waehrung), EuerKategorieId = p.EuerKategorieId, Notiz = Wert(CsvFeld.Notiz),
            };
            var schluessel = Abbildung.DublettenSchluessel(d);
            string? hinweis = null;
            if (s.Belege.Dubletten(schluessel).Count > 0) hinweis = "Mögliche Dublette (gleicher Partner, Datum, Betrag bereits erfasst).";
            else if (!schluesselImFile.Add(schluessel)) hinweis = "Mögliche Dublette innerhalb der Datei.";
            ergebnis.Add(new ImportZeile(nr, d, [], hinweis is not null, hinweis));
        }
        return new ImportVorschau(ergebnis, s.Importe.NachDateiHash("BELEGE", Sha(text)).Count > 0);
    }

    private static int Spalte(string[] kopf, string angabe)
    {
        var i = Array.FindIndex(kopf, k => string.Equals(k, angabe.Trim(), StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        return int.TryParse(angabe, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n - 1 : -1;
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
