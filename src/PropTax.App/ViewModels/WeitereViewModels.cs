using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropTax.Application;
using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.App.ViewModels;

public sealed partial class KurseViewModel : VmBasis
{
    private readonly IDialoge _dialoge;

    public KurseViewModel(Arbeitsbereich ab, Statusleiste status, IDialoge dialoge) : base(ab, status)
    {
        _dialoge = dialoge;
        Aktualisieren();
    }

    public ObservableCollection<KursDaten> Kurse { get; } = [];
    public Kursquelle[] Quellen { get; } = Enum.GetValues<Kursquelle>();
    [ObservableProperty] public partial string Waehrung { get; set; } = "USD";
    [ObservableProperty] public partial decimal Jahr { get; set; } = DateTime.Today.Year;
    [ObservableProperty] public partial decimal Monat { get; set; } = DateTime.Today.Month;
    [ObservableProperty] public partial string Kurs { get; set; } = "";
    [ObservableProperty] public partial Kursquelle Quelle { get; set; } = Kursquelle.Bmf;

    [RelayCommand]
    public void Aktualisieren() => BelegeViewModel.Ersetze(Kurse, Ab.Kurse.Liste().OrderByDescending(k => k.Jahr).ThenByDescending(k => k.Monat).ThenBy(k => k.Waehrung));

    [RelayCommand]
    public void Setzen() => Versuche(() =>
    {
        Ab.Kurse.Setze(new KursDaten(Waehrung.Trim().ToUpperInvariant(), (int)Jahr, (int)Monat, Dezimal(Kurs, "Kurs"), Quelle));
        Aktualisieren();
    }, $"Kurs {Waehrung} {Monat:00}/{Jahr} gespeichert. Gebuchte Belege ggf. unter Einstellungen neu berechnen.");

    [RelayCommand]
    public async Task CsvImportieren()
    {
        var pfad = await _dialoge.DateiOeffnen("Kurstabelle (CSV) wählen", "csv", "txt");
        if (pfad is null) return;
        Versuche(() =>
        {
            var r = Ab.Kurse.ImportiereCsv(File.ReadAllText(pfad), pfad, Quelle);
            Aktualisieren();
            if (r.Fehler.Count > 0) throw new FachlicherFehler($"{r.Uebernommen} Kurse übernommen, {r.Fehler.Count} Fehler: {r.Fehler[0]}");
            Status.Ok($"{r.Uebernommen} Kurse übernommen.");
        });
    }
}

public sealed record PeriodeZeileVm(string Bezeichnung, string Schluessel, bool Festgeschrieben, string Info);

public sealed partial class PeriodenViewModel : VmBasis
{
    public PeriodenViewModel(Arbeitsbereich ab, Statusleiste status) : base(ab, status) => Aktualisieren();

    public ObservableCollection<PeriodeZeileVm> Zeilen { get; } = [];
    [ObservableProperty] public partial decimal Jahr { get; set; } = DateTime.Today.Year;
    [ObservableProperty] public partial string Begruendung { get; set; } = "";

    partial void OnJahrChanged(decimal value) => Aktualisieren();

    [RelayCommand]
    public void Aktualisieren()
    {
        var fest = Ab.Perioden.Festgeschriebene();
        var j = (int)Jahr;
        var zeilen = Enumerable.Range(1, 4).Select(q =>
        {
            var z = UstZeitraum.Quartal(j, q);
            var p = fest.FirstOrDefault(x => x.Art == "UST" && x.Schluessel == z.ToString());
            return new PeriodeZeileVm($"USt {q}. Quartal {j}", z.ToString(), p is not null, p is null ? "offen" : $"festgeschrieben {p.Am?[..10]} von {p.Von}");
        }).ToList();
        var e = fest.FirstOrDefault(x => x.Art == "EUER" && x.Schluessel == j.ToString());
        zeilen.Add(new PeriodeZeileVm($"EÜR {j}", j.ToString(), e is not null, e is null ? "offen" : $"festgeschrieben {e.Am?[..10]} von {e.Von}"));
        BelegeViewModel.Ersetze(Zeilen, zeilen);
    }

    [RelayCommand]
    public void Festschreiben(PeriodeZeileVm? zeile)
    {
        if (zeile is null || zeile.Festgeschrieben) return;
        Versuche(() =>
        {
            if (zeile.Bezeichnung.StartsWith("EÜR", StringComparison.Ordinal)) Ab.Perioden.FestschreibenEuer((int)Jahr, Begruendung);
            else Ab.Perioden.FestschreibenUst(UstZeitraum.Quartal((int)Jahr, zeile.Schluessel[^1] - '0'), Begruendung);
            Aktualisieren();
        }, $"{zeile.Bezeichnung} festgeschrieben. Änderungen sind nur noch per Storno/Korrektur möglich.");
    }
}

public sealed partial class ProtokollViewModel : VmBasis
{
    public ProtokollViewModel(Arbeitsbereich ab, Statusleiste status) : base(ab, status) => Aktualisieren();

    public ObservableCollection<ProtokollEintrag> Eintraege { get; } = [];
    [ObservableProperty] public partial string Kettenstatus { get; set; } = "";
    [ObservableProperty] public partial bool KetteOk { get; set; }

