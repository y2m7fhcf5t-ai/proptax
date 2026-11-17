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
    public string? Speichern { get; set; }
    public Task<string?> DateiSpeichern(string titel, string vorschlag, string endung) =>
        Task.FromResult(Speichern is null ? null : Path.Combine(Speichern, vorschlag));
    public List<string> Geoeffnet { get; } = [];
    public void Oeffnen(string pfad) => Geoeffnet.Add(pfad);
    public bool Beendet { get; private set; }
    public void Beenden() => Beendet = true;
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
        Assert.Equal(["Belege", "Import", "Stammdaten", "Kurse", "Perioden", "Protokoll", "Einstellungen", "Auswertungen"],
            tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        Assert.Contains("4 Partner ohne Land", vm.Status.Text);
        for (var i = 0; i < 8; i++) vm.Reiter = i; // jeder Reiter lädt ohne Fehler
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

    private void Testdaten()
    {
        _ab.Kurse.Setze(new KursDaten("USD", 2026, 4, 1.1000m, Kursquelle.Bmf));
        _ab.Kurse.Setze(new KursDaten("USD", 2026, 5, 1.0800m, Kursquelle.Bmf));
        var fn = _ab.Stammdaten.Partner().Single(p => p.Name == "Funded Next").Id;
        var pt = _ab.Stammdaten.Betriebe().Single(b => b.Name == "Prop-Trading").Id;
        void Buche(string nr, DateOnly d, decimal usd, Belegtyp typ = Belegtyp.ChallengeGebuehr)
        {
            var id = _ab.Belege.ErfasseEntwurf(new BelegDaten
            {
                Belegnummer = nr, BetriebId = pt, PartnerId = fn, Typ = typ, Richtung = BelegDaten.RichtungFuer(typ),
                Rechnungsdatum = d, Leistungsdatum = d, Zahlungsdatum = d, Betrag = new Domain.Werte.CurrencyAmount(usd, "USD"),
            });
            _ab.Belege.Buche(id);
        }
        Buche("FN-1001", new(2026, 3, 10), 99m);
        Buche("FN-1002", new(2026, 3, 12), 149m);
        Buche("FN-1003", new(2026, 4, 8), 99m);
        Buche("PO-1", new(2026, 5, 15), 2000m, Belegtyp.Payout);
    }

    [AvaloniaFact]
    public async Task Auswertungen_laden_exportieren_und_Screenshots()
    {
        Testdaten();
        var (fenster, vm) = Starte();
        var a = vm.Auswertungen;
        a.Jahr = 2026;
        a.Aktualisieren();
        Assert.False(vm.Status.IstFehler, vm.Status.Text);
        Assert.Equal(4, a.Zeitraeume.Count);
        Assert.Equal(["52", "53"], a.Kennzahlen.Select(k => k.Kz));
        a.KennzahlAuswahl = a.Kennzahlen[0];
        Assert.Equal(2, a.DrillBelege.Count);
        Assert.Contains("Zahllast 43,63", a.VaKopf);
        Assert.Equal(4, a.Fristen.Count);
        Assert.Contains("nein", a.Fristen[2].Pflicht);
        Assert.Equal(3, a.Nachweis.Count);
        Assert.Single(a.DashboardFirmen);
        Assert.Contains("Payouts 1.851,85", a.DashboardSumme);
        Assert.Contains("Ergebnis", a.EuerSumme);

        var ziel = Path.Combine(_ordner, "export");
        Directory.CreateDirectory(ziel);
        _dialoge.Speichern = ziel;
        await a.NachweisPdfCommand.ExecuteAsync(null);
        await a.NachweisXlsxCommand.ExecuteAsync(null);
        await a.NachweisZipCommand.ExecuteAsync(null);
        await a.VaXlsxCommand.ExecuteAsync(null);
        await a.EuerXlsxCommand.ExecuteAsync(null);
        await a.EuerCsvCommand.ExecuteAsync(null);
        Assert.Equal(6, Directory.GetFiles(ziel).Length);

        a.Begruendung = "laut Steuerberater";
        a.ZuordnungAuswahl = a.ZeilenZuordnung.First(z => z.Kategorie.StartsWith("Prop-Firmen"));
        a.AnlageZeile = "Zeile (Test)";
        a.ZeileSpeichern();
        Assert.Contains(a.EuerZeilen, z => z.Zeile == "Zeile (Test)");
        a.VorjahrEingabe = "12.000,00";
        a.VorjahrSpeichern();
        Assert.Contains("manuell", a.GrenzText);
        a.UmschaltungUebernehmen();
        Assert.True(vm.Status.IstFehler); // kein Vorschlag → Meldung

        vm.Reiter = 7;
        var screenshot = Environment.GetEnvironmentVariable("PROPTAX_SCREENSHOT_DIR");
        var innen = fenster.GetLogicalDescendants().OfType<AuswertungenView>().First().GetLogicalDescendants().OfType<TabControl>().First();
        foreach (var (index, name) in new[] { (0, "ustva"), (5, "dashboard") })
        {
            innen.SelectedIndex = index;
            if (index == 0) a.KennzahlAuswahl = a.Kennzahlen[0];
            Dispatch();
            var bild = fenster.CaptureRenderedFrame();
            Assert.NotNull(bild);
            if (!string.IsNullOrEmpty(screenshot)) bild!.Save(Path.Combine(screenshot, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task Sicherung_Verschluesselung_Exporte_und_Dokument_oeffnen()
    {
        Testdaten();
        var pdf = Path.Combine(_ordner, "rechnung.pdf");
        File.WriteAllText(pdf, "%PDF");
        var beleg = _ab.Belege.Liste().First(b => b.Daten.Belegnummer == "FN-1001").Id;
        _ab.Dokumente.Zuordnen(beleg, _ab.Dokumente.Ablegen(pdf).Id);
        var (fenster, vm) = Starte();

        // Dokument öffnen aus der Belegmaske
        vm.Belege.Auswahl = vm.Belege.Belege.First(b => b.Belegnummer == "FN-1001");
        var dok = Assert.Single(vm.Belege.Dokumente);
        vm.Belege.DokumentOeffnenCommand.Execute(dok);
        Assert.EndsWith(".pdf", Assert.Single(_dialoge.Geoeffnet));

        // Exporte Belegliste und Protokoll
        var ziel = Path.Combine(_ordner, "exp");
        Directory.CreateDirectory(ziel);
        _dialoge.Speichern = ziel;
        await vm.Belege.ListeXlsxCommand.ExecuteAsync(null);
        await vm.Belege.ListeCsvCommand.ExecuteAsync(null);
        await vm.Belege.ListePdfCommand.ExecuteAsync(null);
        await vm.Protokoll.ExportPdfCommand.ExecuteAsync(null);
        await vm.Protokoll.ExportXlsxCommand.ExecuteAsync(null);
        await vm.Protokoll.ExportCsvCommand.ExecuteAsync(null);
        await vm.Auswertungen.VaPdfCommand.ExecuteAsync(null);
        await vm.Auswertungen.EuerPdfCommand.ExecuteAsync(null);
        Assert.Equal(8, Directory.GetFiles(ziel).Length);

        // Sicherung + Verschlüsselung
        var e = vm.Einstellungen;
        e.JetztSichern();
        Assert.Single(e.Sicherungen);
        e.PwNeu = "kurz";
        e.PwWiederholung = "kurz";
        e.Verschluesseln();
        Assert.True(vm.Status.IstFehler);
        e.PwNeu = e.PwWiederholung = "ein-sicheres-passwort";
        e.Verschluesseln();
        Assert.False(vm.Status.IstFehler, vm.Status.Text);
        Assert.True(e.IstVerschluesselt);
        Assert.Contains("unverschlüsselt", vm.Status.Text);
        e.Begruendung = "nach Verschlüsselung";
        e.UnverschluesselteLoeschen();
        Assert.All(e.Sicherungen, s => Assert.Equal("ja", s.Verschluesselt));
        Assert.Contains("🔒", vm.Titel);
        vm.Reiter = 6;
        e.Aktualisieren();
        Dispatch();
        if (Environment.GetEnvironmentVariable("PROPTAX_SCREENSHOT_DIR") is { Length: > 0 } dir)
            fenster.CaptureRenderedFrame()!.Save(Path.Combine(dir, "einstellungen.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);

        // Auto-Sicherung beim Beenden genau einmal
        Assert.NotNull(vm.SicherungBeimBeenden());
        Assert.Null(vm.SicherungBeimBeenden());
        Assert.Single(_ab.Sicherungen.Liste(), s => s.Automatisch);

        // Wiederherstellen der ausgewählten Sicherung → App wird beendet
        e.Aktualisieren();
        e.SicherungAuswahl = e.Sicherungen.First();
        e.PwSicherung = "ein-sicheres-passwort";
        await e.WiederherstellenCommand.ExecuteAsync(null);
        Assert.True(_dialoge.Beendet, vm.Status.Text);
        Assert.True(Arbeitsbereich.BenoetigtPasswort(_ordner));
        _ = fenster;
    }

    [AvaloniaFact]
    public void Passwortfenster_drei_Versuche()
    {
        var versuche = 0;
        var w = new PasswortWindow(_ordner, pw => { versuche++; return pw == "richtig" ? null : "Das Passwort ist falsch."; });
        w.Show();
        var ok = w.FindControl<Button>("Ok")!;
        var feld = w.FindControl<TextBox>("Passwort")!;
        var meldung = w.FindControl<TextBlock>("Meldung")!;
        feld.Text = "falsch";
        ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("1/3", meldung.Text);
        feld.Text = "richtig";
        ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, versuche);
        Assert.Contains("1/3", meldung.Text);
        for (var i = 0; i < 2; i++) { feld.Text = "x"; ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        Assert.Contains("Zu viele Versuche", meldung.Text);
    }

    private static void Dispatch() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}
