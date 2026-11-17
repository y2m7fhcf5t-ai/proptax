using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;

namespace PropTax.Reporting.Tests;

public class ExportTests
{
    private static NachweisBericht Bericht() => new("Max Trader", 2026, new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero),
    [
        new(new DateOnly(2026, 3, 10), "FN-1001", "Funded Next", "AE", "Challenge-Gebühr", 99m, "USD", 1.08m, 91.67m, 17.42m, "52/53", "2026-Q1", null, ["rechnung.pdf"]),
        new(new DateOnly(2026, 2, 1), "DF-1", "Data Feeds Ltd", "IE", "Software/Abo", 30m, "EUR", null, 30m, 5.70m, "46/47", "2026-Q1", null, []),
        new(new DateOnly(2026, 4, 5), "FN-1001-S", "Funded Next", "AE", "Challenge-Gebühr", -99m, "USD", 1.08m, -91.67m, -17.42m, "52/53", "2026-Q2", "Storno zu FN-1001", []),
    ]);

    [Fact]
    public void Summen()
    {
        var b = Bericht();
        Assert.Equal(30m, b.SummeEur);
        Assert.Equal(5.70m, b.SummeSteuer);
        Assert.Equal(2, b.AnzahlOhneBeleg);
    }

    [Fact]
    public void Pdf_wird_erzeugt()
    {
        using var ms = new MemoryStream();
        NachweisExport.Pdf(Bericht(), ms);
        var kopf = Encoding.ASCII.GetString(ms.ToArray(), 0, 5);
        Assert.Equal("%PDF-", kopf);
        Assert.True(ms.Length > 2000);
        var ziel = Environment.GetEnvironmentVariable("PROPTAX_PDF");
        if (!string.IsNullOrEmpty(ziel)) File.WriteAllBytes(ziel, ms.ToArray());
    }

    [Fact]
    public void Xlsx_enthaelt_Werte_und_Summenformel()
    {
        using var ms = new MemoryStream();
        NachweisExport.Xlsx(Bericht(), ms);
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        Assert.Contains("Nachweis", ws.Cell(1, 1).GetString());
        Assert.Equal("Belegnummer", ws.Cell(3, 2).GetString());
        Assert.Equal("FN-1001", ws.Cell(4, 2).GetString());
        Assert.Equal(91.67m, ws.Cell(4, 9).GetValue<decimal>());
        Assert.Equal(new DateTime(2026, 3, 10), ws.Cell(4, 1).GetDateTime());
        Assert.Equal("Summe", ws.Cell(7, 1).GetString());
        Assert.Equal("SUM(I4:I6)", ws.Cell(7, 9).FormulaA1);
        Assert.Equal(30m, ws.Cell(7, 9).GetValue<decimal>());
        Assert.Equal(5.70m, ws.Cell(7, 10).GetValue<decimal>());
        Assert.Contains("keine Steuerberatung", ws.Cell(9, 1).GetString());
    }

    [Fact]
    public void Zip_mit_Bericht_und_Belegen()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "proptax-rep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var datei = Path.Combine(tmp, "abc.pdf");
            File.WriteAllText(datei, "%PDF beleg");
            using var ms = new MemoryStream();
            NachweisExport.Zip(Bericht(), [new("FN-1001", "rechnung.pdf", datei), new("FN-1001", "rechnung.pdf", datei), new("A/B", "x:y.pdf", datei)], ms);
            ms.Position = 0;
            using var zip = new ZipArchive(ms);
            Assert.Equal(["nachweis.pdf", "nachweis.xlsx", "belege/FN-1001_rechnung.pdf", "belege/A_B_x_y.pdf"], zip.Entries.Select(e => e.FullName));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Tabelle_als_Xlsx_ohne_Summen_und_Csv()
    {
        var t = new Tabelle("USt-VA: Vorschau 2026-Q1 mit sehr langem Titel für das Blatt", ["Kz", "Bezeichnung", "Betrag", "Anzahl", "Datum"],
            [["52", "Leistungen; Ausland", 91.67m, 1, new DateOnly(2026, 3, 31)], ["53", null, 17.42m, 1L, null]]);
        using var x = new MemoryStream();
        TabellenExport.Xlsx(t, x);
        x.Position = 0;
        using var wb = new XLWorkbook(x);
        Assert.Equal(17.42m, wb.Worksheets.First().Cell(5, 3).GetValue<decimal>());
        Assert.True(wb.Worksheets.First().Name.Length <= 31);

        using var c = new MemoryStream();
        TabellenExport.Csv(t, c);
        var text = Encoding.UTF8.GetString(c.ToArray()).TrimStart('﻿');
        Assert.StartsWith("Kz;Bezeichnung;Betrag;Anzahl;Datum", text);
        Assert.Contains("52;\"Leistungen; Ausland\";91,67;1;31.03.2026", text);
        Assert.Contains("53;;17,42;1;", text);
    }
}
