using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropTax.Application;
using PropTax.Domain;

namespace PropTax.App.ViewModels;

public sealed partial class SpaltenZuordnungVm(CsvFeld feld, IReadOnlyList<string> spalten) : ObservableObject
{
    public const string Keine = "(keine)";
    public CsvFeld Feld { get; } = feld;
    public string FeldName => Feld.ToString();
    public IReadOnlyList<string> Spalten { get; } = [Keine, .. spalten];
    [ObservableProperty] public partial string Spalte { get; set; } = Keine;
}

public sealed record ImportZeileVm(int Zeile, string Belegnummer, string Datum, string Betrag, string Typ, string Status);

public sealed record VorschlagVm(Zuordnungsvorschlag V)
{
    public string Dokument => V.Dokument.Dateiname;
    public string Beleg => V.Belegnummer;
    public string Grund => $"{V.Grund} ({V.Punkte})";
}

public sealed partial class ImportViewModel : VmBasis
{
    private readonly IDialoge _dialoge;

    public ImportViewModel(Arbeitsbereich ab, Statusleiste status, IDialoge dialoge) : base(ab, status)
    {
        _dialoge = dialoge;
        Ersetze(Betriebe, ab.Stammdaten.Betriebe());
        Betrieb = Betriebe.FirstOrDefault();
        LadeStammdaten();
    }

    public ObservableCollection<BetriebDaten> Betriebe { get; } = [];
    public ObservableCollection<PartnerDaten> Partner { get; } = [];
    public ObservableCollection<EuerKategorieDaten> EuerKategorien { get; } = [];
    public ObservableCollection<string> Profile { get; } = [];
    public ObservableCollection<SpaltenZuordnungVm> Zuordnungen { get; } = [];
    public ObservableCollection<ImportZeileVm> Vorschau { get; } = [];
    public ObservableCollection<VorschlagVm> Vorschlaege { get; } = [];
    public ObservableCollection<DokumentInfo> OffeneDokumente { get; } = [];
    public Belegtyp[] Belegtypen { get; } = Enum.GetValues<Belegtyp>().Where(t => t != Belegtyp.Erstattung).ToArray();

    [ObservableProperty] public partial string? Datei { get; set; }
    [ObservableProperty] public partial string CsvText { get; set; } = "";
    [ObservableProperty] public partial string Trennzeichen { get; set; } = ";";
    [ObservableProperty] public partial string Dezimalzeichen { get; set; } = ",";
    [ObservableProperty] public partial string Datumsformat { get; set; } = "dd.MM.yyyy";
    [ObservableProperty] public partial bool HatKopfzeile { get; set; } = true;
    [ObservableProperty] public partial BetriebDaten? Betrieb { get; set; }
    [ObservableProperty] public partial Belegtyp StandardTyp { get; set; } = Belegtyp.ChallengeGebuehr;
    [ObservableProperty] public partial PartnerDaten? StandardPartner { get; set; }
    [ObservableProperty] public partial string StandardWaehrung { get; set; } = "USD";
    [ObservableProperty] public partial EuerKategorieDaten? EuerKategorie { get; set; }
    [ObservableProperty] public partial bool DublettenUebernehmen { get; set; }
    [ObservableProperty] public partial bool DateiErneut { get; set; }
    [ObservableProperty] public partial string ProfilName { get; set; } = "";
    [ObservableProperty] public partial string? ProfilAuswahl { get; set; }
    [ObservableProperty] public partial string Zusammenfassung { get; set; } = "";
    [ObservableProperty] public partial DokumentInfo? DokumentAuswahl { get; set; }
    [ObservableProperty] public partial string ZielBelegnummer { get; set; } = "";

    public void LadeStammdaten()
    {
        Ersetze(Partner, Ab.Stammdaten.Partner().Where(p => p.Aktiv));
        Ersetze(EuerKategorien, Ab.Stammdaten.EuerKategorien());
        Ersetze(Profile, Ab.Import.Profile());
        Ersetze(OffeneDokumente, Ab.Dokumente.Unzugeordnet());
    }

    [RelayCommand]
    public async Task DateiWaehlen()
    {
        var pfad = await _dialoge.DateiOeffnen("CSV-Datei wählen", "csv", "txt");
        if (pfad is null) return;
        Versuche(() => LadeText(File.ReadAllText(pfad), pfad), $"{Path.GetFileName(pfad)} geladen.");
    }

