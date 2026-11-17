using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PropTax.Application;

/// <summary>RFC-4180-naher CSV-Leser (Anführungszeichen, verdoppelte Quotes, Zeilenumbrüche in Feldern) und Formaterkennung.</summary>
public static partial class Csv
{
    public static IReadOnlyList<string[]> Lies(string text, char trennzeichen)
    {
        var zeilen = new List<string[]>();
        var feld = new StringBuilder();
        var zeile = new List<string>();
        var inQuotes = false;
        text = text.TrimStart('﻿');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else feld.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == trennzeichen) { zeile.Add(feld.ToString()); feld.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                zeile.Add(feld.ToString()); feld.Clear();
                if (zeile.Count > 1 || zeile[0].Length > 0) zeilen.Add([.. zeile]);
                zeile.Clear();
            }
            else feld.Append(c);
        }
        zeile.Add(feld.ToString());
        if (zeile.Count > 1 || zeile[0].Length > 0) zeilen.Add([.. zeile]);
        return zeilen.Select(z => z.Select(f => f.Trim()).ToArray()).ToList();
    }

    /// <summary>Trennzeichen mit der konsistentesten Spaltenzahl (; , Tab |).</summary>
    public static char ErkenneTrennzeichen(string text)
    {
        var probe = string.Join('\n', text.Split('\n').Take(20));
        return new[] { ';', ',', '\t', '|' }
            .Select(t => (T: t, Z: Lies(probe, t)))
            .Where(x => x.Z.Count > 0 && x.Z[0].Length > 1)
            .OrderByDescending(x => x.Z.Count(z => z.Length == x.Z[0].Length))
            .ThenByDescending(x => x.Z[0].Length)
            .Select(x => x.T)
            .DefaultIfEmpty(';')
            .First();
    }

    /// <summary>',' wenn Werte wie 1.234,56 / 99,00 vorkommen, sonst '.'.</summary>
    public static char ErkenneDezimalzeichen(IEnumerable<string> werte)
    {
        int komma = 0, punkt = 0;
        foreach (var w in werte)
        {
            if (DeZahl().IsMatch(w)) komma++;
            else if (EnZahl().IsMatch(w)) punkt++;
        }
        return komma > punkt ? ',' : '.';
    }

    public static readonly string[] Datumsformate =
        ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm"];

    /// <summary>Erstes Format, das alle datumsartigen Werte parst (dd/MM vor MM/dd).</summary>
    public static string? ErkenneDatumsformat(IEnumerable<string> werte)
    {
        var kandidaten = werte.Where(w => DatumAehnlich().IsMatch(w)).ToList();
        if (kandidaten.Count == 0) return null;
        return Datumsformate.FirstOrDefault(f => kandidaten.All(w => DateTime.TryParseExact(w, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)));
    }

    public static bool TryDatum(string wert, string format, out DateOnly datum)
    {
        datum = default;
        if (!DateTime.TryParseExact(wert.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return false;
        datum = DateOnly.FromDateTime(dt);
        return true;
    }

    /// <summary>Parst Beträge wie "1.234,56", "-99,00 €", "$1,234.56", "(12.50)", "USD 99".</summary>
    public static bool TryBetrag(string wert, char dezimalzeichen, out decimal betrag)
    {
        betrag = 0m;
        var w = wert.Trim();
        var negativ = false;
        if (w.StartsWith('(') && w.EndsWith(')')) { negativ = true; w = w[1..^1]; }
        w = Waehrungszeichen().Replace(w, "").Replace(" ", "").Replace(" ", "");
        if (w.EndsWith('-')) { negativ = !negativ; w = w[..^1]; }
        var tausender = dezimalzeichen == ',' ? '.' : ',';
        w = w.Replace(tausender.ToString(), "").Replace(dezimalzeichen, '.');
        if (!decimal.TryParse(w, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out betrag)) return false;
        if (negativ) betrag = -betrag;
        return true;
    }

    [GeneratedRegex(@"^-?\(?[\d.]*\d,\d{1,6}\)?$")]
    private static partial Regex DeZahl();
    [GeneratedRegex(@"^-?\(?[\d,]*\d\.\d{1,6}\)?$")]
    private static partial Regex EnZahl();
    [GeneratedRegex(@"^\d{1,4}[./-]\d{1,2}[./-]\d{1,4}")]
    private static partial Regex DatumAehnlich();
    [GeneratedRegex(@"[A-Za-z€$£]")]
    private static partial Regex Waehrungszeichen();
}
