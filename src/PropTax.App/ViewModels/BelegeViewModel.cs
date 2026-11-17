using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropTax.Application;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.App.ViewModels;

public sealed class BelegZeileVm(BelegAnsicht a)
{
    public BelegAnsicht Ansicht { get; } = a;
    public long Id => Ansicht.Id;
    public string Belegnummer => Ansicht.Daten.Belegnummer;
    public string Datum => Ansicht.Daten.Leistungsdatum.ToString("dd.MM.yyyy");
    public string Typ => Anzeige.Text(Ansicht.Daten.Typ);
    public string Partner => Ansicht.PartnerName;
    public string Betrieb => Ansicht.BetriebName;
    public string Betrag => $"{VmBasis.Geld(Ansicht.Daten.Betrag.Betrag)} {Ansicht.Daten.Betrag.Waehrung}";
    public string Bmg => Ansicht.Steuer is null ? "" : VmBasis.Geld(Ansicht.Steuer.Bemessungsgrundlage.Betrag);
    public string Steuer => Ansicht.Steuer is null ? "" : VmBasis.Geld(Ansicht.Steuer.Steuer.Betrag);
    public string Vorsteuer => Ansicht.Steuer is null ? "" : VmBasis.Geld(Ansicht.Steuer.Vorsteuer.Betrag);
    public string Zeitraum => Ansicht.Steuer?.Zeitraum.ToString() ?? "";
    public string Status => Ansicht.StornoVonId is not null ? "Storno" : Ansicht.IstStorniert ? "Storniert" : Ansicht.Status.ToString();
    public string Hinweise => string.Join(" ", new[]
    {
        Ansicht.MoeglicheDublette ? "⚠ Dublette?" : null,
        Ansicht.PartnerKategorie is null ? "⚠ Partner ohne Land" : null,
        Ansicht.Steuer?.IstManuell == true ? "✎ manuell" : null,
        Ansicht.AnzahlDokumente > 0 ? $"📎{Ansicht.AnzahlDokumente}" : null,
    }.Where(x => x is not null));
}

public sealed partial class BelegeViewModel : VmBasis
{
    public BelegeViewModel(Arbeitsbereich ab, Statusleiste status) : base(ab, status)
    {
        Aktualisieren();
        Neu();
    }

    public ObservableCollection<BelegZeileVm> Belege { get; } = [];
    public ObservableCollection<BetriebDaten> Betriebe { get; } = [];
    public ObservableCollection<PartnerDaten> Partner { get; } = [];
    public ObservableCollection<EuerKategorieDaten> EuerKategorien { get; } = [];
    public ObservableCollection<VorlageDaten> Vorlagen { get; } = [];
    public ObservableCollection<BelegZeileVm> BuchbareBezuege { get; } = [];
    public Belegtyp[] Belegtypen { get; } = Enum.GetValues<Belegtyp>();
    public Richtung[] Richtungen { get; } = Enum.GetValues<Richtung>();
    public Steuerschuldner?[] Steuerschuldner_ { get; } = [null, .. Enum.GetValues<Steuerschuldner>().Cast<Steuerschuldner?>()];
    public string[] StatusFilterWerte { get; } = ["Alle", "Entwurf", "Gebucht"];

    // Filter
    [ObservableProperty] public partial string Suchtext { get; set; } = "";
    [ObservableProperty] public partial string StatusFilter { get; set; } = "Alle";
    [ObservableProperty] public partial BelegZeileVm? Auswahl { get; set; }

    // Editor
    [ObservableProperty] public partial long? BearbeiteterId { get; set; }
    [ObservableProperty] public partial string EditorTitel { get; set; } = "";
    [ObservableProperty] public partial bool IstGebucht { get; set; }
    [ObservableProperty] public partial string Belegnummer { get; set; } = "";
    [ObservableProperty] public partial BetriebDaten? Betrieb { get; set; }
    [ObservableProperty] public partial PartnerDaten? PartnerAuswahl { get; set; }
    [ObservableProperty] public partial Belegtyp Typ { get; set; } = Belegtyp.ChallengeGebuehr;
    [ObservableProperty] public partial Richtung Richtung { get; set; } = Richtung.Eingang;
    [ObservableProperty] public partial DateTime? Rechnungsdatum { get; set; }
    [ObservableProperty] public partial DateTime? Leistungsdatum { get; set; }
    [ObservableProperty] public partial DateTime? Zahlungsdatum { get; set; }
    [ObservableProperty] public partial string Betrag { get; set; } = "";
    [ObservableProperty] public partial string Waehrung { get; set; } = "USD";
    [ObservableProperty] public partial string AusgewieseneUst { get; set; } = "";
    [ObservableProperty] public partial EuerKategorieDaten? EuerKategorie { get; set; }
    [ObservableProperty] public partial BelegZeileVm? Bezug { get; set; }
    [ObservableProperty] public partial string Notiz { get; set; } = "";

