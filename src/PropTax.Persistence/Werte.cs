using System.Globalization;
using PropTax.Domain.Werte;

namespace PropTax.Persistence;

/// <summary>Konvertierungen zwischen Domänenwerten und Speicherformaten (docs/datenmodell.md).</summary>
public static class Werte
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Datum(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static string? Datum(DateOnly? d) => d is null ? null : Datum(d.Value);
    public static DateOnly Datum(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", Inv);
    public static DateOnly? DatumOpt(string? s) => s is null ? null : Datum(s);

    public static string Zeit(DateTimeOffset z) => z.ToUniversalTime().ToString("O", Inv);
    public static DateTimeOffset Zeit(string s) => DateTimeOffset.Parse(s, Inv, DateTimeStyles.RoundtripKind);

    public static string Dez(decimal d) => d.ToString(Inv);
    public static string? Dez(decimal? d) => d?.ToString(Inv);
    public static decimal Dez(string s) => decimal.Parse(s, NumberStyles.Number, Inv);
    public static decimal? DezOpt(string? s) => s is null ? null : Dez(s);

    /// <summary>Betrag → ganze Cent; mehr als 2 Nachkommastellen sind unzulässig.</summary>
    public static long Cent(decimal betrag)
    {
        var c = betrag * 100m;
        if (c != decimal.Truncate(c))
            throw new ArgumentException($"Betrag {betrag.ToString(Inv)} hat mehr als zwei Nachkommastellen.");
        return decimal.ToInt64(c);
    }

    public static long Cent(Money m) => Cent(m.Betrag);
    public static long? Cent(Money? m) => m is null ? null : Cent(m.Value.Betrag);
    public static decimal AusCent(long c) => c / 100m;
    public static Money MoneyAusCent(long c) => new(c / 100m);
    public static Money? MoneyAusCent(long? c) => c is null ? null : new Money(c.Value / 100m);
}
