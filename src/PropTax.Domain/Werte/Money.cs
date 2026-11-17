namespace PropTax.Domain.Werte;

/// <summary>EUR-Betrag. Ausschließlich decimal; Rundung kaufmännisch auf 2 Stellen.</summary>
public readonly record struct Money(decimal Betrag)
{
    public static Money Null => new(0m);

    public static Money Euro(decimal betrag) => new(betrag);

    /// <summary>Kaufmännische Rundung (Midpoint AwayFromZero) auf Cent.</summary>
    public Money Gerundet() => new(Math.Round(Betrag, 2, MidpointRounding.AwayFromZero));

    /// <summary>Speicherformat: ganze Cent (exakt, nur für gerundete Beträge).</summary>
    public long InCent()
    {
        var g = Gerundet().Betrag;
        if (g != Betrag)
            throw new InvalidOperationException("Nur gerundete Beträge dürfen als Cent gespeichert werden.");
        return decimal.ToInt64(g * 100m);
    }

    public static Money AusCent(long cent) => new(cent / 100m);

    public static Money operator +(Money a, Money b) => new(a.Betrag + b.Betrag);
    public static Money operator -(Money a, Money b) => new(a.Betrag - b.Betrag);
    public static Money operator -(Money a) => new(-a.Betrag);

    public override string ToString() =>
        Betrag.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " €";
}
