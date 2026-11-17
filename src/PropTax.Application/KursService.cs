using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PropTax.Domain;
using PropTax.Persistence;

namespace PropTax.Application;

public sealed record KursDaten(string Waehrung, int Jahr, int Monat, decimal Kurs, Kursquelle Quelle);

public sealed record KursImportErgebnis(int Uebernommen, IReadOnlyList<string> Fehler);

/// <summary>
/// Pflege der Umrechnungskurse (Notation 1 EUR = x Fremdwährung). CSV-Formate:
/// lang: <c>Währung;Monat;Kurs</c> (Monat als 2026-03, 03/2026, 03.2026) oder
/// breit: Kopfzeile <c>Währung;2026-01;2026-02;…</c>.
/// </summary>
public sealed partial class KursService(Arbeitsbereich ab)
{
    public IReadOnlyList<KursDaten> Liste()
    {
        using var s = ab.Sitzung();
        return s.Regelwerk.Kurse().Select(k => new KursDaten(k.Waehrung, (int)k.Jahr, (int)k.Monat, Werte.Dez(k.Kurs), Enum.Parse<Kursquelle>(k.Quelle))).ToList();
    }

    public void Setze(KursDaten k, string begruendung = "Kurspflege")
    {
        Pruefe(k);
        using var s = ab.Sitzung();
        Speichere(s, k, null, begruendung);
        s.Commit();
    }

    public KursImportErgebnis ImportiereCsv(string text, string dateiname, Kursquelle quelle = Kursquelle.Bmf)
    {
        var t = Csv.ErkenneTrennzeichen(text);
        var zeilen = Csv.Lies(text, t);
        var fehler = new List<string>();
        var kurse = new List<KursDaten>();
        if (zeilen.Count == 0) return new KursImportErgebnis(0, ["Datei ist leer."]);

        var kopfMonate = zeilen[0].Skip(1).Select(Monat).ToList();
        if (kopfMonate.Count >= 2 && kopfMonate.All(m => m is not null))
        {
            foreach (var z in zeilen.Skip(1))
                for (var i = 1; i < z.Length && i <= kopfMonate.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(z[i])) continue;
                    if (Kurs(z[i]) is { } kurs) kurse.Add(new KursDaten(z[0].ToUpperInvariant(), kopfMonate[i - 1]!.Value.J, kopfMonate[i - 1]!.Value.M, kurs, quelle));
                    else fehler.Add($"{z[0]} {zeilen[0][i]}: Kurs „{z[i]}“ ungültig.");
                }
        }
        else
        {
            for (var n = 0; n < zeilen.Count; n++)
            {
                var z = zeilen[n];
                if (z.Length < 3) { fehler.Add($"Zeile {n + 1}: erwartet Währung;Monat;Kurs."); continue; }
                var m = Monat(z[1]);
                var k = Kurs(z[2]);
                if (m is null || k is null)
                {
                    if (n > 0) fehler.Add($"Zeile {n + 1}: „{string.Join(t, z)}“ nicht lesbar.");
                    continue; // Kopfzeile
                }
                kurse.Add(new KursDaten(z[0].ToUpperInvariant(), m.Value.J, m.Value.M, k.Value, quelle));
            }
        }

        using var s = ab.Sitzung();
        var lauf = s.Importe.Einfuegen(new ImportLaufZeile
        {
            Art = "KURSE", Datei = Path.GetFileName(dateiname), Zeitpunkt = Werte.Zeit(ab.Jetzt),
            DateiSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
        });
        var ok = 0;
        foreach (var k in kurse)
        {
            try { Pruefe(k); Speichere(s, k, lauf, $"Kursimport {Path.GetFileName(dateiname)}"); ok++; }
            catch (FachlicherFehler ex) { fehler.Add(ex.Message); }
        }
        s.Importe.Abschliessen(lauf, ok, 0, fehler.Count);
        s.Commit();
        return new KursImportErgebnis(ok, fehler);
    }

    private void Speichere(Sitzung s, KursDaten k, long? lauf, string begruendung)
    {
        var alt = s.Regelwerk.Kurse().SingleOrDefault(x => x.Waehrung == k.Waehrung && x.Jahr == k.Jahr && x.Monat == k.Monat && x.Quelle == k.Quelle.ToString());
        s.Regelwerk.SpeichereKurs(k.Waehrung, k.Jahr, k.Monat, k.Kurs, k.Quelle, lauf);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "kurs", $"{k.Waehrung} {k.Jahr}-{k.Monat:00} {k.Quelle}", alt is null ? "ANLAGE" : "AENDERUNG",
            "Kurs", alt?.Kurs, Werte.Dez(k.Kurs), begruendung);
    }

    private static void Pruefe(KursDaten k)
    {
        if (!WaehrungsCode().IsMatch(k.Waehrung) || k.Waehrung == "EUR") throw new FachlicherFehler($"Ungültige Währung „{k.Waehrung}“.");
        if (k.Monat is < 1 or > 12 || k.Jahr is < 2000 or > 2100) throw new FachlicherFehler($"Ungültiger Monat {k.Monat}/{k.Jahr}.");
        if (k.Kurs <= 0m) throw new FachlicherFehler($"Kurs für {k.Waehrung} {k.Monat:00}/{k.Jahr} muss positiv sein.");
    }

    private static (int J, int M)? Monat(string s)
    {
        var m = MonatIso().Match(s.Trim());
        if (m.Success) return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        m = MonatDe().Match(s.Trim());
        if (m.Success) return (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        return null;
    }

    private static decimal? Kurs(string s)
    {
        var w = s.Trim();
        var dez = w.Contains(',') && !w.Contains('.') ? ',' : '.';
        return Csv.TryBetrag(w, dez, out var k) ? k : null;
    }

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})$")]
    private static partial Regex MonatIso();
    [GeneratedRegex(@"^(\d{1,2})[./](\d{4})$")]
    private static partial Regex MonatDe();
    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex WaehrungsCode();
}