    [RelayCommand]
    public void Aktualisieren()
    {
        BelegeViewModel.Ersetze(Eintraege, Ab.Protokoll.Alle().Reverse());
        KetteOk = Ab.Protokoll.PruefeKette();
        Kettenstatus = KetteOk
            ? $"✓ Hash-Kette intakt ({Eintraege.Count} Einträge)"
            : "✗ Hash-Kette verletzt – Protokoll wurde außerhalb der Anwendung verändert!";
    }
}

public sealed partial class EinstellungenViewModel : VmBasis
{
    public EinstellungenViewModel(Arbeitsbereich ab, Statusleiste status) : base(ab, status) => Aktualisieren();

    public string Datenordner => Ab.Datenordner;
    public ObservableCollection<BesteuerungsartAb> Historie { get; } = [];
    public Besteuerungsart[] Arten { get; } = Enum.GetValues<Besteuerungsart>();
    public string[] PayoutOptionen { get; } = EinstellungenService.PayoutOptionen;

    [ObservableProperty] public partial DateTime? Ab_ { get; set; } = new DateTime(DateTime.Today.Year, 1, 1);
    [ObservableProperty] public partial Besteuerungsart Art { get; set; } = Besteuerungsart.Kleinunternehmer;
    [ObservableProperty] public partial string Payout { get; set; } = "NICHT_STEUERBAR";
    [ObservableProperty] public partial string Begruendung { get; set; } = "";
    [ObservableProperty] public partial string AktuellerStand { get; set; } = "";

    [RelayCommand]
    public void Aktualisieren()
    {
        BelegeViewModel.Ersetze(Historie, Ab.Einstellungen.BesteuerungsartHistorie());
        var heute = DateOnly.FromDateTime(DateTime.Today);
        Payout = Ab.Einstellungen.Wert(EinstellungenService.PayoutBehandlung, heute);
        AktuellerStand = $"Heute: {Anzeige.Text(Ab.Einstellungen.Regelwerk().Besteuerungsart(heute))}, " +
            $"VA-Zeitraum {Ab.Einstellungen.Wert("ust.voranmeldungszeitraum", heute)}, Payouts {Payout}";
    }

    [RelayCommand]
    public void BesteuerungsartSetzen() => Versuche(() =>
    {
        Ab.Einstellungen.SetzeBesteuerungsart(Datum(Ab_, "Stichtag"), Art, Begruendung);
        Aktualisieren();
    }, "Besteuerungsart gespeichert. Offene Belege ggf. neu berechnen.");

    [RelayCommand]
    public void PayoutSetzen() => Versuche(() =>
    {
        Ab.Einstellungen.SetzeEinstellung(EinstellungenService.PayoutBehandlung, Payout, Datum(Ab_, "Stichtag"), Begruendung);
        Aktualisieren();
    }, "Payout-Behandlung gespeichert.");

    [RelayCommand]
    public void NeuBerechnen() => Versuche(() =>
    {
        var n = Ab.Belege.NeuBerechnen(string.IsNullOrWhiteSpace(Begruendung) ? "Neuberechnung nach Stammdatenänderung" : Begruendung);
        Status.Ok($"{n} Beleg(e) in offenen Perioden neu berechnet.");
    });
}

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(Arbeitsbereich ab, IDialoge dialoge)
    {
        Arbeitsbereich = ab;
        Status = new Statusleiste();
        Belege = new BelegeViewModel(ab, Status);
        Import = new ImportViewModel(ab, Status, dialoge);
        Stammdaten = new StammdatenViewModel(ab, Status);
        Kurse = new KurseViewModel(ab, Status, dialoge);
        Perioden = new PeriodenViewModel(ab, Status);
        Protokoll = new ProtokollViewModel(ab, Status);
        Einstellungen = new EinstellungenViewModel(ab, Status);
        Auswertungen = new AuswertungenViewModel(ab, Status, dialoge);
        var ohneLand = ab.Stammdaten.Partner().Count(p => p.Aktiv && p.Kategorie is null);
        Status.Ok(ohneLand > 0
            ? $"Datenordner {ab.Datenordner}. Hinweis: {ohneLand} Partner ohne Land/Kategorie – unter Stammdaten ergänzen."
            : $"Datenordner {ab.Datenordner}.");
    }

    public Arbeitsbereich Arbeitsbereich { get; }
    public Statusleiste Status { get; }
    public BelegeViewModel Belege { get; }
    public ImportViewModel Import { get; }
    public StammdatenViewModel Stammdaten { get; }
    public KurseViewModel Kurse { get; }
    public PeriodenViewModel Perioden { get; }
    public ProtokollViewModel Protokoll { get; }
    public EinstellungenViewModel Einstellungen { get; }
    public AuswertungenViewModel Auswertungen { get; }
    public string Titel => $"PropTax – {Arbeitsbereich.Datenordner}";

    [ObservableProperty] public partial int Reiter { get; set; }

    /// <summary>Beim Reiterwechsel Daten neu laden (Änderungen aus anderen Reitern übernehmen).</summary>
    partial void OnReiterChanged(int value)
    {
        switch (value)
        {
            case 0: Belege.Aktualisieren(); break;
            case 1: Import.LadeStammdaten(); break;
            case 2: Stammdaten.Aktualisieren(); break;
            case 3: Kurse.Aktualisieren(); break;
            case 4: Perioden.Aktualisieren(); break;
            case 5: Protokoll.Aktualisieren(); break;
            case 6: Einstellungen.Aktualisieren(); break;
            case 7: Auswertungen.Aktualisieren(); break;
        }
    }
}
