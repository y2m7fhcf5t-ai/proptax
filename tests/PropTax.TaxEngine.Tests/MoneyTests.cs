using PropTax.Domain.Werte;

namespace PropTax.TaxEngine.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("91.665", "91.67")]
    [InlineData("91.664", "91.66")]
    [InlineData("-0.005", "-0.01")]
    public void Rundung_ist_kaufmaennisch(string roh, string erwartet) =>
        Assert.Equal(decimal.Parse(erwartet, System.Globalization.CultureInfo.InvariantCulture),
            new Money(decimal.Parse(roh, System.Globalization.CultureInfo.InvariantCulture)).Gerundet().Betrag);

    [Fact]
    public void Abnahmefall1_Umrechnung_99_USD_zu_1_08()
    {
        var eur = new CurrencyAmount(99.00m, "USD").InEuro(1.0800m).Gerundet();
        Assert.Equal(91.67m, eur.Betrag);
    }

    [Fact]
    public void Cent_Roundtrip() => Assert.Equal(new Money(91.67m), Money.AusCent(new Money(91.67m).InCent()));

    [Fact]
    public void Ungerundeter_Betrag_darf_nicht_als_Cent_gespeichert_werden() =>
        Assert.Throws<InvalidOperationException>(() => new Money(1.005m).InCent());
}
