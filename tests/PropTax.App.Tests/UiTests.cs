using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using PropTax.App.ViewModels;
using PropTax.App.Views;
using PropTax.Application;
using PropTax.Domain;

[assembly: AvaloniaTestApplication(typeof(PropTax.App.Tests.TestApp))]

namespace PropTax.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PropTax.App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class KeineDialoge : IDialoge
{
    public string? Datei { get; set; }
    public string? Ordner { get; set; }
    public Task<string?> DateiOeffnen(string titel, params string[] endungen) => Task.FromResult(Datei);
    public Task<string?> OrdnerWaehlen(string titel) => Task.FromResult(Ordner);
}

public sealed class UiTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "proptax-ui-" + Guid.NewGuid().ToString("N"));
    private readonly Arbeitsbereich _ab;
    private readonly KeineDialoge _dialoge = new();

    public UiTests()
    {
        _ab = Arbeitsbereich.Oeffne(_ordner, "UI-Test");
        var fn = _ab.Stammdaten.Partner().Single(p => p.Name == "Funded Next");
        _ab.Stammdaten.SpeicherePartner(fn with { LandIso = "AE" });
        _ab.Kurse.Setze(new KursDaten("USD", 2026, 3, 1.0800m, Kursquelle.Bmf));
    }

    public void Dispose()
    {
        try { Directory.Delete(_ordner, true); } catch (IOException) { }
    }

    private (MainWindow Fenster, MainViewModel Vm) Starte()
    {
        var fenster = new MainWindow { Width = 1400, Height = 860 };
        var vm = new MainViewModel(_ab, _dialoge);
        fenster.DataContext = vm;
        fenster.Show();
        return (fenster, vm);
    }

    [AvaloniaFact]
    public void Hauptfenster_startet_mit_allen_Reitern_und_Hinweis_auf_Partner_ohne_Land()
    {
        var (fenster, vm) = Starte();
        var tabs = fenster.GetLogicalDescendants().OfType<TabControl>().First();
        Assert.Equal(["Belege", "Import", "Stammdaten", "Kurse", "Perioden", "Protokoll", "Einstellungen"],
            tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Contains("4 Partner ohne Land", vm.Status.Text);
        for (var i = 0; i < 7; i++) vm.Reiter = i; // jeder Reiter lädt ohne Fehler
        Assert.False(vm.Status.IstFehler);
    }

    [AvaloniaFact]
    public void Beleg_erfassen_berechnen_buchen_und_stornieren()
    {
        var (_, vm) = Starte();
        var b = vm.Belege;
        b.Neu();
        b.PartnerAuswahl = b.Partner.Single(p => p.Name == "Funded Next");
        b.Typ = Belegtyp.ChallengeGebuehr;
        b.Rechnungsdatum = b.Leistungsdatum = b.Zahlungsdatum = new DateTime(2026, 3, 10);
        b.Betrag = "99,00";
        b.Waehrung = "USD";

        b.Berechnen();
        Assert.Contains("Kz 52 = 91,67", b.Vorschau);
        Assert.Contains("Zahllast 17,42", b.Vorschau);

        b.Buchen();
        Assert.False(vm.Status.IstFehler, vm.Status.Text);
        Assert.True(b.IstGebucht);
        var zeile = Assert.Single(b.Belege);
        Assert.Equal("17,42", zeile.Steuer);
        Assert.Equal("2026-Q1", zeile.Zeitraum);

        b.Begruendung = "";
        b.Stornieren();
        Assert.True(vm.Status.IstFehler); // Begründung fehlt → Meldung statt Absturz
        b.Begruendung = "Fehlbuchung";
        b.Stornodatum = new DateTime(2026, 4, 2);
        b.Stornieren();
        Assert.False(vm.Status.IstFehler, vm.Status.Text);
        Assert.Equal(2, b.Belege.Count);
        Assert.Contains(b.Belege, z => z.Status == "Storno" && z.Steuer == "-17,42");
    }

    [AvaloniaFact]
    public void Ungueltige_Eingaben_erscheinen_als_Meldung()
    {
        var (_, vm) = Starte();
        vm.Belege.Neu();
        vm.Belege.Betrag = "abc";
        vm.Belege.Buchen();
        Assert.True(vm.Status.IstFehler);
        vm.Belege.PartnerAuswahl = vm.Belege.Partner.Single(p => p.Name == "IQ Capital");
        vm.Belege.Betrag = "50";
        vm.Belege.Buchen();
        Assert.Contains("keine Kategorie", vm.Status.Text);
    }

    [AvaloniaFact]
    public void CSV_Import_ueber_Oberflaeche_mit_automatischer_Spaltenzuordnung()
    {
        var (_, vm) = Starte();
        var csv = Path.Combine(_ordner, "export.csv");
        File.WriteAllText(csv, "Datum;Rechnung;Anbieter;Betrag;Währung\n10.03.2026;FN-1;Funded Next;99,00;USD\n11.03.2026;FN-2;Funded Next;149,00;USD\n");
        _dialoge.Datei = csv;
        vm.Import.DateiWaehlenCommand.Execute(null);
        Assert.Equal("Rechnung", vm.Import.Zuordnungen.Single(z => z.Feld == CsvFeld.Belegnummer).Spalte);
        Assert.Equal("Anbieter", vm.Import.Zuordnungen.Single(z => z.Feld == CsvFeld.Partner).Spalte);
        Assert.Equal(2, vm.Import.Vorschau.Count(z => z.Status == "✓"));
        vm.Import.ProfilName = "Funded Next";
        vm.Import.Importieren();
        Assert.Contains("2 Entwürfe", vm.Status.Text);
        vm.Belege.Aktualisieren();
        Assert.Equal(2, vm.Belege.Belege.Count);
        vm.Belege.BucheMarkierte(vm.Belege.Belege);
        Assert.All(vm.Belege.Belege, z => Assert.Equal("Gebucht", z.Status));
    }

    [AvaloniaFact]
    public void Stammdaten_Perioden_Einstellungen_Protokoll()
    {
        var (_, vm) = Starte();
        var s = vm.Stammdaten;
        s.PartnerAuswahl = s.Partner.Single(p => p.Name == "Tradifei");
        s.Land = "US";
        s.PartnerSpeichern();
        Assert.Equal(PartnerKategorie.Drittland, s.Partner.Single(p => p.Name == "Tradifei").Kategorie);

        vm.Perioden.Jahr = 2026;
        vm.Perioden.Begruendung = "VA Q1 abgegeben";
        vm.Perioden.Festschreiben(vm.Perioden.Zeilen[0]);
        Assert.True(vm.Perioden.Zeilen[0].Festgeschrieben);

        vm.Einstellungen.Begruendung = "Klärung Steuerberater";
        vm.Einstellungen.Payout = "STEUERBAR";
        vm.Einstellungen.Ab_ = new DateTime(2026, 7, 1);
        vm.Einstellungen.PayoutSetzen();
        Assert.False(vm.Status.IstFehler, vm.Status.Text);

        vm.Kurse.Waehrung = "GBP"; vm.Kurse.Jahr = 2026; vm.Kurse.Monat = 3; vm.Kurse.Kurs = "0,8512";
        vm.Kurse.Setzen();
        Assert.Contains(vm.Kurse.Kurse, k => k.Waehrung == "GBP");

        vm.Protokoll.Aktualisieren();
        Assert.True(vm.Protokoll.KetteOk);
        Assert.Contains(vm.Protokoll.Eintraege, e => e.Aktion == "FESTSCHREIBUNG");
    }

    [AvaloniaFact]
    public void Screenshot_Belegmaske()
    {
        var (fenster, vm) = Starte();
        var b = vm.Belege;
        foreach (var (nr, tag, usd) in new[] { ("FN-1001", 10, 99m), ("FN-1002", 12, 149m), ("FN-1003", 20, 99m) })
        {
            var id = _ab.Belege.ErfasseEntwurf(new BelegDaten
            {
                Belegnummer = nr, BetriebId = b.Betriebe.First(x => x.Name == "Prop-Trading").Id, PartnerId = b.Partner.Single(p => p.Name == "Funded Next").Id,
                Typ = Belegtyp.ChallengeGebuehr, Richtung = Richtung.Eingang, Rechnungsdatum = new(2026, 3, tag), Leistungsdatum = new(2026, 3, tag),
                Zahlungsdatum = new(2026, 3, tag), Betrag = new Domain.Werte.CurrencyAmount(usd, "USD"),
            });
            if (nr != "FN-1003") _ab.Belege.Buche(id);
        }
        b.Aktualisieren();
        b.Auswahl = b.Belege.First(z => z.Belegnummer == "FN-1002");
        Dispatch();
        var bild = fenster.CaptureRenderedFrame();
        Assert.NotNull(bild);
        var ziel = Environment.GetEnvironmentVariable("PROPTAX_SCREENSHOT");
        if (!string.IsNullOrEmpty(ziel)) bild!.Save(ziel, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Dispatch() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}
