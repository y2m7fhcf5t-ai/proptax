using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PropTax.Reporting;

/// <summary>Finanzamt-Nachweisreport als PDF, XLSX und ZIP (inkl. Belegdateien).</summary>
public static class NachweisExport
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    public const string Hinweis = "Erstellt mit PropTax – Erfassungs- und Berechnungswerkzeug, keine Steuerberatung. Regeln und Kennzahlen vor Verwendung fachlich prüfen.";

    static NachweisExport() => QuestPDF.Settings.License = LicenseType.Community;

    private static string G(decimal d) => d.ToString("N2", De);

    public static void Pdf(NachweisBericht b, Stream ziel)
    {
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontSize(8));
            page.Header().Column(c =>
            {
                c.Item().Text($"Nachweis Prop-Firmen-Gebühren {b.Jahr}").FontSize(14).SemiBold();
                c.Item().Text($"{b.Unternehmen} · erstellt {b.Erstellt.ToLocalTime():dd.MM.yyyy HH:mm} · {b.Zeilen.Count} Belege · " +
                              $"Umrechnung §16 Abs. 6 UStG (BMF-Monatskurse) · Steuerschuldnerschaft §13b UStG");
            });
            page.Content().PaddingTop(8).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(52); c.RelativeColumn(1.4f); c.RelativeColumn(1.6f); c.ConstantColumn(28); c.RelativeColumn(1.2f);
                    c.ConstantColumn(62); c.ConstantColumn(30); c.ConstantColumn(44); c.ConstantColumn(60); c.ConstantColumn(55);
                    c.ConstantColumn(44); c.ConstantColumn(44); c.RelativeColumn(1.6f);
                });
                t.Header(h =>
                {
                    foreach (var k in new[] { "Datum", "Beleg", "Partner", "Land", "Typ", "Betrag FW", "Whg", "Kurs", "EUR", "§13b-USt", "Kz", "Zeitraum", "Belegdatei / Hinweis" })
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(2).Text(k).SemiBold();
                });
                foreach (var z in b.Zeilen)
                {
                    t.Cell().Padding(2).Text(z.Datum.ToString("dd.MM.yyyy", De));
                    t.Cell().Padding(2).Text(z.Belegnummer);
                    t.Cell().Padding(2).Text(z.Partner);
                    t.Cell().Padding(2).Text(z.Land ?? "");
                    t.Cell().Padding(2).Text(z.Typ);
                    t.Cell().Padding(2).AlignRight().Text(G(z.BetragFw));
                    t.Cell().Padding(2).Text(z.Waehrung);
                    t.Cell().Padding(2).AlignRight().Text(z.Kurs?.ToString("0.0000", De) ?? "");
                    t.Cell().Padding(2).AlignRight().Text(G(z.Eur));
                    t.Cell().Padding(2).AlignRight().Text(G(z.Steuer13b));
                    t.Cell().Padding(2).Text(z.Kennzahlen);
                    t.Cell().Padding(2).Text(z.Zeitraum);
                    t.Cell().Padding(2).Text(string.Join(", ", z.Belegdateien.DefaultIfEmpty("FEHLT: kein Beleg")) + (z.Hinweis is null ? "" : $" · {z.Hinweis}"));
                }
                t.Cell().ColumnSpan(8).BorderTop(1).Padding(2).Text("Summe").SemiBold();
                t.Cell().BorderTop(1).Padding(2).AlignRight().Text(G(b.SummeEur)).SemiBold();
                t.Cell().BorderTop(1).Padding(2).AlignRight().Text(G(b.SummeSteuer)).SemiBold();
                t.Cell().ColumnSpan(3).BorderTop(1).Padding(2).Text(b.AnzahlOhneBeleg > 0 ? $"{b.AnzahlOhneBeleg} ohne Belegdatei" : "");
            });
            page.Footer().Row(r =>
            {
                r.RelativeItem().Text(Hinweis).FontSize(7).FontColor(Colors.Grey.Darken1);
                r.ConstantItem(60).AlignRight().Text(x => { x.Span("Seite "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        })).GeneratePdf(ziel);
    }

    public static void Xlsx(NachweisBericht b, Stream ziel)
    {
        var kopf = new[] { "Datum", "Belegnummer", "Partner", "Land", "Typ", "Betrag FW", "Währung", "Kurs (1 EUR =)", "EUR", "§13b-USt", "Kennzahlen", "Zeitraum", "Hinweis", "Belegdateien" };
        var zeilen = b.Zeilen.Select(z => (IReadOnlyList<object?>)new object?[]
        {
            z.Datum, z.Belegnummer, z.Partner, z.Land, z.Typ, z.BetragFw, z.Waehrung, z.Kurs, z.Eur, z.Steuer13b, z.Kennzahlen, z.Zeitraum, z.Hinweis,
            string.Join(", ", z.Belegdateien),
        }).ToList();
        TabellenExport.Xlsx(new Tabelle($"Nachweis Prop-Firmen-Gebühren {b.Jahr} – {b.Unternehmen}", kopf, zeilen, [Hinweis]), ziel,
            summenSpalten: [8, 9]);
    }

    /// <summary>ZIP mit nachweis.pdf, nachweis.xlsx und allen Belegdateien unter belege/&lt;Belegnummer&gt;_&lt;Datei&gt;.</summary>
    public static void Zip(NachweisBericht b, IEnumerable<BelegAnhang> anhaenge, Stream ziel)
    {
        using var zip = new ZipArchive(ziel, ZipArchiveMode.Create, leaveOpen: true);
        using (var s = zip.CreateEntry("nachweis.pdf").Open()) Pdf(b, s);
        using (var s = zip.CreateEntry("nachweis.xlsx").Open())
        {
            using var ms = new MemoryStream();
            Xlsx(b, ms);
            ms.Position = 0;
            ms.CopyTo(s);
        }
        var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in anhaenge)
        {
            var name = $"belege/{Sicher(a.Belegnummer)}_{Sicher(a.Dateiname)}";
            if (!namen.Add(name)) continue;
            zip.CreateEntryFromFile(a.Pfad, name, CompressionLevel.Optimal);
        }
    }

    /// <summary>Dateinamen nach Windows-Regeln bereinigen (unabhängig vom Betriebssystem, auf dem exportiert wird).</summary>
    private static string Sicher(string s) => string.Concat(s.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c));
}

