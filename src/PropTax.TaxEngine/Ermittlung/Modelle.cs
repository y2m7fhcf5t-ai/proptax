using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.TaxEngine.Ermittlung;

public sealed record BelegEingabe
{
    public required string BelegRef { get; init; }
    public string Betrieb { get; init; } = "";
    public required Belegtyp Typ { get; init; }
    public required Richtung Richtung { get; init; }
    public required PartnerKategorie PartnerKategorie { get; init; }
    public string? PartnerLand { get; init; }
    public DateOnly? Rechnungsdatum { get; init; }
    public required DateOnly Leistungsdatum { get; init; }
    public DateOnly? Zahlungsdatum { get; init; }
    public required CurrencyAmount Betrag { get; init; }
    /// <summary>Bei Inlandsrechnungen: in der Rechnung ausgewiesene USt (EUR).</summary>
    public Money? AusgewieseneUst { get; init; }
    /// <summary>Pflicht bei <see cref="Belegtyp.Erstattung"/>.</summary>
    public Erstattungsbezug? Bezug { get; init; }
}

public sealed record Erstattungsbezug(BelegEingabe Original, SteuerErgebnis OriginalErgebnis);

/// <summary>Manuelle Übersteuerung; Begründung ist Pflicht.</summary>
public sealed record Uebersteuerung(string Begruendung)
{
    public Steuerbarkeit? Steuerbarkeit { get; init; }
    public Steuerschuldner? Steuerschuldner { get; init; }
    public decimal? Satz { get; init; }
    public decimal? Kurs { get; init; }
    public string? KennzahlSchluessel { get; init; }
    public DateOnly? Entstehungsdatum { get; init; }
}

public enum KennzahlArt { Bemessungsgrundlage, Steuer, Vorsteuer }

public sealed record KennzahlPosition(string Kennzahl, KennzahlArt Art, Money Betrag);

public sealed record SteuerErgebnis
{
    public required string BelegRef { get; init; }
    public required string Betrieb { get; init; }
    public required string RegelCode { get; init; }
    public required Leistungsart Leistungsart { get; init; }
    public required Steuerbarkeit Steuerbarkeit { get; init; }
    public required string Leistungsort { get; init; }
    public required Steuerschuldner Steuerschuldner { get; init; }
    public required Besteuerungsart Besteuerungsart { get; init; }
    public string? KennzahlSchluessel { get; init; }
    public decimal? Satz { get; init; }
    public decimal? Kurs { get; init; }
    public required DateOnly Entstehungsdatum { get; init; }
    public required UstZeitraum Zeitraum { get; init; }
    public required Money Bemessungsgrundlage { get; init; }
    public required Money Steuer { get; init; }
    public required Money Vorsteuer { get; init; }
    public Money Zahllast => Steuer - Vorsteuer;
    /// <summary>Beitrag zum Gesamtumsatz nach §19 UStG.</summary>
    public required Money Umsatz19 { get; init; }
    public required IReadOnlyList<KennzahlPosition> Kennzahlen { get; init; }
    public required IReadOnlyList<string> Begruendung { get; init; }
    public bool IstManuell { get; init; }
    public string? ManuelleBegruendung { get; init; }

    public Money Kz(string kennzahl) =>
        Kennzahlen.Where(k => k.Kennzahl == kennzahl).Aggregate(Money.Null, (s, k) => s + k.Betrag);
}
