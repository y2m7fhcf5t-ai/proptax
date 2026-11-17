using System.Text;
using PropTax.Domain;

namespace PropTax.Application.Tests;

public class CsvTests
{
    [Fact]
    public void Lies_Quotes_Trennzeichen_Zeilenumbruch_BOM()
    {
        var z = Csv.Lies("\uFEFFa;\"b;c\";\"x \"\"y\"\"\"\r\n1;\"zwei\nzeilen\";3\n\n", ';');
        Assert.Equal(2, z.Count);
        Assert.Equal(["a", "b;c", "x \"y\""], z[0]);
        Assert.Equal("zwei\nzeilen", z[1][1]);
    }

    [Theory]
    [InlineData("Datum;Betrag;Partner\n01.03.2026;99,00;A\n", ';')]
    [InlineData("Date,Amount,Partner\n2026-03-01,\"1,234.56\",A\n2026-03-02,5.00,B\n", ',')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    public void Trennzeichen_erkennen(string text, char erwartet) => Assert.Equal(erwartet, Csv.ErkenneTrennzeichen(text));

    [Fact]
    public void Dezimal_und_Datumsformat_erkennen()
    {
        Assert.Equal(',', Csv.ErkenneDezimalzeichen(["1.234,56", "99,00", "abc"]));
        Assert.Equal('.', Csv.ErkenneDezimalzeichen(["1,234.56", "99.00"]));
        Assert.Equal("dd.MM.yyyy", Csv.ErkenneDatumsformat(["01.03.2026", "31.12.2026", "x"]));
        Assert.Equal("yyyy-MM-dd", Csv.ErkenneDatumsformat(["2026-03-01"]));
        Assert.Equal("MM/dd/yyyy", Csv.ErkenneDatumsformat(["03/31/2026"]));
        Assert.Null(Csv.ErkenneDatumsformat(["abc"]));
    }

    [Theory]
    [InlineData("1.234,56", ',', "1234.56")]
    [InlineData("-99,00 €", ',', "-99.00")]
    [InlineData("$1,234.56", '.', "1234.56")]
    [InlineData("(12.50)", '.', "-12.50")]
    [InlineData("USD 99", '.', "99")]
    [InlineData("99,00-", ',', "-99.00")]
    public void Betraege_parsen(string w, char dez, string erwartet)
    {
        Assert.True(Csv.TryBetrag(w, dez, out var b));
        Assert.Equal(decimal.Parse(erwartet, System.Globalization.CultureInfo.InvariantCulture), b);
    }

    [Fact]
    public void Ungueltiger_Betrag_und_Datum()
    {
        Assert.False(Csv.TryBetrag("abc", ',', out _));
        Assert.False(Csv.TryDatum("32.13.2026", "dd.MM.yyyy", out _));
    }
}

public class KursImportTests : Basis
{
    [Fact]
    public void Langformat_mit_Kopfzeile_und_Fehlern()
    {
        var r = Ab.Kurse.ImportiereCsv("Waehrung;Monat;Kurs\nUSD;2026-05;1,0812\nGBP;06/2026;0.8512\nCHF;2026-13;0,95\nXX;2026-01;1\nJPY;2026-01\n", "bmf.csv");
        Assert.Equal(2, r.Uebernommen);
        Assert.Equal(3, r.Fehler.Count);
        Assert.Contains(Ab.Kurse.Liste(), k => k.Waehrung == "GBP" && k.Monat == 6 && k.Kurs == 0.8512m);
    }

    [Fact]
    public void Breitformat_Monate_als_Spalten()
    {
        var r = Ab.Kurse.ImportiereCsv("Waehrung;2026-01;2026-02;2026-03\nUSD;1,03;1,04;\nAED;3,80;x;3,95\n", "breit.csv");
        Assert.Equal(4, r.Uebernommen);
        Assert.Single(r.Fehler);
        Assert.Equal(1.04m, Ab.Kurse.Liste().Single(k => k.Waehrung == "USD" && k.Monat == 2).Kurs);
    }

    [Fact]
    public void Kurs_setzen_prueft_Werte()
    {
        Assert.Throws<FachlicherFehler>(() => Ab.Kurse.Setze(new KursDaten("EUR", 2026, 1, 1m, Kursquelle.Bmf)));
        Assert.Throws<FachlicherFehler>(() => Ab.Kurse.Setze(new KursDaten("USD", 2026, 0, 1m, Kursquelle.Bmf)));
        Assert.Throws<FachlicherFehler>(() => Ab.Kurse.Setze(new KursDaten("USD", 2026, 1, 0m, Kursquelle.Bmf)));
        Assert.Empty(Ab.Kurse.ImportiereCsv("", "leer.csv").Uebernommen is 0 ? [] : new[] { "x" });
    }
}

public class BelegImportTests : Basis
{
    private const string Export = """
        Datum;Rechnung;Anbieter;Betrag;Währung;Text
        10.03.2026;FN-1001;Funded Next;99,00;USD;Challenge 100k
        11.03.2026;FN-1002;Funded Next;99,00;USD;Challenge 100k
        11.03.2026;FN-1002;Funded Next;99,00;USD;doppelt in Datei
        12.03.2026;XY-1;Unbekannt GmbH;10,00;USD;
        12.03.2026;FN-1003;Funded Next;abc;USD;
        """;