    /// <summary>Lädt CSV-Text, erkennt das Format und schlägt eine Spaltenzuordnung vor.</summary>
    public void LadeText(string text, string datei)
    {
        Datei = datei;
        CsvText = text;
        var a = Ab.Import.Analysiere(text);
        Trennzeichen = a.Trennzeichen == '\t' ? "\\t" : a.Trennzeichen.ToString();
        Dezimalzeichen = a.Dezimalzeichen.ToString();
        if (a.Datumsformat is not null) Datumsformat = a.Datumsformat;
        Ersetze(Zuordnungen, Enum.GetValues<CsvFeld>().Select(f => new SpaltenZuordnungVm(f, a.Spalten) { Spalte = Rate(f, a.Spalten) }));
        VorschauAktualisieren();
    }

    [RelayCommand]
    public void ProfilLaden() => Versuche(() =>
    {
        var p = Ab.Import.LadeProfil(ProfilAuswahl ?? throw new FachlicherFehler("Kein Profil gewählt.")) ?? throw new FachlicherFehler("Profil nicht gefunden.");
        Trennzeichen = p.Trennzeichen == '\t' ? "\\t" : p.Trennzeichen.ToString();
        Dezimalzeichen = p.Dezimalzeichen.ToString();
        Datumsformat = p.Datumsformat;
        HatKopfzeile = p.HatKopfzeile;
        Betrieb = Betriebe.FirstOrDefault(b => b.Id == p.BetriebId) ?? Betrieb;
        StandardTyp = p.StandardTyp;
        StandardWaehrung = p.StandardWaehrung;
        StandardPartner = Partner.FirstOrDefault(x => x.Id == p.StandardPartnerId);
        EuerKategorie = EuerKategorien.FirstOrDefault(k => k.Id == p.EuerKategorieId);
        foreach (var z in Zuordnungen) z.Spalte = p.Zuordnung.TryGetValue(z.Feld, out var s) && z.Spalten.Contains(s) ? s : SpaltenZuordnungVm.Keine;
        ProfilName = ProfilAuswahl!;
        VorschauAktualisieren();
    }, $"Profil „{ProfilAuswahl}“ geladen.");

    [RelayCommand]
    public void VorschauAktualisieren() => Versuche(() =>
    {
        if (string.IsNullOrEmpty(CsvText)) return;
        var v = Ab.Import.Vorschau(CsvText, Profil());
        Ersetze(Vorschau, v.Zeilen.Select(z => new ImportZeileVm(z.Zeile, z.Daten?.Belegnummer ?? "", z.Daten?.Leistungsdatum.ToString("dd.MM.yyyy") ?? "",
            z.Daten is null ? "" : $"{Geld(z.Daten.Betrag.Betrag)} {z.Daten.Betrag.Waehrung}", z.Daten is null ? "" : Anzeige.Text(z.Daten.Typ),
            z.Fehler.Count > 0 ? "✗ " + string.Join("; ", z.Fehler) : z.IstDublette ? "⚠ " + z.Hinweis : "✓")));
        Zusammenfassung = $"{v.Zeilen.Count(z => z.Importierbar && !z.IstDublette)} importierbar, {v.Zeilen.Count(z => z.IstDublette)} mögliche Dubletten, " +
            $"{v.Zeilen.Count(z => !z.Importierbar)} fehlerhaft" + (v.DateiBereitsImportiert ? " – Datei wurde bereits importiert!" : "");
    });

    [RelayCommand]
    public void Importieren() => Versuche(() =>
    {
        var r = Ab.Import.Importiere(CsvText, Datei ?? "import.csv", Profil(), DublettenUebernehmen, DateiErneut,
            string.IsNullOrWhiteSpace(ProfilName) ? null : ProfilName);
        Ersetze(Profile, Ab.Import.Profile());
        Status.Ok($"Import {r.ImportLaufId}: {r.Ok} Entwürfe angelegt, {r.Dubletten} Dubletten übersprungen, {r.Fehler} fehlerhaft. Buchen im Reiter „Belege“.");
        VorschauAktualisieren();
    });

    [RelayCommand]
    public async Task OrdnerImportieren()
    {
        var ordner = await _dialoge.OrdnerWaehlen("Ordner mit Belegen (PDF/Bilder) wählen");
        if (ordner is null) return;
        Versuche(() =>
        {
            var docs = Ab.Dokumente.ImportiereOrdner(ordner);
            VorschlaegeAktualisieren();
            Status.Ok($"{docs.Count(d => d.Neu)} neue Dokumente abgelegt, {docs.Count(d => !d.Neu)} bereits vorhanden.");
        });
    }

