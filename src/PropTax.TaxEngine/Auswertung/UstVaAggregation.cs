using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.TaxEngine.Auswertung;

/// <summary>Rundung der Bemessungsgrundlagen für die Formularansicht (O-08).</summary>
public enum FormularRundung { Abschneiden, Kaufmaennisch }

public sealed record KennzahlSumme(string Kennzahl, KennzahlArt Art, Money Betrag, decimal Formularwert, IReadOnlyList<SteuerErgebnis> Belege);

public sealed record UstVaVorschau(
    UstZeitraum Zeitraum,
    IReadOnlyList<KennzahlSumme> Kennzahlen,
    Money Steuer,
    Money Vorsteuer,
    Money Zahllast,
    IReadOnlyList<SteuerErgebnis> Belege)
{
    public KennzahlSumme? Kz(string kennzahl) => Kennzahlen.FirstOrDefault(k => k.Kennzahl == kennzahl);
    public Money Betrag(string kennzahl) => Kz(kennzahl)?.Betrag ?? Money.Null;
}

public static class UstVaAggregation
{
    public static UstVaVorschau Berechne(IEnumerable<SteuerErgebnis> ergebnisse, UstZeitraum zeitraum,
        FormularRundung rundung = FormularRundung.Abschneiden) => throw new NotImplementedException();
}