    private CsvProfil Profil() => new()
    {
        Trennzeichen = ';', Dezimalzeichen = ',', Datumsformat = "dd.MM.yyyy", BetriebId = PropTrading,
        Zuordnung = new()
        {
            [CsvFeld.Leistungsdatum] = "Datum", [CsvFeld.Belegnummer] = "Rechnung", [CsvFeld.Partner] = "Anbieter",
            [CsvFeld.Betrag] = "Betrag", [CsvFeld.Waehrung] = "Währung", [CsvFeld.Notiz] = "6",
        },
    };

    [Fact]
    public void Analyse_erkennt_Format()
    {
        var a = Ab.Import.Analysiere(Export);
        Assert.Equal(';', a.Trennzeichen);
        Assert.Equal(',', a.Dezimalzeichen);
        Assert.Equal("dd.MM.yyyy", a.Datumsformat);
        Assert.Equal("Anbieter", a.Spalten[2]);
        Assert.Equal(5, a.Beispielzeilen.Count);
    }

    [Fact]
    public void Vorschau_Import_Dubletten_und_Wiederholung()
    {
        var v = Ab.Import.Vorschau(Export, Profil());
        Assert.False(v.DateiBereitsImportiert);
        Assert.Equal(5, v.Zeilen.Count);
        Assert.True(v.Zeilen[0].Importierbar);
        Assert.Contains("bereits vorhanden", Assert.Single(v.Zeilen[2].Fehler));
        Assert.Contains("unbekannt", Assert.Single(v.Zeilen[3].Fehler));
        Assert.Contains("nicht lesbar", Assert.Single(v.Zeilen[4].Fehler));
        Assert.Equal("Challenge 100k", v.Zeilen[0].Daten!.Notiz);
        Assert.Equal(Richtung.Eingang, v.Zeilen[0].Daten!.Richtung);

        var r = Ab.Import.Importiere(Export, @"C:\Downloads\funded.csv", Profil(), profilSpeichernAls: "Funded Next Export");
        Assert.Equal((2, 0, 3), (r.Ok, r.Dubletten, r.Fehler));
        Assert.All(r.BelegIds, id => Assert.Equal(BelegStatus.Entwurf, Ab.Belege.Laden(id).Status));
        Assert.Empty(Ab.Belege.BucheAlle(r.BelegIds));

        Assert.True(Ab.Import.Vorschau(Export, Profil()).DateiBereitsImportiert);
        Assert.Throws<FachlicherFehler>(() => Ab.Import.Importiere(Export, "funded.csv", Profil()));
        var erneut = Ab.Import.Importiere(Export.Replace("FN-100", "FN-200"), "funded2.csv", Profil());
        Assert.Equal(2, erneut.Dubletten);
        Assert.Equal(0, erneut.Ok);

        Assert.Equal(["Funded Next Export"], Ab.Import.Profile());
        Assert.Equal("Anbieter", Ab.Import.LadeProfil("Funded Next Export")!.Zuordnung[CsvFeld.Partner]);
        Assert.Null(Ab.Import.LadeProfil("gibt es nicht"));
    }

    [Fact]
    public void Bankexport_englisch_ohne_Belegnummer_mit_Standardpartner_und_Typ_Spalte()
    {
        const string bank = "Date,Description,Amount,Type\n2026-05-15,Payout FN,\"-2,000.00\",Payout\n2026-05-16,Fee,10.00,Unsinn\n,leer,5.00,Payout\n2026-05-17,R,5.00,Erstattung\n";
        var p = new CsvProfil
        {
            Trennzeichen = ',', Dezimalzeichen = '.', Datumsformat = "yyyy-MM-dd", BetriebId = PropTrading, StandardWaehrung = "usd",
            StandardPartnerId = FundedNext, StandardTyp = Belegtyp.SonstigeAusgabe,
            Zuordnung = new() { [CsvFeld.Zahlungsdatum] = "Date", [CsvFeld.Betrag] = "Amount", [CsvFeld.Belegtyp] = "Type", [CsvFeld.Notiz] = "Description" },
        };
        var v = Ab.Import.Vorschau(bank, p);
        var payout = v.Zeilen[0].Daten!;
        Assert.Equal((Belegtyp.Payout, Richtung.Ausgang, 2000m, "USD"), (payout.Typ, payout.Richtung, payout.Betrag.Betrag, payout.Betrag.Waehrung));
        Assert.StartsWith("IMP0-", payout.Belegnummer);
        Assert.Contains("unbekannt", v.Zeilen[1].Fehler[0]);
        Assert.Contains("Kein Datum", v.Zeilen[2].Fehler[0]);
        Assert.Contains("Erstattungen", v.Zeilen[3].Fehler[0]);
    }

