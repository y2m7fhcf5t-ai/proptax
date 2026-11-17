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
    public static GrenzStatus Pruefe(IEnumerable<UmsatzPosition> positionen, int jahr, Money vorjahresumsatz,
        IRegelwerk regelwerk, decimal warnschwelle = 0.8m) => throw new NotImplementedException();
}
