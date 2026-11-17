namespace PropTax.Reporting;

/// <summary>Eine Zeile des Finanzamt-Nachweises (Prop-Firmen-Gebühren inkl. Erstattungen/Stornos).</summary>
public sealed record NachweisZeile(
    DateOnly Datum,
    string Belegnummer,
    string Partner,
    string? Land,
    string Typ,
    decimal BetragFw,
    string Waehrung,
    decimal? Kurs,
    decimal Eur,
    decimal Steuer13b,
    string Kennzahlen,
    string Zeitraum,
    string? Hinweis,
    IReadOnlyList<string> Belegdateien);

public sealed record NachweisBericht(string Unternehmen, int Jahr, DateTimeOffset Erstellt, IReadOnlyList<NachweisZeile> Zeilen)
{
    public decimal SummeEur => Zeilen.Sum(z => z.Eur);
    public decimal SummeSteuer => Zeilen.Sum(z => z.Steuer13b);
    public int AnzahlOhneBeleg => Zeilen.Count(z => z.Belegdateien.Count == 0);
}

/// <summary>Belegdatei für das ZIP-Paket.</summary>
public sealed record BelegAnhang(string Belegnummer, string Dateiname, string Pfad);

/// <summary>Generische Tabelle (USt-VA-Vorschau, EÜR, …) für XLSX/CSV-Export.</summary>
public sealed record Tabelle(string Titel, IReadOnlyList<string> Kopf, IReadOnlyList<IReadOnlyList<object?>> Zeilen, IReadOnlyList<string>? Fussnoten = null);
