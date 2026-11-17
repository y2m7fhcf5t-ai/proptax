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
    /// <summary>Summiert alle Ergebnisse des Zeitraums je Kennzahl; Zahllast = Σ Steuer − Σ Vorsteuer.</summary>
    public static UstVaVorschau Berechne(IEnumerable<SteuerErgebnis> ergebnisse, UstZeitraum zeitraum,
        FormularRundung rundung = FormularRundung.Abschneiden)
    {
        var belege = ergebnisse.Where(e => e.Zeitraum == zeitraum).ToList();

        var kennzahlen = belege
            .SelectMany(e => e.Kennzahlen.Select(k => (Ergebnis: e, Position: k)))
            .GroupBy(x => (x.Position.Kennzahl, x.Position.Art))
            .Select(g =>
            {
                var summe = g.Aggregate(Money.Null, (s, x) => s + x.Position.Betrag);
                return new KennzahlSumme(g.Key.Kennzahl, g.Key.Art, summe, Formularwert(summe, g.Key.Art, rundung),
                    g.Select(x => x.Ergebnis).Distinct().ToList());
            })
            .OrderBy(k => int.TryParse(k.Kennzahl, out var n) ? n : int.MaxValue)
            .ThenBy(k => k.Kennzahl, StringComparer.Ordinal)
            .ToList();

        var steuer = belege.Aggregate(Money.Null, (s, e) => s + e.Steuer);
        var vorsteuer = belege.Aggregate(Money.Null, (s, e) => s + e.Vorsteuer);
        return new UstVaVorschau(zeitraum, kennzahlen, steuer, vorsteuer, steuer - vorsteuer, belege);
    }

    /// <summary>
    /// TODO-STEUER O-08: Bemessungsgrundlagen im Formular in vollen Euro; Rundungsart konfigurierbar.
    /// Steuer- und Vorsteuerbeträge bleiben centgenau.
    /// </summary>
    private static decimal Formularwert(Money summe, KennzahlArt art, FormularRundung rundung) =>
        art != KennzahlArt.Bemessungsgrundlage ? summe.Betrag
        : rundung == FormularRundung.Abschneiden ? Math.Truncate(summe.Betrag)
        : Math.Round(summe.Betrag, 0, MidpointRounding.AwayFromZero);
}
