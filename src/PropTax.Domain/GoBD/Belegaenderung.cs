using System.Globalization;
using PropTax.Domain.Werte;

namespace PropTax.Domain.GoBD;

/// <summary>
/// Erzwingt die Periodenfestschreibung (GoBD): Erfassen und Ändern nur in offenen Zeiträumen;
/// festgeschriebene Belege werden ausschließlich durch Storno-/Korrekturbelege in offenen Zeiträumen berichtigt.
/// Jede Operation wird mit Begründung protokolliert.
/// </summary>
public sealed class Belegaenderung(IPeriodenStatus perioden, Protokoll protokoll, string benutzer, TimeProvider zeit)
{
    private const string Entitaet = "beleg";

    public Buchung Erfassen(Buchung beleg, string begruendung)
    {
        PruefeBegruendung(begruendung);
        PruefeOffen(beleg.Datum);
        Protokolliere(beleg.BelegRef, "ANLAGE", null, null, Beschreibe(beleg), begruendung);
        return beleg;
    }

    public Buchung Aendern(Buchung alt, Buchung neu, string begruendung)
    {
        PruefeBegruendung(begruendung);
        PruefeOffen(alt.Datum);
        PruefeOffen(neu.Datum);
        void Feld(string name, string a, string n)
        {
            if (a != n) Protokolliere(alt.BelegRef, "AENDERUNG", name, a, n, begruendung);
        }
        Feld(nameof(Buchung.Datum), alt.Datum.ToString("O", CultureInfo.InvariantCulture), neu.Datum.ToString("O", CultureInfo.InvariantCulture));
        Feld(nameof(Buchung.Betrag), Format(alt.Betrag), Format(neu.Betrag));
        Feld(nameof(Buchung.Text), alt.Text, neu.Text);
        return neu;
    }

    public Buchung Stornieren(Buchung original, DateOnly stornodatum, string begruendung)
    {
        PruefeBegruendung(begruendung);
        PruefeOffen(stornodatum);
        var storno = new Buchung(original.BelegRef + "-S", stornodatum,
            original.Betrag with { Betrag = -original.Betrag.Betrag }, "Storno: " + original.Text)
        { StornoVon = original.BelegRef };
        Protokolliere(original.BelegRef, "STORNO", null, Beschreibe(original), Beschreibe(storno), begruendung);
        return storno;
    }

    public (Buchung Storno, Buchung Neu) Korrigieren(Buchung original, Buchung korrigiert, string begruendung)
    {
        PruefeBegruendung(begruendung);
        PruefeOffen(korrigiert.Datum);
        var storno = Stornieren(original, korrigiert.Datum, begruendung);
        var neu = korrigiert with { KorrekturVon = original.BelegRef };
        Protokolliere(original.BelegRef, "KORREKTUR", null, Beschreibe(original), Beschreibe(neu), begruendung);
        return (storno, neu);
    }

    private void PruefeOffen(DateOnly datum)
    {
        if (perioden.IstFestgeschrieben(datum)) throw new PeriodeFestgeschriebenException(datum);
    }

    private static void PruefeBegruendung(string begruendung)
    {
        if (string.IsNullOrWhiteSpace(begruendung))
            throw new ArgumentException("Eine Begründung ist Pflicht.", nameof(begruendung));
    }

    private void Protokolliere(string id, string aktion, string? feld, string? alt, string? neu, string begruendung) =>
        protokoll.Anhaengen(zeit.GetUtcNow(), benutzer, Entitaet, id, aktion, feld, alt, neu, begruendung);

    private static string Format(CurrencyAmount b) => $"{b.Betrag.ToString(CultureInfo.InvariantCulture)} {b.Waehrung}";

    private static string Beschreibe(Buchung b) =>
        $"{b.BelegRef} | {b.Datum.ToString("O", CultureInfo.InvariantCulture)} | {Format(b.Betrag)} | {b.Text}";
}
