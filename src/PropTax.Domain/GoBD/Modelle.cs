using PropTax.Domain.Werte;

namespace PropTax.Domain.GoBD;

/// <summary>Minimale Belegsicht für GoBD-Operationen (Erfassen, Ändern, Storno, Korrektur).</summary>
public sealed record Buchung(string BelegRef, DateOnly Datum, CurrencyAmount Betrag, string Text)
{
    public string? StornoVon { get; init; }
    public string? KorrekturVon { get; init; }
}

public interface IPeriodenStatus
{
    /// <summary>True, wenn der Zeitraum, in den das Datum fällt, festgeschrieben ist.</summary>
    bool IstFestgeschrieben(DateOnly datum);
}

public sealed record ProtokollEintrag(
    long Nr,
    DateTimeOffset Zeitpunkt,
    string Benutzer,
    string Entitaet,
    string EntitaetId,
    string Aktion,
    string? Feld,
    string? Alt,
    string? Neu,
    string Begruendung,
    string PrevHash,
    string Hash);

public sealed class PeriodeFestgeschriebenException(DateOnly datum)
    : InvalidOperationException($"Der Zeitraum mit Datum {datum:dd.MM.yyyy} ist festgeschrieben. Änderungen nur per Storno/Korrektur in offener Periode.")
{
    public DateOnly Datum { get; } = datum;
}

/// <summary>Senke für Protokolleinträge (im Speicher oder Datenbank).</summary>
public interface IProtokoll
{
    ProtokollEintrag Anhaengen(DateTimeOffset zeitpunkt, string benutzer, string entitaet, string entitaetId,
        string aktion, string? feld, string? alt, string? neu, string begruendung);
}
