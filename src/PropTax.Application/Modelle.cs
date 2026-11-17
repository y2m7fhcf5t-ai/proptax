using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.Application;

public sealed record BetriebDaten(long Id, string Name, string? Kurzzeichen, bool Aktiv);

public sealed record PartnerDaten
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public string? LandIso { get; init; }
    /// <summary>null = ungeklärt (Belege nicht buchbar).</summary>
    public PartnerKategorie? Kategorie { get; init; }
    public string? UstIdnr { get; init; }
    public string? TaxId { get; init; }
    public Belegtyp? StdBelegtyp { get; init; }
    public bool IstPropFirma { get; init; }
    public bool Aktiv { get; init; } = true;
    public string? Notiz { get; init; }
}

public sealed record EuerKategorieDaten(long Id, string Code, string Bezeichnung, string Art);

public sealed record VorlageDaten
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public long? BetriebId { get; init; }
    public long? PartnerId { get; init; }
    public Belegtyp Typ { get; init; }
    public Richtung Richtung { get; init; }
    public CurrencyAmount? Betrag { get; init; }
    public long? EuerKategorieId { get; init; }
}

public sealed record BelegDaten
{
    public required string Belegnummer { get; init; }
    public required long BetriebId { get; init; }
    public required long PartnerId { get; init; }
    public required Belegtyp Typ { get; init; }
    public required Richtung Richtung { get; init; }
    public DateOnly? Rechnungsdatum { get; init; }
    public required DateOnly Leistungsdatum { get; init; }
    public DateOnly? Zahlungsdatum { get; init; }
    public required CurrencyAmount Betrag { get; init; }
    public Money? AusgewieseneUst { get; init; }
    public decimal? Bankkurs { get; init; }
    public long? EuerKategorieId { get; init; }
    public long? BezugBelegId { get; init; }
    public string? Notiz { get; init; }

    /// <summary>Standardrichtung je Belegtyp (Einnahmen = Ausgang).</summary>
    public static Richtung RichtungFuer(Belegtyp typ) =>
        typ is Belegtyp.Payout or Belegtyp.Verkauf or Belegtyp.SonstigeEinnahme ? Richtung.Ausgang : Richtung.Eingang;
}

public enum BelegStatus { Entwurf, Gebucht }

public sealed record BelegAnsicht(
    long Id,
    BelegDaten Daten,
    BelegStatus Status,
    string BetriebName,
    string PartnerName,
    PartnerKategorie? PartnerKategorie,
    SteuerErgebnis? Steuer,
    long? StornoVonId,
    long? StorniertDurchId,
    long? KorrekturVonId,
    int AnzahlDokumente,
    bool MoeglicheDublette)
{
    public bool IstStorniert => StorniertDurchId is not null;
}

public sealed record BelegFilter
{
    public DateOnly? Von { get; init; }
    public DateOnly? Bis { get; init; }
    public long? BetriebId { get; init; }
    public long? PartnerId { get; init; }
    public BelegStatus? Status { get; init; }
    public string? Suchtext { get; init; }
}
