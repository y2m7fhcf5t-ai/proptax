using PropTax.Domain;

namespace PropTax.TaxEngine.Regelwerk;

public sealed class InMemoryRegelwerk : IRegelwerk
{
    public List<Steuerregel> RegelListe { get; } = [];
    public List<Steuersatz> Steuersaetze { get; } = [];
    public List<Kennzahlzuordnung> Kennzahlen { get; } = [];
    public List<Wechselkurs> Kurse { get; } = [];
    public List<Grenzwert19> Grenzwerte19 { get; } = [];
    public List<BesteuerungsartAb> Besteuerungsarten { get; } = [];
    public List<Einstellung> Einstellungen { get; } = [];

    public InMemoryRegelwerk SetzeKurs(string waehrung, int jahr, int monat, decimal kurs, Kursquelle quelle = Kursquelle.Bmf) => throw new NotImplementedException();
    public InMemoryRegelwerk SetzeEinstellung(string schluessel, string wert, DateOnly? ab = null) => throw new NotImplementedException();
    public InMemoryRegelwerk SetzeBesteuerungsart(DateOnly ab, Besteuerungsart art) => throw new NotImplementedException();

    public IReadOnlyList<Steuerregel> Regeln() => throw new NotImplementedException();
    public decimal Satz(string schluessel, DateOnly datum) => throw new NotImplementedException();
    public decimal Kurs(string waehrung, int jahr, int monat, Kursquelle quelle) => throw new NotImplementedException();
    public Kennzahlzuordnung Kennzahl(int jahr, string schluessel) => throw new NotImplementedException();
    public Besteuerungsart Besteuerungsart(DateOnly datum) => throw new NotImplementedException();
    public Grenzwert19 Grenzwert19(int jahr) => throw new NotImplementedException();
    public string Einstellung(string schluessel, DateOnly datum) => throw new NotImplementedException();
}
