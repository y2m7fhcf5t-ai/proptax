using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.TaxEngine.Regelwerk;

/// <summary>Eine Zeile der Regeltabelle. Nicht gesetzte Match-Felder (null/leer) passen auf alles.</summary>
public sealed record Steuerregel
{
    public required string Code { get; init; }
    public int Prioritaet { get; init; }
    public IReadOnlyList<Belegtyp> Belegtypen { get; init; } = [];
    public Richtung? Richtung { get; init; }
    public PartnerKategorie? PartnerKategorie { get; init; }
    public DateOnly GueltigAb { get; init; } = DateOnly.MinValue;
    public DateOnly? GueltigBis { get; init; }
    /// <summary>Optionale Bedingung auf eine Einstellung, Format "schluessel=wert".</summary>
    public string? Bedingung { get; init; }

    public required Leistungsart Leistungsart { get; init; }
    public required Steuerbarkeit Steuerbarkeit { get; init; }
    public required string Leistungsort { get; init; }
    public required Steuerschuldner Steuerschuldner { get; init; }
    public string? SatzSchluessel { get; init; }
    public string? KennzahlSchluessel { get; init; }
    public required Entstehung Entstehung { get; init; }
    public bool ZaehltZu19Umsatz { get; init; }
    public string Rechtsgrundlage { get; init; } = "";
    public PruefStatus Status { get; init; } = PruefStatus.Ungeprueft;
}

public sealed record Steuersatz(string Schluessel, decimal Satz, DateOnly GueltigAb, DateOnly? GueltigBis, PruefStatus Status);

public sealed record Kennzahlzuordnung(int Jahr, string Schluessel, string? KzBmg, string? KzSteuer, string? KzVorsteuer,
    string Bezeichnung, PruefStatus Status);

/// <summary>Kurs in Notation 1 EUR = <see cref="Kurs"/> Fremdwährung.</summary>
public sealed record Wechselkurs(string Waehrung, int Jahr, int Monat, decimal Kurs, Kursquelle Quelle);

public sealed record Grenzwert19(int Jahr, Money GrenzeVorjahr, Money GrenzeLaufendesJahr, string Rechtsgrundlage, PruefStatus Status);

public sealed record BesteuerungsartAb(DateOnly Ab, Besteuerungsart Art);

public sealed record Einstellung(string Schluessel, string Wert, DateOnly Ab);

public sealed class RegelwerkException(string message) : InvalidOperationException(message);
