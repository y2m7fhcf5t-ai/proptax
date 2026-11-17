using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropTax.Application;
using PropTax.Domain;
using PropTax.Reporting;

namespace PropTax.App.ViewModels;

public sealed record KennzahlVm(KennzahlZeile K)
{
    public string Kz => K.Kennzahl;
    public string Bezeichnung => K.Bezeichnung;
    public string Betrag => VmBasis.Geld(K.Betrag);
    public string Formular => K.Art == TaxEngine.Ermittlung.KennzahlArt.Bemessungsgrundlage ? K.Formularwert.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : VmBasis.Geld(K.Formularwert);
    public int Belege => K.Belege.Count;
}

public sealed record DrillVm(DrillBeleg B)
{
    public string Beleg => B.Belegnummer;
    public string Datum => B.Datum.ToString("dd.MM.yyyy");
    public string Partner => B.Partner;
    public string Betrag => $"{VmBasis.Geld(B.BetragFw)} {B.Waehrung}";
    public string Wert => VmBasis.Geld(B.Wert);
}

public sealed record ZeitraumVm(ZeitraumZeile Z)
{
    public string Zeitraum => Z.Zeitraum.ToString();
    public string Faellig => Z.Faelligkeit.Tatsaechlich.ToString("ddd dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))
        + (Z.Faelligkeit.Verschiebungsgrund is null ? "" : $" (verschoben: {Z.Faelligkeit.Verschiebungsgrund})");
    public string Pflicht => (Z.Pflicht ? "ja – " : "nein – ") + Z.PflichtGrund;
    public string Steuer => VmBasis.Geld(Z.Steuer);
    public string Vorsteuer => VmBasis.Geld(Z.Vorsteuer);
    public string Zahllast => VmBasis.Geld(Z.Zahllast);
    public string Status => Z.Festgeschrieben ? "festgeschrieben" : "offen";
    public int Belege => Z.AnzahlBelege;
}

public sealed record EuerZeileVm(EuerZeile Z)
{
    public string Art => Z.Art;
    public string Kategorie => Z.Kategorie;
    public string Zeile => Z.AnlageZeile ?? "nicht zugeordnet";
    public string Betrag => VmBasis.Geld(Z.Betrag);
    public int Anzahl => Z.Anzahl;
}

public sealed record EuerPositionVm(EuerPosition P)
{
    public string Beleg => P.Belegnummer;
    public string Datum => P.Datum.ToString("dd.MM.yyyy");
    public string Betrieb => P.Betrieb;
    public string Kategorie => P.Kategorie;
    public string Art => P.Art;
    public string Betrag => VmBasis.Geld(P.BetragEur);
    public string UstEur => P.UstEur is { } u ? VmBasis.Geld(u) : "";
    public string Kursdifferenz => P.Kursdifferenz == 0m ? "" : VmBasis.Geld(P.Kursdifferenz);
    public string Quelle => P.Quelle;
}

public sealed record NachweisVm(NachweisZeile Z)
{
    public string Datum => Z.Datum.ToString("dd.MM.yyyy");
    public string Beleg => Z.Belegnummer;
    public string Partner => Z.Partner;
    public string Land => Z.Land ?? "";
    public string Betrag => $"{VmBasis.Geld(Z.BetragFw)} {Z.Waehrung}";
    public string Kurs => Z.Kurs?.ToString("0.0000", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) ?? "";
    public string Eur => VmBasis.Geld(Z.Eur);
    public string Steuer => VmBasis.Geld(Z.Steuer13b);
    public string Kz => Z.Kennzahlen;
    public string Zeitraum => Z.Zeitraum;
    public string Dateien => Z.Belegdateien.Count == 0 ? "⚠ fehlt" : string.Join(", ", Z.Belegdateien);
    public string Hinweis => Z.Hinweis ?? "";
}

public sealed record DashboardVm(DashboardZeile Z, decimal Max)
{
    private const double Breite = 220;
    public string Bezeichnung => Z.Bezeichnung;
    public string Gebuehren => VmBasis.Geld(Z.Gebuehren);
    public string Payouts => VmBasis.Geld(Z.Payouts);
    public string UstLast => VmBasis.Geld(Z.UstLast);
    public string Netto => VmBasis.Geld(Z.Netto);
    public bool NettoNegativ => Z.Netto < 0;
    public double BalkenGebuehren => Max <= 0 ? 0 : (double)(Math.Max(0, Z.Gebuehren) / Max) * Breite;
    public double BalkenPayouts => Max <= 0 ? 0 : (double)(Math.Max(0, Z.Payouts) / Max) * Breite;
    public string Anzahl => $"{Z.AnzahlGebuehren} / {Z.AnzahlPayouts}";
}

public sealed partial class AuswertungenViewModel : VmBasis
{
    private readonly IDialoge _dialoge;
    private bool _laedt;

