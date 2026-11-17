using PropTax.Domain;

namespace PropTax.TaxEngine.Regelwerk;

/// <summary>Quelle aller fachlichen Parameter. Fehlende Daten führen zu <see cref="RegelwerkException"/>, nie zu Defaults.</summary>
public interface IRegelwerk
{
    IReadOnlyList<Steuerregel> Regeln();
    decimal Satz(string schluessel, DateOnly datum);
    decimal Kurs(string waehrung, int jahr, int monat, Kursquelle quelle);
    Kennzahlzuordnung Kennzahl(int jahr, string schluessel);
    Besteuerungsart Besteuerungsart(DateOnly datum);
    Grenzwert19 Grenzwert19(int jahr);
    string Einstellung(string schluessel, DateOnly datum);
}
