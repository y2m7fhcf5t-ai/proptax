namespace PropTax.Domain.Werte;

/// <summary>Betrag in Fremd- oder Heimatwährung (ISO 4217).</summary>
public readonly record struct CurrencyAmount(decimal Betrag, string Waehrung)
{
    /// <summary>
    /// Umrechnung in EUR bei Kursangabe "1 EUR = kurs Fremdwährung" (BMF-Notation).
    /// Ergebnis ungerundet; gerundet wird je Beleg durch den Aufrufer.
    /// </summary>
    public Money InEuro(decimal kursFremdwaehrungJeEuro)
    {
        if (Waehrung == "EUR") return new Money(Betrag);
        if (kursFremdwaehrungJeEuro <= 0m)
            throw new ArgumentOutOfRangeException(nameof(kursFremdwaehrungJeEuro));
        return new Money(Betrag / kursFremdwaehrungJeEuro);
    }
}