    [RelayCommand]
    public async Task DokumentHinzufuegen()
    {
        var pfad = await _dialoge.DateiOeffnen("Beleg (PDF/Bild) wählen", "pdf", "png", "jpg", "jpeg", "xml");
        if (pfad is null) return;
        Versuche(() => { Ab.Dokumente.Ablegen(pfad); VorschlaegeAktualisieren(); }, "Dokument abgelegt.");
    }

    [RelayCommand]
    public void VorschlaegeAktualisieren()
    {
        Ersetze(Vorschlaege, Ab.Dokumente.Vorschlaege().Select(v => new VorschlagVm(v)));
        Ersetze(OffeneDokumente, Ab.Dokumente.Unzugeordnet());
    }

    [RelayCommand]
    public void VorschlagUebernehmen(VorschlagVm? v)
    {
        if (v is null) return;
        Versuche(() => { Ab.Dokumente.Zuordnen(v.V.BelegId, v.V.Dokument.Id); VorschlaegeAktualisieren(); }, $"{v.Dokument} → {v.Beleg} zugeordnet.");
    }

    [RelayCommand]
    public void ManuellZuordnen() => Versuche(() =>
    {
        var d = DokumentAuswahl ?? throw new FachlicherFehler("Kein Dokument gewählt.");
        var b = Ab.Belege.Liste(new BelegFilter { Suchtext = ZielBelegnummer })
            .FirstOrDefault(x => string.Equals(x.Daten.Belegnummer, ZielBelegnummer.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new FachlicherFehler($"Beleg „{ZielBelegnummer}“ nicht gefunden.");
        Ab.Dokumente.Zuordnen(b.Id, d.Id);
        VorschlaegeAktualisieren();
    }, "Dokument zugeordnet.");

    internal CsvProfil Profil() => new()
    {
        Trennzeichen = Trennzeichen == "\\t" ? '\t' : Trennzeichen.FirstOrDefault(';'),
        Dezimalzeichen = Dezimalzeichen.FirstOrDefault(','),
        Datumsformat = Datumsformat,
        HatKopfzeile = HatKopfzeile,
        Zuordnung = Zuordnungen.Where(z => z.Spalte != SpaltenZuordnungVm.Keine).ToDictionary(z => z.Feld, z => z.Spalte),
        BetriebId = Betrieb?.Id ?? throw new FachlicherFehler("Betrieb fehlt."),
        StandardTyp = StandardTyp,
        StandardWaehrung = StandardWaehrung,
        StandardPartnerId = StandardPartner?.Id,
        EuerKategorieId = EuerKategorie?.Id,
    };

    private static readonly Dictionary<CsvFeld, string[]> Hinweise = new()
    {
        [CsvFeld.Belegnummer] = ["rechnung", "invoice", "beleg", "nr", "number", "id"],
        [CsvFeld.Leistungsdatum] = ["datum", "date", "leistung"],
        [CsvFeld.Rechnungsdatum] = ["rechnungsdatum", "invoice date"],
        [CsvFeld.Zahlungsdatum] = ["zahlung", "payment", "valuta", "buchungstag"],
        [CsvFeld.Betrag] = ["betrag", "amount", "summe", "total", "preis"],
        [CsvFeld.Waehrung] = ["währung", "waehrung", "currency", "cur"],
        [CsvFeld.Partner] = ["partner", "anbieter", "firma", "payee", "merchant", "empfänger", "name"],
        [CsvFeld.Belegtyp] = ["typ", "type", "art"],
        [CsvFeld.Notiz] = ["text", "beschreibung", "description", "verwendungszweck", "notiz", "memo"],
    };

    private static string Rate(CsvFeld feld, IReadOnlyList<string> spalten)
    {
        var exakt = spalten.FirstOrDefault(s => Hinweise[feld].Any(h => string.Equals(s, h, StringComparison.OrdinalIgnoreCase)));
        if (exakt is not null) return exakt;
        if (feld == CsvFeld.Rechnungsdatum) return SpaltenZuordnungVm.Keine;
        return spalten.FirstOrDefault(s => Hinweise[feld].Any(h => s.Contains(h, StringComparison.OrdinalIgnoreCase))) ?? SpaltenZuordnungVm.Keine;
    }

    private static void Ersetze<T>(ObservableCollection<T> ziel, IEnumerable<T> quelle) => BelegeViewModel.Ersetze(ziel, quelle);
}
