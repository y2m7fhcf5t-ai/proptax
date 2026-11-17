using System.Globalization;
using PropTax.Domain;

namespace PropTax.TaxEngine.Regelwerk;

/// <summary>Regelwerk im Speicher (Startwerte, Tests). Die Persistenz liefert später eine DB-gestützte Implementierung.</summary>
public sealed class InMemoryRegelwerk : IRegelwerk
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public List<Steuerregel> RegelListe { get; } = [];
    public List<Steuersatz> Steuersaetze { get; } = [];
    public List<Kennzahlzuordnung> Kennzahlen { get; } = [];
    public List<Wechselkurs> Kurse { get; } = [];
    public List<Grenzwert19> Grenzwerte19 { get; } = [];
    public List<BesteuerungsartAb> Besteuerungsarten { get; } = [];
    public List<Einstellung> Einstellungen { get; } = [];

    public InMemoryRegelwerk SetzeKurs(string waehrung, int jahr, int monat, decimal kurs, Kursquelle quelle = Kursquelle.Bmf)
    {
        Kurse.RemoveAll(k => k.Waehrung == waehrung && k.Jahr == jahr && k.Monat == monat && k.Quelle == quelle);
        Kurse.Add(new Wechselkurs(waehrung, jahr, monat, kurs, quelle));
        return this;
    }

    public InMemoryRegelwerk SetzeEinstellung(string schluessel, string wert, DateOnly? ab = null)
    {
        Einstellungen.Add(new Einstellung(schluessel, wert, ab ?? DateOnly.MinValue));
        return this;
    }

    public InMemoryRegelwerk SetzeBesteuerungsart(DateOnly ab, Besteuerungsart art)
    {
        Besteuerungsarten.Add(new BesteuerungsartAb(ab, art));
        return this;
    }

    public IReadOnlyList<Steuerregel> Regeln() => RegelListe;

    public decimal Satz(string schluessel, DateOnly datum) =>
        Steuersaetze.SingleOrDefault(s => s.Schluessel == schluessel && s.GueltigAb <= datum && (s.GueltigBis is null || datum <= s.GueltigBis))?.Satz
        ?? throw new RegelwerkException($"Kein Steuersatz '{schluessel}' gültig am {datum.ToString("d", De)}.");

    public decimal Kurs(string waehrung, int jahr, int monat, Kursquelle quelle) =>
        Kurse.SingleOrDefault(k => k.Waehrung == waehrung && k.Jahr == jahr && k.Monat == monat && k.Quelle == quelle)?.Kurs
        ?? throw new RegelwerkException($"Kein {quelle}-Kurs für {waehrung} {monat:00}/{jahr} gepflegt.");

    public Kennzahlzuordnung Kennzahl(int jahr, string schluessel) =>
        Kennzahlen.SingleOrDefault(k => k.Jahr == jahr && k.Schluessel == schluessel)
        ?? throw new RegelwerkException($"Keine Kennzahlzuordnung '{schluessel}' für {jahr} gepflegt.");

    public Besteuerungsart Besteuerungsart(DateOnly datum) =>
        Besteuerungsarten.Where(b => b.Ab <= datum).OrderBy(b => b.Ab).LastOrDefault()?.Art
        ?? throw new RegelwerkException($"Keine Besteuerungsart für {datum.ToString("d", De)} festgelegt.");

    public Grenzwert19 Grenzwert19(int jahr) =>
        Grenzwerte19.SingleOrDefault(g => g.Jahr == jahr)
        ?? throw new RegelwerkException($"Keine §19-Grenzwerte für {jahr} gepflegt.");

    public string Einstellung(string schluessel, DateOnly datum) =>
        Einstellungen.Where(e => e.Schluessel == schluessel && e.Ab <= datum).OrderBy(e => e.Ab).LastOrDefault()?.Wert
        ?? throw new RegelwerkException($"Einstellung '{schluessel}' ist nicht gesetzt.");
}
