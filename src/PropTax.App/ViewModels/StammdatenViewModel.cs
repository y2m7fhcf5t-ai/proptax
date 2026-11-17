using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropTax.Application;
using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.App.ViewModels;

public sealed partial class StammdatenViewModel : VmBasis
{
    public StammdatenViewModel(Arbeitsbereich ab, Statusleiste status) : base(ab, status)
    {
        Aktualisieren();
        NeuerPartner();
    }

    public ObservableCollection<PartnerDaten> Partner { get; } = [];
    public ObservableCollection<BetriebDaten> Betriebe { get; } = [];
    public ObservableCollection<EuerKategorieDaten> EuerKategorien { get; } = [];
    public ObservableCollection<VorlageDaten> Vorlagen { get; } = [];
    public PartnerKategorie?[] Kategorien { get; } = [null, PartnerKategorie.Inland, PartnerKategorie.Eu, PartnerKategorie.Drittland];
    public Belegtyp?[] Belegtypen { get; } = [null, .. Enum.GetValues<Belegtyp>().Cast<Belegtyp?>()];
    public Belegtyp[] VorlageTypen { get; } = Enum.GetValues<Belegtyp>();

    [ObservableProperty] public partial PartnerDaten? PartnerAuswahl { get; set; }
    [ObservableProperty] public partial long PartnerId { get; set; }
    [ObservableProperty] public partial string PartnerName { get; set; } = "";
    [ObservableProperty] public partial string Land { get; set; } = "";
    [ObservableProperty] public partial PartnerKategorie? Kategorie { get; set; }
    [ObservableProperty] public partial string UstIdnr { get; set; } = "";
    [ObservableProperty] public partial string TaxId { get; set; } = "";
    [ObservableProperty] public partial Belegtyp? StdBelegtyp { get; set; }
    [ObservableProperty] public partial bool IstPropFirma { get; set; }
    [ObservableProperty] public partial bool PartnerAktiv { get; set; } = true;
    [ObservableProperty] public partial string PartnerNotiz { get; set; } = "";

    [ObservableProperty] public partial BetriebDaten? BetriebAuswahl { get; set; }
    [ObservableProperty] public partial string BetriebName { get; set; } = "";
    [ObservableProperty] public partial string BetriebKurz { get; set; } = "";

    [ObservableProperty] public partial string VorlageName { get; set; } = "";
    [ObservableProperty] public partial BetriebDaten? VorlageBetrieb { get; set; }
    [ObservableProperty] public partial PartnerDaten? VorlagePartner { get; set; }
    [ObservableProperty] public partial Belegtyp VorlageTyp { get; set; } = Belegtyp.ChallengeGebuehr;
    [ObservableProperty] public partial string VorlageBetrag { get; set; } = "";
    [ObservableProperty] public partial string VorlageWaehrung { get; set; } = "USD";
    [ObservableProperty] public partial EuerKategorieDaten? VorlageKategorie { get; set; }

    partial void OnPartnerAuswahlChanged(PartnerDaten? value)
    {
        if (value is null) return;
        PartnerId = value.Id; PartnerName = value.Name; Land = value.LandIso ?? ""; Kategorie = value.Kategorie;
        UstIdnr = value.UstIdnr ?? ""; TaxId = value.TaxId ?? ""; StdBelegtyp = value.StdBelegtyp; IstPropFirma = value.IstPropFirma;
        PartnerAktiv = value.Aktiv; PartnerNotiz = value.Notiz ?? "";
    }

    partial void OnBetriebAuswahlChanged(BetriebDaten? value)
    {
        if (value is null) return;
        BetriebName = value.Name; BetriebKurz = value.Kurzzeichen ?? "";
    }

    [RelayCommand]
    public void Aktualisieren()
    {
        BelegeViewModel.Ersetze(Partner, Ab.Stammdaten.Partner());
        BelegeViewModel.Ersetze(Betriebe, Ab.Stammdaten.Betriebe());
        BelegeViewModel.Ersetze(EuerKategorien, Ab.Stammdaten.EuerKategorien());
        BelegeViewModel.Ersetze(Vorlagen, Ab.Stammdaten.Vorlagen());
    }

    [RelayCommand]
    public void NeuerPartner()
    {
        PartnerAuswahl = null;
        PartnerId = 0; PartnerName = Land = UstIdnr = TaxId = PartnerNotiz = ""; Kategorie = null; StdBelegtyp = null; IstPropFirma = false; PartnerAktiv = true;
    }

    [RelayCommand]
    public void PartnerSpeichern() => Versuche(() =>
    {
        var id = Ab.Stammdaten.SpeicherePartner(new PartnerDaten
        {
            Id = PartnerId, Name = PartnerName, LandIso = Land, Kategorie = Kategorie, UstIdnr = Leer(UstIdnr), TaxId = Leer(TaxId),
            StdBelegtyp = StdBelegtyp, IstPropFirma = IstPropFirma, Aktiv = PartnerAktiv, Notiz = Leer(PartnerNotiz),
        });
        Aktualisieren();
        PartnerAuswahl = Partner.First(p => p.Id == id);
    }, $"Partner „{PartnerName}“ gespeichert.");

    [RelayCommand]
    public void NeuerBetrieb() { BetriebAuswahl = null; BetriebName = BetriebKurz = ""; }

    [RelayCommand]
    public void BetriebSpeichern() => Versuche(() =>
    {
        Ab.Stammdaten.SpeichereBetrieb(new BetriebDaten(BetriebAuswahl?.Id ?? 0, BetriebName, Leer(BetriebKurz), true));
        Aktualisieren();
    }, $"Betrieb „{BetriebName}“ gespeichert.");

    [RelayCommand]
    public void VorlageSpeichern() => Versuche(() =>
    {
        Ab.Stammdaten.SpeichereVorlage(new VorlageDaten
        {
            Name = VorlageName, BetriebId = VorlageBetrieb?.Id, PartnerId = VorlagePartner?.Id ?? throw new FachlicherFehler("Partner der Vorlage fehlt."),
            Typ = VorlageTyp, Richtung = BelegDaten.RichtungFuer(VorlageTyp), EuerKategorieId = VorlageKategorie?.Id,
            Betrag = DezimalOpt(VorlageBetrag, "Betrag") is { } b ? new CurrencyAmount(b, VorlageWaehrung.Trim().ToUpperInvariant()) : null,
        });
        Aktualisieren();
    }, $"Vorlage „{VorlageName}“ gespeichert.");

    private static string? Leer(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