    public AuswertungenViewModel(Arbeitsbereich ab, Statusleiste status, IDialoge dialoge) : base(ab, status)
    {
        _dialoge = dialoge;
        Aktualisieren();
    }

    public ObservableCollection<UstZeitraum> Zeitraeume { get; } = [];
    public ObservableCollection<KennzahlVm> Kennzahlen { get; } = [];
    public ObservableCollection<DrillVm> DrillBelege { get; } = [];
    public ObservableCollection<ZeitraumVm> Fristen { get; } = [];
    public ObservableCollection<string> GrenzWarnungen { get; } = [];
    public ObservableCollection<BetriebDaten?> Betriebe { get; } = [];
    public ObservableCollection<EuerZeileVm> EuerZeilen { get; } = [];
    public ObservableCollection<EuerPositionVm> EuerPositionen { get; } = [];
    public ObservableCollection<string> EuerHinweise { get; } = [];
    public ObservableCollection<EuerZeilenZuordnung> ZeilenZuordnung { get; } = [];
    public ObservableCollection<NachweisVm> Nachweis { get; } = [];
    public ObservableCollection<DashboardVm> DashboardFirmen { get; } = [];
    public ObservableCollection<DashboardVm> DashboardMonate { get; } = [];
    public ObservableCollection<PartnerDaten?> Firmen { get; } = [];

    [ObservableProperty] public partial decimal Jahr { get; set; } = DateTime.Today.Year;
    [ObservableProperty] public partial UstZeitraum? Zeitraum { get; set; }
    [ObservableProperty] public partial KennzahlVm? KennzahlAuswahl { get; set; }
    [ObservableProperty] public partial string VaKopf { get; set; } = "";
    [ObservableProperty] public partial string FristenSumme { get; set; } = "";

    [ObservableProperty] public partial double GrenzProzent { get; set; }
    [ObservableProperty] public partial string GrenzText { get; set; } = "";
    [ObservableProperty] public partial string Vorschlag { get; set; } = "";
    [ObservableProperty] public partial bool HatVorschlag { get; set; }
    [ObservableProperty] public partial string VorjahrEingabe { get; set; } = "";
    [ObservableProperty] public partial string Begruendung { get; set; } = "";

    [ObservableProperty] public partial BetriebDaten? Betrieb { get; set; }
    [ObservableProperty] public partial string EuerSumme { get; set; } = "";
    [ObservableProperty] public partial EuerZeilenZuordnung? ZuordnungAuswahl { get; set; }
    [ObservableProperty] public partial string AnlageZeile { get; set; } = "";

    [ObservableProperty] public partial string NachweisSumme { get; set; } = "";
    [ObservableProperty] public partial PartnerDaten? Firma { get; set; }
    [ObservableProperty] public partial string DashboardSumme { get; set; } = "";

    partial void OnJahrChanged(decimal value) => Aktualisieren();
    partial void OnZeitraumChanged(UstZeitraum? value) { if (!_laedt) LadeVa(); }
    partial void OnKennzahlAuswahlChanged(KennzahlVm? value) =>
        BelegeViewModel.Ersetze(DrillBelege, (value?.K.Belege ?? []).Select(b => new DrillVm(b)));
    partial void OnBetriebChanged(BetriebDaten? value) { if (!_laedt) LadeEuer(); }
    partial void OnFirmaChanged(PartnerDaten? value) { if (!_laedt) LadeDashboard(); }
    partial void OnZuordnungAuswahlChanged(EuerZeilenZuordnung? value) => AnlageZeile = value?.Zeile ?? "";

    [RelayCommand]
    public void Aktualisieren() => Versuche(() =>
    {
        _laedt = true;
        try
        {
            var j = (int)Jahr;
            var alt = Zeitraum;
            BelegeViewModel.Ersetze(Zeitraeume, Ab.Auswertungen.Zeitraeume(j));
            Zeitraum = Zeitraeume.Contains(alt ?? default) ? alt : Zeitraeume.FirstOrDefault();
            if (Betriebe.Count == 0) BelegeViewModel.Ersetze(Betriebe, [null, .. Ab.Stammdaten.Betriebe()]);
            BelegeViewModel.Ersetze(Firmen, [null, .. Ab.Stammdaten.Partner().Where(p => p.IstPropFirma)]);
        }
        finally { _laedt = false; }
        LadeVa();
        LadeFristen();
        LadeGrenze();
        LadeEuer();
        LadeNachweis();
        LadeDashboard();
    });

