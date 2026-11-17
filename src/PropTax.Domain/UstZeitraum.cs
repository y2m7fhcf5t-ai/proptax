namespace PropTax.Domain;

/// <summary>Umsatzsteuerlicher Voranmeldungs- bzw. Erklärungszeitraum.</summary>
public readonly record struct UstZeitraum(int Jahr, ZeitraumArt Art, int Nummer)
{
    public static UstZeitraum Aus(DateOnly datum, ZeitraumArt art) => art switch
    {
        ZeitraumArt.Monat => new(datum.Year, art, datum.Month),
        ZeitraumArt.Quartal => new(datum.Year, art, (datum.Month - 1) / 3 + 1),
        ZeitraumArt.Jahr => new(datum.Year, art, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(art)),
    };

    public static UstZeitraum Quartal(int jahr, int quartal) => new(jahr, ZeitraumArt.Quartal, quartal);

    public override string ToString() => Art switch
    {
        ZeitraumArt.Monat => $"{Jahr}-{Nummer:00}",
        ZeitraumArt.Quartal => $"{Jahr}-Q{Nummer}",
        _ => $"{Jahr}",
    };
}
