using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.TaxEngine.Auswertung;

public sealed record UmsatzPosition(string BelegRef, string Betrieb, DateOnly Datum, Money Betrag)
{
    public static IEnumerable<UmsatzPosition> Aus(IEnumerable<SteuerErgebnis> ergebnisse) =>
        ergebnisse.Where(e => e.Umsatz19 != Money.Null)
            .Select(e => new UmsatzPosition(e.BelegRef, e.Betrieb, e.Entstehungsdatum, e.Umsatz19));
}

public enum GrenzStufe { Warnschwelle, Ueberschritten }

public sealed record GrenzWarnung(GrenzStufe Stufe, DateOnly Datum, string BelegRef, Money SummeNachBeleg, string Text);

public sealed record Umschaltvorschlag(DateOnly AbDatum, string? BelegRef, string Begruendung);

public sealed record GrenzStatus(
    int Jahr,
    Money Summe,
    Money Grenze,
    decimal Quote,
    bool VorjahrUeberschritten,
    IReadOnlyList<GrenzWarnung> Warnungen,
    Umschaltvorschlag? Vorschlag,
    IReadOnlyDictionary<string, Money> JeBetrieb);

/// <summary>§19-Grenzmonitor über alle Betriebe des Unternehmens.</summary>
public static class Kleinunternehmergrenze
{
    /// <summary>
    /// Kumuliert die §19-Umsätze des Jahres chronologisch über alle Betriebe.
    /// Warnung beim Erreichen der Warnschwelle (Standard 80 %) und beim Überschreiten der Grenze des laufenden Jahres;
    /// Umschaltvorschlag ab dem Umsatz, mit dem die Grenze überschritten wird (§19 Abs. 1 UStG n. F., O-02).
    /// </summary>
    public static GrenzStatus Pruefe(IEnumerable<UmsatzPosition> positionen, int jahr, Money vorjahresumsatz,
        IRegelwerk regelwerk, decimal warnschwelle = 0.8m)
    {
        var grenzen = regelwerk.Grenzwert19(jahr);
        var grenze = grenzen.GrenzeLaufendesJahr;
        var schwelle = grenze.Betrag * warnschwelle;
        var liste = positionen.Where(p => p.Datum.Year == jahr)
            .OrderBy(p => p.Datum).ThenBy(p => p.BelegRef, StringComparer.Ordinal).ToList();

        var warnungen = new List<GrenzWarnung>();
        Umschaltvorschlag? vorschlag = null;
        var vorjahrUeberschritten = vorjahresumsatz.Betrag > grenzen.GrenzeVorjahr.Betrag;
        if (vorjahrUeberschritten)
            vorschlag = new Umschaltvorschlag(new DateOnly(jahr, 1, 1), null,
                $"Vorjahresumsatz {vorjahresumsatz} > {grenzen.GrenzeVorjahr}: Kleinunternehmerregelung im Jahr {jahr} nicht anwendbar.");

        var summe = Money.Null;
        foreach (var p in liste)
        {
            var vorher = summe;
            summe += p.Betrag;
            if (vorher.Betrag < schwelle && summe.Betrag >= schwelle)
                warnungen.Add(new GrenzWarnung(GrenzStufe.Warnschwelle, p.Datum, p.BelegRef, summe,
                    $"{warnschwelle:P0} der §19-Grenze ({grenze}) erreicht."));
            if (vorher.Betrag <= grenze.Betrag && summe.Betrag > grenze.Betrag)
            {
                warnungen.Add(new GrenzWarnung(GrenzStufe.Ueberschritten, p.Datum, p.BelegRef, summe,
                    $"§19-Grenze {grenze} überschritten."));
                vorschlag ??= new Umschaltvorschlag(p.Datum, p.BelegRef,
                    $"Grenze {grenze} mit Beleg {p.BelegRef} überschritten: Regelbesteuerung ab diesem Umsatz (TODO-STEUER O-02).");
            }
        }

        var jeBetrieb = liste.GroupBy(p => p.Betrieb)
            .ToDictionary(g => g.Key, g => g.Aggregate(Money.Null, (s, p) => s + p.Betrag));
        var quote = grenze.Betrag == 0m ? 0m : Math.Round(summe.Betrag / grenze.Betrag, 4, MidpointRounding.AwayFromZero);
        return new GrenzStatus(jahr, summe, grenze, quote, vorjahrUeberschritten, warnungen, vorschlag, jeBetrieb);
    }
}