public static class TabellenExport
{
    static TabellenExport() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Schreibt eine Tabelle als XLSX (Titel, Kopf, Daten, optionale Summenzeile, Fußnoten).</summary>
    public static void Xlsx(Tabelle t, Stream ziel, IReadOnlyCollection<int>? summenSpalten = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(t.Titel.Length > 31 ? t.Titel[..31].Replace(":", "") : t.Titel.Replace(":", ""));
        ws.Cell(1, 1).Value = t.Titel;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        for (var c = 0; c < t.Kopf.Count; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = t.Kopf[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        var r = 4;
        foreach (var zeile in t.Zeilen)
        {
            for (var c = 0; c < zeile.Count; c++) Setze(ws.Cell(r, c + 1), zeile[c]);
            r++;
        }
        if (summenSpalten is { Count: > 0 } && t.Zeilen.Count > 0)
        {
            ws.Cell(r, 1).Value = "Summe";
            ws.Cell(r, 1).Style.Font.Bold = true;
            foreach (var c in summenSpalten)
            {
                var spalte = ws.Column(c + 1).ColumnLetter();
                var cell = ws.Cell(r, c + 1);
                cell.FormulaA1 = $"SUM({spalte}4:{spalte}{r - 1})";
                cell.Style.NumberFormat.Format = "#,##0.00";
                cell.Style.Font.Bold = true;
            }
            r++;
        }
        foreach (var f in t.Fussnoten ?? [])
        {
            r++;
            ws.Cell(r, 1).Value = f;
            ws.Cell(r, 1).Style.Font.Italic = true;
        }
        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents(3, Math.Max(3, r), 8, 60);
        wb.SaveAs(ziel);
    }

    /// <summary>Tabelle als PDF (A4 quer, Kopfzeile wiederholt, Fußnoten und Seitenzahlen).</summary>
    public static void Pdf(Tabelle t, Stream ziel, string? untertitel = null)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        static bool Zahl(object? v) => v is decimal or int or long;
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(8));
            page.Header().Column(c =>
            {
                c.Item().Text(t.Titel).FontSize(13).SemiBold();
                if (untertitel is not null) c.Item().Text(untertitel);
            });
            page.Content().PaddingTop(8).Column(c =>
            {
                c.Item().Table(tab =>
                {
                    tab.ColumnsDefinition(cd => { foreach (var _ in t.Kopf) cd.RelativeColumn(); });
                    tab.Header(h => { foreach (var k in t.Kopf) h.Cell().Background(Colors.Grey.Lighten3).Padding(2).Text(k).SemiBold(); });
                    foreach (var z in t.Zeilen)
                        for (var i = 0; i < t.Kopf.Count; i++)
                        {
                            var v = i < z.Count ? z[i] : null;
                            var text = v switch
                            {
                                null => "",
                                decimal d => d.ToString("N2", de),
                                DateOnly d => d.ToString("dd.MM.yyyy", de),
                                _ => Convert.ToString(v, de) ?? "",
                            };
                            var zelle = tab.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2);
                            if (Zahl(v)) zelle.AlignRight().Text(text); else zelle.Text(text);
                        }
                });
                foreach (var f in t.Fussnoten ?? []) c.Item().PaddingTop(6).Text(f).Italic();
            });
            page.Footer().Row(r =>
            {
                r.RelativeItem().Text(NachweisExport.Hinweis).FontSize(7).FontColor(Colors.Grey.Darken1);
                r.ConstantItem(60).AlignRight().Text(x => { x.Span("Seite "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        })).GeneratePdf(ziel);
    }

    /// <summary>CSV (Semikolon, de-DE-Zahlen, UTF-8 mit BOM für Excel).</summary>
    public static void Csv(Tabelle t, Stream ziel)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        using var w = new StreamWriter(ziel, new System.Text.UTF8Encoding(true), leaveOpen: true);
        static string Q(string s) => s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        w.WriteLine(string.Join(';', t.Kopf.Select(Q)));
        foreach (var z in t.Zeilen)
            w.WriteLine(string.Join(';', z.Select(v => Q(v switch
            {
                null => "",
                decimal d => d.ToString("0.00", de),
                DateOnly d => d.ToString("dd.MM.yyyy", de),
                _ => Convert.ToString(v, de) ?? "",
            }))));
    }

    private static void Setze(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: break;
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.00##";
                break;
            case DateOnly d:
                cell.Value = d.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd.mm.yyyy";
                break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            default: cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture); break;
        }
    }
}
