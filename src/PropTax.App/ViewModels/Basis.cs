using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PropTax.Application;
using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.App.ViewModels;

/// <summary>Plattformdialoge (Datei/Ordner wählen); im Test durch eine Attrappe ersetzt.</summary>
public interface IDialoge
{
    Task<string?> DateiOeffnen(string titel, params string[] endungen);
    Task<string?> OrdnerWaehlen(string titel);
    Task<string?> DateiSpeichern(string titel, string vorschlag, string endung);
    /// <summary>Öffnet eine Datei mit dem Standardprogramm des Systems.</summary>
    void Oeffnen(string pfad);
    /// <summary>Beendet die Anwendung (z. B. nach Wiederherstellung oder Ordnerwechsel).</summary>
    void Beenden();
}

/// <summary>Gemeinsame Statusleiste für Erfolgs- und Fehlermeldungen.</summary>
public sealed partial class Statusleiste : ObservableObject
{
    [ObservableProperty] public partial string Text { get; set; } = "Bereit.";
    [ObservableProperty] public partial bool IstFehler { get; set; }

    public void Ok(string text) { Text = text; IstFehler = false; }
    public void Fehler(string text) { Text = text; IstFehler = true; }
}

public abstract class VmBasis(Arbeitsbereich ab, Statusleiste status) : ObservableObject
{
    protected static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    protected Arbeitsbereich Ab { get; } = ab;

    /// <summary>Gemeinsamer Export einer Tabelle über den Speichern-Dialog.</summary>
    protected async Task ExportiereTabelle(IDialoge dialoge, Func<PropTax.Reporting.Tabelle> tabelle, string name, string endung)
    {
        var ziel = await dialoge.DateiSpeichern("Export speichern", $"{name}.{endung}", endung);
        if (ziel is null) return;
        var format = endung switch { "pdf" => ExportFormat.Pdf, "csv" => ExportFormat.Csv, _ => ExportFormat.Xlsx };
        Versuche(() => Ab.Auswertungen.ExportiereTabelle(tabelle(), ziel, format), $"Gespeichert: {ziel}");
    }
    public Statusleiste Status { get; } = status;

    /// <summary>Führt eine Aktion aus; fachliche Fehler werden als Meldung angezeigt statt die App zu beenden.</summary>
    protected bool Versuche(Action aktion, string? erfolg = null)
    {
        try
        {
            aktion();
            if (erfolg is not null) Status.Ok(erfolg);
            return true;
        }
        catch (Exception ex) when (ex is FachlicherFehler or RegelwerkException or PeriodeFestgeschriebenException or ArgumentException or FormatException or IOException)
        {
            Status.Fehler(ex.Message);
            return false;
        }
    }

    protected static decimal Dezimal(string? text, string feld)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FachlicherFehler($"{feld} fehlt.");
        var t = text.Trim().Replace(" ", "");
        if (t.Contains('.') && !t.Contains(',')) t = t.Replace('.', ','); // 99.00 → 99,00
        return decimal.Parse(t, NumberStyles.Number, De);
    }

    protected static decimal? DezimalOpt(string? text, string feld) => string.IsNullOrWhiteSpace(text) ? null : Dezimal(text, feld);

    protected static DateOnly Datum(DateTime? d, string feld) => d is null ? throw new FachlicherFehler($"{feld} fehlt.") : DateOnly.FromDateTime(d.Value);
    protected static DateOnly? DatumOpt(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);
    protected static DateTime? AlsDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);

    public static string Geld(decimal betrag) => betrag.ToString("N2", De);
}

/// <summary>Deutsche Anzeigenamen für Enums.</summary>
public static class Anzeige
{
    private static readonly Dictionary<Enum, string> Texte = new()
    {
        [Belegtyp.ChallengeGebuehr] = "Challenge-Gebühr", [Belegtyp.Reset] = "Reset", [Belegtyp.Activation] = "Activation",
        [Belegtyp.FundedGebuehr] = "Funded-Gebühr", [Belegtyp.AboSoftware] = "Software/Abo", [Belegtyp.Payout] = "Payout",
        [Belegtyp.Erstattung] = "Erstattung", [Belegtyp.Verkauf] = "Verkauf", [Belegtyp.SonstigeAusgabe] = "Sonstige Ausgabe",
        [Belegtyp.SonstigeEinnahme] = "Sonstige Einnahme",
        [Richtung.Eingang] = "Eingang (Ausgabe)", [Richtung.Ausgang] = "Ausgang (Einnahme)",
        [PartnerKategorie.Inland] = "Inland", [PartnerKategorie.Eu] = "EU", [PartnerKategorie.Drittland] = "Drittland",
        [Steuerschuldner.Leistender] = "Leistender", [Steuerschuldner.Empfaenger13bAbs1] = "§13b Abs. 1 (EU)",
        [Steuerschuldner.Empfaenger13bAbs2Nr1] = "§13b Abs. 2 Nr. 1 (Drittland)",
        [Besteuerungsart.Kleinunternehmer] = "Kleinunternehmer (§19)", [Besteuerungsart.Regelbesteuerung] = "Regelbesteuerung",
        [Kursquelle.Bmf] = "BMF", [Kursquelle.Bank] = "Bank", [Kursquelle.Manuell] = "Manuell",
    };

    public static string Text(object? wert) => wert is Enum e && Texte.TryGetValue(e, out var t) ? t : wert?.ToString() ?? "";
}

public sealed class EnumAnzeigeKonverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EnumAnzeigeKonverter Instanz = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null ? "(keine)" : Anzeige.Text(value);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