    // Übersteuerung
    [ObservableProperty] public partial bool UebersteuerungAktiv { get; set; }
    [ObservableProperty] public partial Steuerschuldner? UeSteuerschuldner { get; set; }
    [ObservableProperty] public partial string UeSatz { get; set; } = "";
    [ObservableProperty] public partial string UeKurs { get; set; } = "";
    [ObservableProperty] public partial string UeBegruendung { get; set; } = "";

    // Aktionen auf gebuchten Belegen
    [ObservableProperty] public partial string Begruendung { get; set; } = "";
    [ObservableProperty] public partial DateTime? Stornodatum { get; set; } = DateTime.Today;
    [ObservableProperty] public partial EuerKategorieDaten? MehrfachKategorie { get; set; }

    [ObservableProperty] public partial string Vorschau { get; set; } = "";

    partial void OnAuswahlChanged(BelegZeileVm? value)
    {
        if (value is not null) Lade(value.Ansicht);
    }

    partial void OnTypChanged(Belegtyp value) => Richtung = BelegDaten.RichtungFuer(value);

    partial void OnPartnerAuswahlChanged(PartnerDaten? value)
    {
        if (value?.StdBelegtyp is { } t && BearbeiteterId is null) Typ = t;
    }

    [RelayCommand]
    public void Aktualisieren()
    {
        Ersetze(Betriebe, Ab.Stammdaten.Betriebe().Where(b => b.Aktiv));
        Ersetze(Partner, Ab.Stammdaten.Partner().Where(p => p.Aktiv));
        Ersetze(EuerKategorien, Ab.Stammdaten.EuerKategorien());
        Ersetze(Vorlagen, Ab.Stammdaten.Vorlagen());
        var filter = new BelegFilter
        {
            Suchtext = string.IsNullOrWhiteSpace(Suchtext) ? null : Suchtext,
            Status = StatusFilter == "Alle" ? null : Enum.Parse<BelegStatus>(StatusFilter),
        };
        var liste = Ab.Belege.Liste(filter).OrderByDescending(b => b.Daten.Leistungsdatum).ThenByDescending(b => b.Id).Select(b => new BelegZeileVm(b)).ToList();
        Ersetze(Belege, liste);
        Ersetze(BuchbareBezuege, Ab.Belege.Liste(new BelegFilter { Status = BelegStatus.Gebucht })
            .Where(b => b.StornoVonId is null && b.Daten.Typ != Belegtyp.Erstattung).Select(b => new BelegZeileVm(b)));
    }

    [RelayCommand]
    public void Neu()
    {
        BearbeiteterId = null;
        IstGebucht = false;
        EditorTitel = "Neuer Beleg";
        var heute = DateTime.Today;
        Belegnummer = Ab.Belege.NaechsteBelegnummer(DateOnly.FromDateTime(heute));
        Betrieb ??= Betriebe.FirstOrDefault();
        Rechnungsdatum = Leistungsdatum = Zahlungsdatum = heute;
        Betrag = AusgewieseneUst = Notiz = "";
        Bezug = null;
        UebersteuerungAktiv = false;
        Vorschau = "";
    }

    [RelayCommand]
    public void AusVorlage(VorlageDaten? vorlage)
    {
        if (vorlage is null) return;
        Versuche(() =>
        {
            var d = Ab.Stammdaten.AusVorlage(vorlage.Id, DateOnly.FromDateTime(Leistungsdatum ?? DateTime.Today), Belegnummer);
            Neu();
            Uebernehme(d);
        }, $"Vorlage „{vorlage.Name}“ übernommen.");
    }

    [RelayCommand]
    public void Berechnen() => Versuche(() => ZeigeVorschau(Ab.Belege.Vorschau(Daten(), Uebersteuerung())));