    private void LadeVa()
    {
        if (Zeitraum is not { } z) return;
        Versuche(() =>
        {
            var v = Ab.Auswertungen.UstVa(z);
            BelegeViewModel.Ersetze(Kennzahlen, v.Kennzahlen.Select(k => new KennzahlVm(k)));
            BelegeViewModel.Ersetze(DrillBelege, v.Belege.Select(b => new DrillVm(b)));
            var zz = new ZeitraumVm(v.Zeitraum);
            VaKopf = $"{z}: Steuer {zz.Steuer} € · Vorsteuer {zz.Vorsteuer} € · Zahllast {zz.Zahllast} € · fällig {zz.Faellig} · Voranmeldung {zz.Pflicht} · {zz.Status}";
        });
    }

    private void LadeFristen() => Versuche(() =>
    {
        var z = Ab.Auswertungen.Zahllasten((int)Jahr);
        BelegeViewModel.Ersetze(Fristen, z.Select(x => new ZeitraumVm(x)));
        FristenSumme = $"Jahressumme Zahllast {Geld(z.Sum(x => x.Zahllast))} € · {z.Count(x => x.Pflicht)} Voranmeldung(en) erforderlich";
    });

    private void LadeGrenze() => Versuche(() =>
    {
        var g = Ab.Auswertungen.Grenze((int)Jahr);
        GrenzProzent = (double)Math.Min(g.Status.Quote * 100m, 120m);
        GrenzText = $"Gesamtumsatz §19 {Geld(g.Status.Summe.Betrag)} € von {Geld(g.Status.Grenze.Betrag)} € ({g.Status.Quote:P1}) · " +
            $"Vorjahr {Geld(g.Vorjahresumsatz)} € ({(g.VorjahrManuell ? "manuell" : "aus Daten")}) · heute: {Anzeige.Text(g.ArtHeute)} · " +
            string.Join(", ", g.Status.JeBetrieb.Select(b => $"{b.Key} {Geld(b.Value.Betrag)} €"));
        BelegeViewModel.Ersetze(GrenzWarnungen, g.Status.Warnungen.Select(w => $"{w.Datum:dd.MM.yyyy} · {w.BelegRef}: {w.Text} (kumuliert {Geld(w.SummeNachBeleg.Betrag)} €)"));
        HatVorschlag = g.Status.Vorschlag is not null;
        Vorschlag = g.Status.Vorschlag is { } v ? $"Vorschlag: Regelbesteuerung ab {v.AbDatum:dd.MM.yyyy}. {v.Begruendung}" : "Kein Umschaltbedarf.";
    });

    private void LadeEuer() => Versuche(() =>
    {
        var e = Ab.Auswertungen.Euer((int)Jahr, Betrieb?.Id);
        BelegeViewModel.Ersetze(EuerZeilen, e.Zeilen.Select(z => new EuerZeileVm(z)));
        BelegeViewModel.Ersetze(EuerPositionen, e.Positionen.Select(p => new EuerPositionVm(p)));
        BelegeViewModel.Ersetze(EuerHinweise, e.Hinweise);
        BelegeViewModel.Ersetze(ZeilenZuordnung, Ab.Stammdaten.EuerZeilen((int)Jahr));
        EuerSumme = $"{e.Betrieb}: Einnahmen {Geld(e.Einnahmen)} € − Ausgaben {Geld(e.Ausgaben)} € = Ergebnis {Geld(e.Gewinn)} € · Kursdifferenzen (enthalten) {Geld(e.Kursdifferenzen)} €";
    });

    private void LadeNachweis() => Versuche(() =>
    {
        var n = Ab.Auswertungen.Nachweis((int)Jahr);
        BelegeViewModel.Ersetze(Nachweis, n.Zeilen.Select(z => new NachweisVm(z)));
        NachweisSumme = $"{n.Zeilen.Count} Belege · EUR {Geld(n.SummeEur)} · §13b-USt {Geld(n.SummeSteuer)}" + (n.AnzahlOhneBeleg > 0 ? $" · ⚠ {n.AnzahlOhneBeleg} ohne Belegdatei" : "");
    });

