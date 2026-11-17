using PropTax.Domain.Werte;

namespace PropTax.App;

internal static class SelfTest
{
    public static int Ausfuehren()
    {
        var eur = new CurrencyAmount(99.00m, "USD").InEuro(1.0800m).Gerundet();
        var ok = eur.Betrag == 91.67m;
        Console.WriteLine(ok ? $"SELFTEST OK ({eur})" : $"SELFTEST FEHLER ({eur})");
        return ok ? 0 : 1;
    }
}