    [RelayCommand]
    public void EntwurfSpeichern() => Versuche(() =>
    {
        var d = Daten();
        if (BearbeiteterId is { } id) Ab.Belege.AendereEntwurf(id, d);
        else BearbeiteterId = Ab.Belege.ErfasseEntwurf(d);
        var dubletten = Ab.Belege.MoeglicheDubletten(d, BearbeiteterId.Value).Count;
        NachAktion(BearbeiteterId.Value, dubletten > 0
            ? $"Entwurf {d.Belegnummer} gespeichert. Hinweis: {dubletten} mögliche Dublette(n)."
            : $"Entwurf {d.Belegnummer} gespeichert.");
    });

    [RelayCommand]
    public void Buchen() => Versuche(() =>
    {
        var d = Daten();
        if (BearbeiteterId is { } id) Ab.Belege.AendereEntwurf(id, d);
        else BearbeiteterId = Ab.Belege.ErfasseEntwurf(d);
        var e = Ab.Belege.Buche(BearbeiteterId.Value, Uebersteuerung());
        NachAktion(BearbeiteterId.Value, $"{d.Belegnummer} gebucht: {e.Zeitraum}, USt {Geld(e.Steuer.Betrag)} €, Zahllast {Geld(e.Zahllast.Betrag)} €.");
    });

    [RelayCommand]
    public void GebuchtAendern() => Versuche(() =>
    {
        var id = BearbeiteterId ?? throw new FachlicherFehler("Kein Beleg ausgewählt.");
        var e = Ab.Belege.AendereGebucht(id, Daten(), Begruendung, Uebersteuerung());
        NachAktion(id, $"Geändert und neu berechnet: USt {Geld(e.Steuer.Betrag)} €.");
    });

    [RelayCommand]
    public void Stornieren() => Versuche(() =>
    {
        var id = BearbeiteterId ?? throw new FachlicherFehler("Kein Beleg ausgewählt.");
        var stornoId = Ab.Belege.Storniere(id, Datum(Stornodatum, "Stornodatum"), Begruendung);
        NachAktion(stornoId, "Storno gebucht.");
    });

    [RelayCommand]
    public void EntwurfLoeschen() => Versuche(() =>
    {
        var id = BearbeiteterId ?? throw new FachlicherFehler("Kein Beleg ausgewählt.");
        Ab.Belege.LoescheEntwurf(id, string.IsNullOrWhiteSpace(Begruendung) ? "Entwurf verworfen" : Begruendung);
        Aktualisieren();
        Neu();
    }, "Entwurf gelöscht.");

    /// <summary>Sammelbuchung der markierten Entwürfe.</summary>
    public void BucheMarkierte(IEnumerable<BelegZeileVm> markiert)
    {
        var ids = markiert.Where(m => m.Ansicht.Status == BelegStatus.Entwurf).Select(m => m.Id).ToList();
        var fehler = Ab.Belege.BucheAlle(ids);
        Aktualisieren();
        if (fehler.Count == 0) Status.Ok($"{ids.Count} Beleg(e) gebucht.");
        else Status.Fehler($"{ids.Count - fehler.Count} gebucht, {fehler.Count} Fehler: {fehler.First().Value}");
    }

    /// <summary>Mehrfachbearbeitung: EÜR-Kategorie für markierte Belege.</summary>
    public void KategorieFuerMarkierte(IEnumerable<BelegZeileVm> markiert) => Versuche(() =>
    {
        var ids = markiert.Select(m => m.Id).ToList();
        var fehler = Ab.Belege.SetzeEuerKategorie(ids, MehrfachKategorie?.Id,
            string.IsNullOrWhiteSpace(Begruendung) ? "Mehrfachbearbeitung EÜR-Kategorie" : Begruendung);
        Aktualisieren();
        if (fehler.Count > 0) throw new FachlicherFehler($"{ids.Count - fehler.Count} geändert, {fehler.Count} übersprungen (festgeschrieben).");
    }, "EÜR-Kategorie gesetzt.");

    private void NachAktion(long id, string meldung)
    {
        Aktualisieren();
        Lade(Ab.Belege.Laden(id));
        Status.Ok(meldung);
    }

    private void Lade(BelegAnsicht a)
    {
        BearbeiteterId = a.Id;
        IstGebucht = a.Status == BelegStatus.Gebucht;
        EditorTitel = $"{a.Daten.Belegnummer} – {new BelegZeileVm(a).Status}";
        Uebernehme(a.Daten);
        UebersteuerungAktiv = false;
        Vorschau = a.Steuer is null ? "" : Beschreibe(a.Steuer);
    }