    private void LadeDashboard() => Versuche(() =>
    {
        var d = Ab.Auswertungen.Dashboard((int)Jahr, Firma?.Id);
        var maxF = d.JeFirma.Select(z => Math.Max(z.Gebuehren, z.Payouts)).DefaultIfEmpty(0m).Max();
        var maxM = d.JeMonat.Select(z => Math.Max(z.Gebuehren, z.Payouts)).DefaultIfEmpty(0m).Max();
        BelegeViewModel.Ersetze(DashboardFirmen, d.JeFirma.Select(z => new DashboardVm(z, maxF)));
        BelegeViewModel.Ersetze(DashboardMonate, d.JeMonat.Select(z => new DashboardVm(z, maxM)));
        DashboardSumme = $"Gebühren {Geld(d.Summe.Gebuehren)} € · Payouts {Geld(d.Summe.Payouts)} € · USt-Last {Geld(d.Summe.UstLast)} € · Netto {Geld(d.Summe.Netto)} €";
    });

    [RelayCommand]
    public void UmschaltungUebernehmen() => Versuche(() =>
    {
        var ab = Ab.Auswertungen.UebernehmeUmschaltung((int)Jahr, Begruendung);
        Aktualisieren();
        Status.Ok($"Regelbesteuerung ab {ab:dd.MM.yyyy} gesetzt. Betroffene Belege unter Einstellungen neu berechnen.");
    });

    [RelayCommand]
    public void VorjahrSpeichern() => Versuche(() =>
    {
        Ab.Auswertungen.SetzeVorjahresumsatz((int)Jahr, Dezimal(VorjahrEingabe, "Vorjahresumsatz"), Begruendung);
        LadeGrenze();
    }, "Vorjahresumsatz gespeichert.");

    [RelayCommand]
    public void ZeileSpeichern() => Versuche(() =>
    {
        var z = ZuordnungAuswahl ?? throw new FachlicherFehler("Keine Kategorie gewählt.");
        Ab.Stammdaten.SetzeEuerZeile((int)Jahr, z.KategorieId, string.IsNullOrWhiteSpace(AnlageZeile) ? null : AnlageZeile, Begruendung);
        LadeEuer();
    }, "Zeilenzuordnung gespeichert.");

    [RelayCommand] public Task NachweisPdf() => Exportiere($"Nachweis_{(int)Jahr}", "pdf", z => Ab.Auswertungen.ExportiereNachweis((int)Jahr, z, ExportFormat.Pdf));
    [RelayCommand] public Task NachweisXlsx() => Exportiere($"Nachweis_{(int)Jahr}", "xlsx", z => Ab.Auswertungen.ExportiereNachweis((int)Jahr, z, ExportFormat.Xlsx));
    [RelayCommand] public Task NachweisZip() => Exportiere($"Nachweis_{(int)Jahr}_mit_Belegen", "zip", z => Ab.Auswertungen.ExportiereNachweis((int)Jahr, z, ExportFormat.Zip));
    [RelayCommand] public Task VaXlsx() => Zeitraum is { } zr
        ? Exportiere($"UStVA_{zr}", "xlsx", z => Ab.Auswertungen.ExportiereTabelle(Ab.Auswertungen.UstVaTabelle(zr), z, ExportFormat.Xlsx))
        : Task.CompletedTask;
    [RelayCommand] public Task EuerXlsx() => Exportiere($"EUER_{(int)Jahr}_{Betrieb?.Name ?? "Gesamt"}", "xlsx", z => Ab.Auswertungen.ExportiereTabelle(Ab.Auswertungen.EuerTabelle((int)Jahr, Betrieb?.Id), z, ExportFormat.Xlsx));
    [RelayCommand] public Task EuerCsv() => Exportiere($"EUER_{(int)Jahr}_{Betrieb?.Name ?? "Gesamt"}", "csv", z => Ab.Auswertungen.ExportiereTabelle(Ab.Auswertungen.EuerTabelle((int)Jahr, Betrieb?.Id), z, ExportFormat.Csv));

    private async Task Exportiere(string vorschlag, string endung, Action<string> aktion)
    {
        var ziel = await _dialoge.DateiSpeichern("Export speichern", $"{vorschlag}.{endung}", endung);
        if (ziel is null) return;
        Versuche(() => aktion(ziel), $"Gespeichert: {ziel}");
    }
}