    [Fact]
    public void Profil_ohne_Kopfzeile_mit_Spaltennummern_und_Fehlerfaellen()
    {
        var p = new CsvProfil
        {
            HatKopfzeile = false, BetriebId = PropTrading,
            Zuordnung = new() { [CsvFeld.Leistungsdatum] = "1", [CsvFeld.Betrag] = "2", [CsvFeld.Partner] = "3", [CsvFeld.Waehrung] = "4" },
        };
        var v = Ab.Import.Vorschau("2026-03-01;10,00;Funded Next;USD\n01.03.2026;0;Funded Next;USD\n01.03.2026;;Funded Next;USDX\n01.03.2026;5;;USD\n", p);
        Assert.Contains("passt nicht", v.Zeilen[0].Fehler[0]);
        Assert.Contains("Betrag ist 0", v.Zeilen[1].Fehler[0]);
        Assert.Contains(v.Zeilen[2].Fehler, f => f.Contains("Kein Betrag"));
        Assert.Contains(v.Zeilen[2].Fehler, f => f.Contains("Währung"));
        Assert.Contains("Kein Partner", v.Zeilen[3].Fehler[0]);
        Assert.Throws<FachlicherFehler>(() => Ab.Import.SpeichereProfil(" ", p));
    }
}

public class DokumentTests : Basis
{
    private string Datei(string name, string inhalt)
    {
        var dir = Path.Combine(Ordner, "eingang");
        Directory.CreateDirectory(dir);
        var pfad = Path.Combine(dir, name);
        File.WriteAllText(pfad, inhalt, Encoding.UTF8);
        return pfad;
    }

    [Fact]
    public void Ablage_dedupliziert_und_prueft_Integritaet()
    {
        var a = Ab.Dokumente.Ablegen(Datei("rechnung.pdf", "%PDF-1 A"));
        var b = Ab.Dokumente.Ablegen(Datei("kopie.pdf", "%PDF-1 A"));
        Assert.True(a.Neu);
        Assert.False(b.Neu);
        Assert.Equal(a.Id, b.Id);
        Assert.StartsWith("belege/" + a.Sha256[..2] + "/", a.RelativerPfad);
        Assert.True(File.Exists(Ab.Dokumente.AbsoluterPfad(a)));
        Assert.Throws<FachlicherFehler>(() => Ab.Dokumente.Ablegen(Datei("x.docx", "x")));
        Assert.Empty(Ab.Dokumente.PruefeIntegritaet());

        File.AppendAllText(Ab.Dokumente.AbsoluterPfad(a), "manipuliert");
        Assert.Equal(a.Id, Assert.Single(Ab.Dokumente.PruefeIntegritaet()).Id);
    }

    [Fact]
    public void Ordnerimport_Vorschlaege_und_Zuordnung()
    {
        var c1 = Gebucht(Challenge("FN-1001"));
        var c2 = Gebucht(Challenge("C2", D(2026, 3, 11), 149m));
        Datei("Invoice_FN-1001.pdf", "1");
        Datei("2026-03-11_149.00_receipt.png", "2");
        Datei("11.03.2026 scan.jpg", "3");
        Datei("notizen.txt", "4");
        var docs = Ab.Dokumente.ImportiereOrdner(Path.Combine(Ordner, "eingang"));
        Assert.Equal(3, docs.Count);

        var v = Ab.Dokumente.Vorschlaege();
        Assert.Contains(v, x => x.BelegId == c1 && x.Punkte == 100 && x.Dokument.Dateiname.StartsWith("Invoice"));
        Assert.Contains(v, x => x.BelegId == c2 && x.Punkte == 60);
        Assert.Contains(v, x => x.BelegId == c2 && x.Punkte == 30);

        var rechnung = docs.Single(d => d.Dateiname.StartsWith("Invoice"));
        Ab.Dokumente.Zuordnen(c1, rechnung.Id);
        Ab.Dokumente.Zuordnen(c1, rechnung.Id);
        Assert.Equal(1, Ab.Belege.Laden(c1).AnzahlDokumente);
        Assert.Single(Ab.Dokumente.ZuBeleg(c1));
        Assert.Equal(2, Ab.Dokumente.Unzugeordnet().Count);
        Assert.Single(Ab.Protokoll.Zu("beleg", c1.ToString()), p => p.Aktion == "DOKUMENT");
        Assert.Throws<FachlicherFehler>(() => Ab.Dokumente.Zuordnen(999, rechnung.Id));
        Assert.Throws<FachlicherFehler>(() => Ab.Dokumente.Zuordnen(c1, 999));

        var entwurf = Ab.Belege.ErfasseEntwurf(Challenge("E1"));
        Ab.Dokumente.Zuordnen(entwurf, docs[1].Id);
        Assert.Throws<FachlicherFehler>(() => Ab.Belege.LoescheEntwurf(entwurf, "weg"));
    }
}