    private void Uebernehme(BelegDaten d)
    {
        Belegnummer = d.Belegnummer;
        Betrieb = Betriebe.FirstOrDefault(b => b.Id == d.BetriebId);
        PartnerAuswahl = Partner.FirstOrDefault(p => p.Id == d.PartnerId);
        Typ = d.Typ;
        Richtung = d.Richtung;
        Rechnungsdatum = AlsDateTime(d.Rechnungsdatum);
        Leistungsdatum = AlsDateTime(d.Leistungsdatum);
        Zahlungsdatum = AlsDateTime(d.Zahlungsdatum);
        Betrag = d.Betrag.Betrag == 0m ? "" : Geld(d.Betrag.Betrag);
        Waehrung = d.Betrag.Waehrung;
        AusgewieseneUst = d.AusgewieseneUst is { } u ? Geld(u.Betrag) : "";
        EuerKategorie = EuerKategorien.FirstOrDefault(k => k.Id == d.EuerKategorieId);
        Bezug = BuchbareBezuege.FirstOrDefault(b => b.Id == d.BezugBelegId);
        Notiz = d.Notiz ?? "";
    }

    internal BelegDaten Daten() => new()
    {
        Belegnummer = Belegnummer,
        BetriebId = Betrieb?.Id ?? throw new FachlicherFehler("Betrieb fehlt."),
        PartnerId = PartnerAuswahl?.Id ?? throw new FachlicherFehler("Partner fehlt."),
        Typ = Typ,
        Richtung = Richtung,
        Rechnungsdatum = DatumOpt(Rechnungsdatum),
        Leistungsdatum = Datum(Leistungsdatum, "Leistungsdatum"),
        Zahlungsdatum = DatumOpt(Zahlungsdatum),
        Betrag = new CurrencyAmount(Dezimal(Betrag, "Betrag"), Waehrung.Trim().ToUpperInvariant()),
        AusgewieseneUst = DezimalOpt(AusgewieseneUst, "Ausgewiesene USt") is { } u ? Money.Euro(u) : null,
        EuerKategorieId = EuerKategorie?.Id,
        BezugBelegId = Bezug?.Id,
        Notiz = string.IsNullOrWhiteSpace(Notiz) ? null : Notiz,
    };

    private Uebersteuerung? Uebersteuerung() => !UebersteuerungAktiv ? null : new Uebersteuerung(UeBegruendung)
    {
        Steuerschuldner = UeSteuerschuldner,
        Satz = DezimalOpt(UeSatz, "Satz") is { } s ? s / 100m : null,
        Kurs = DezimalOpt(UeKurs, "Kurs"),
        KennzahlSchluessel = UeSteuerschuldner switch
        {
            Domain.Steuerschuldner.Empfaenger13bAbs1 => "13B_ABS1",
            Domain.Steuerschuldner.Empfaenger13bAbs2Nr1 => "13B_ABS2_NR1",
            _ => null,
        },
    };

    private void ZeigeVorschau(SteuerErgebnis e) => Vorschau = Beschreibe(e);

    internal static string Beschreibe(SteuerErgebnis e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Zeitraum {e.Zeitraum} · Entstehung {e.Entstehungsdatum:dd.MM.yyyy} · {Anzeige.Text(e.Besteuerungsart)}");
        sb.AppendLine($"{Anzeige.Text(e.Steuerschuldner)} · {e.Steuerbarkeit} · Ort: {e.Leistungsort}");
        if (e.Kurs is { } k) sb.AppendLine($"Kurs: 1 EUR = {k.ToString(De)}");
        sb.AppendLine($"BMG {Geld(e.Bemessungsgrundlage.Betrag)} € · USt {Geld(e.Steuer.Betrag)} € · VSt {Geld(e.Vorsteuer.Betrag)} € · Zahllast {Geld(e.Zahllast.Betrag)} €");
        if (e.Kennzahlen.Count > 0) sb.AppendLine("Kennzahlen: " + string.Join(", ", e.Kennzahlen.Select(x => $"Kz {x.Kennzahl} = {Geld(x.Betrag.Betrag)}")));
        if (e.Umsatz19.Betrag != 0) sb.AppendLine($"§19-Umsatz: {Geld(e.Umsatz19.Betrag)} €");
        if (e.IstManuell) sb.AppendLine($"MANUELL: {e.ManuelleBegruendung}");
        sb.AppendLine();
        foreach (var z in e.Begruendung) sb.AppendLine("• " + z);
        return sb.ToString();
    }

    internal static void Ersetze<T>(ObservableCollection<T> ziel, IEnumerable<T> quelle)
    {
        ziel.Clear();
        foreach (var x in quelle) ziel.Add(x);
    }
}
