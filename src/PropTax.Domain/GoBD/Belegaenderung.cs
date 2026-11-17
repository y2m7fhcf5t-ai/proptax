#pragma warning disable CS9113 // Stub (Phase 2, test-first)
namespace PropTax.Domain.GoBD;

/// <summary>Erzwingt Periodenfestschreibung: in festgeschriebenen Zeiträumen nur Storno/Korrektur.</summary>
public sealed class Belegaenderung(IPeriodenStatus perioden, Protokoll protokoll, string benutzer, TimeProvider zeit)
{
    public Buchung Erfassen(Buchung beleg, string begruendung) => throw new NotImplementedException();
    public Buchung Aendern(Buchung alt, Buchung neu, string begruendung) => throw new NotImplementedException();
    public Buchung Stornieren(Buchung original, DateOnly stornodatum, string begruendung) => throw new NotImplementedException();
    public (Buchung Storno, Buchung Neu) Korrigieren(Buchung original, Buchung korrigiert, string begruendung) => throw new NotImplementedException();
}
