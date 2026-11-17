namespace PropTax.Persistence;

// Zeilenklassen 1:1 zu den Tabellen (Dapper, snake_case → PascalCase).
#pragma warning disable CS8618

public sealed class BetriebZeile
{
    public long Id { get; set; }
    public long UnternehmenId { get; set; }
    public string Name { get; set; }
    public string? Kurzzeichen { get; set; }
    public long Aktiv { get; set; } = 1;
}

public sealed class PartnerZeile
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string? LandIso { get; set; }
    public string? Kategorie { get; set; }
    public string? UstIdnr { get; set; }
    public string? TaxId { get; set; }
    public string? StdBelegtyp { get; set; }
    public long IstPropFirma { get; set; }
    public long Aktiv { get; set; } = 1;
    public string? Notiz { get; set; }
}

public sealed class EuerKategorieZeile
{
    public long Id { get; set; }
    public string Code { get; set; }
    public string Bezeichnung { get; set; }
    public string Art { get; set; }
}

public sealed class VorlageZeile
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long? BetriebId { get; set; }
    public long? PartnerId { get; set; }
    public string Belegtyp { get; set; }
    public string Richtung { get; set; }
    public long? BetragFwCent { get; set; }
    public string? Waehrung { get; set; }
    public long? EuerKategorieId { get; set; }
}

public sealed class BelegZeile
{
    public long Id { get; set; }
    public string Belegnummer { get; set; }
    public long BetriebId { get; set; }
    public long PartnerId { get; set; }
    public string Belegtyp { get; set; }
    public string Richtung { get; set; }
    public string? Rechnungsdatum { get; set; }
    public string Leistungsdatum { get; set; }
    public string? Zahlungsdatum { get; set; }
    public long BetragFwCent { get; set; }
    public string Waehrung { get; set; }
    public long? AusgewieseneUstCent { get; set; }
    public string? Bankkurs { get; set; }
    public long? EuerKategorieId { get; set; }
    public string? EuerPeriode { get; set; }
    public string Status { get; set; }
    public long? StornoVonId { get; set; }
    public long? KorrekturVonId { get; set; }
    public long? BezugBelegId { get; set; }
    public long? ImportLaufId { get; set; }
    public string DublettenSchluessel { get; set; }
    public string? Notiz { get; set; }
    public string AngelegtAm { get; set; }
}

public sealed class BelegSteuerZeile
{
    public long Id { get; set; }
    public long BelegId { get; set; }
    public string RegelCode { get; set; }
    public string Leistungsart { get; set; }
    public string Steuerbarkeit { get; set; }
    public string Leistungsort { get; set; }
    public string Steuerschuldner { get; set; }
    public string Besteuerungsart { get; set; }
    public string? KennzahlSchluessel { get; set; }
    public string? Satz { get; set; }
    public string? Kurs { get; set; }
    public string Entstehungsdatum { get; set; }
    public string UstPeriode { get; set; }
    public long BmgCent { get; set; }
    public long SteuerCent { get; set; }
    public long VorsteuerCent { get; set; }
    public long Umsatz19Cent { get; set; }
    public string KennzahlenJson { get; set; }
    public string BegruendungJson { get; set; }
    public long IstManuell { get; set; }
    public string? ManuelleBegruendung { get; set; }
    public string BerechnetAm { get; set; }
    public string EngineVersion { get; set; }
}

public sealed class PeriodeZeile
{
    public long Id { get; set; }
    public string Art { get; set; }
    public string Schluessel { get; set; }
    public string Status { get; set; }
    public string? FestgeschriebenAm { get; set; }
    public string? FestgeschriebenVon { get; set; }
}

public sealed class DokumentZeile
{
    public long Id { get; set; }
    public string Sha256 { get; set; }
    public string Dateiname { get; set; }
    public string RelativerPfad { get; set; }
    public long Groesse { get; set; }
    public string Mime { get; set; }
    public string ImportiertAm { get; set; }
}

public sealed class ImportLaufZeile
{
    public long Id { get; set; }
    public string Art { get; set; }
    public long? ProfilId { get; set; }
    public string Datei { get; set; }
    public string DateiSha256 { get; set; }
    public string Zeitpunkt { get; set; }
    public long AnzahlOk { get; set; }
    public long AnzahlDubletten { get; set; }
    public long AnzahlFehler { get; set; }
}

public sealed class ProfilZeile
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string ProfilJson { get; set; }
}

public sealed class KursZeile
{
    public long Id { get; set; }
    public string Waehrung { get; set; }
    public long Jahr { get; set; }
    public long Monat { get; set; }
    public string Kurs { get; set; }
    public string Quelle { get; set; }
    public long? ImportLaufId { get; set; }
}

public sealed class ProtokollZeile
{
    public long Nr { get; set; }
    public string Zeitpunkt { get; set; }
    public string Benutzer { get; set; }
    public string Entitaet { get; set; }
    public string EntitaetId { get; set; }
    public string Aktion { get; set; }
    public string? Feld { get; set; }
    public string? Alt { get; set; }
    public string? Neu { get; set; }
    public string Begruendung { get; set; }
    public string PrevHash { get; set; }
    public string Hash { get; set; }
}
#pragma warning restore CS8618
