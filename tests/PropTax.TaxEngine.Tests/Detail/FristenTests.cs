using PropTax.Domain;
using static PropTax.TaxEngine.Tests.T;

namespace PropTax.TaxEngine.Tests.Detail;

public class FristenTests
{
    [Theory]
    [InlineData(2019, 4, 21)]
    [InlineData(2020, 4, 12)]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void Ostersonntag(int j, int m, int t) => Assert.Equal(D(j, m, t), Feiertage.Ostersonntag(j));

    [Fact]
    public void Bundeseinheitliche_Feiertage_2026()
    {
        var f = Feiertage.Bundeseinheitlich(2026);
        Assert.Equal(9, f.Count);
        Assert.Equal("Karfreitag", f[D(2026, 4, 3)]);
        Assert.Equal("Ostermontag", f[D(2026, 4, 6)]);
        Assert.Equal("Christi Himmelfahrt", f[D(2026, 5, 14)]);
        Assert.Equal("Pfingstmontag", f[D(2026, 5, 25)]);
        Assert.Equal("Tag der Deutschen Einheit", f[D(2026, 10, 3)]);
        Assert.True(f.ContainsKey(D(2026, 1, 1)) && f.ContainsKey(D(2026, 5, 1)) && f.ContainsKey(D(2026, 12, 25)) && f.ContainsKey(D(2026, 12, 26)));
    }

    [Theory]
    [InlineData(2026, 1, 2026, 4, 10, null)]                       // Freitag
    [InlineData(2025, 4, 2026, 1, 12, "Samstag, Sonntag")]           // 10.01.2026 = Samstag
    [InlineData(2026, 3, 2026, 10, 12, "Samstag, Sonntag")]
    [InlineData(2020, 1, 2020, 4, 14, "Karfreitag, Samstag, Sonntag, Ostermontag")]
    public void VA_Faelligkeit_Quartal(int jahr, int q, int fj, int fm, int ft, string? grund)
    {
        var f = Fristen.VaFaelligkeit(UstZeitraum.Quartal(jahr, q), 10, false);
        Assert.Equal(D(fj, fm, ft), f.Tatsaechlich);
        Assert.Equal(grund, f.Verschiebungsgrund);
        Assert.Equal(f.Verschiebungsgrund is null, f.Gesetzlich == f.Tatsaechlich);
    }

    [Fact]
    public void Dauerfristverlaengerung_und_Monat()
    {
        Assert.Equal(D(2026, 5, 11), Fristen.VaFaelligkeit(UstZeitraum.Quartal(2026, 1), 10, true).Tatsaechlich); // 10.05. = Sonntag
        Assert.Equal(D(2026, 3, 10), Fristen.VaFaelligkeit(new UstZeitraum(2026, ZeitraumArt.Monat, 2), 10, false).Tatsaechlich);
        Assert.Equal(D(2026, 3, 31), Fristen.Ende(UstZeitraum.Quartal(2026, 1)));
        Assert.Equal(D(2026, 12, 31), Fristen.Ende(new UstZeitraum(2026, ZeitraumArt.Jahr, 1)));
        Assert.Equal(D(2026, 2, 28), Fristen.Ende(new UstZeitraum(2026, ZeitraumArt.Monat, 2)));
        Assert.Throws<ArgumentException>(() => Fristen.VaFaelligkeit(new UstZeitraum(2026, ZeitraumArt.Jahr, 1), 10, false));
    }

    [Fact]
    public void Werktag()
    {
        Assert.True(Feiertage.IstWerktag(D(2026, 4, 10)));
        Assert.False(Feiertage.IstWerktag(D(2026, 4, 6)));
        Assert.False(Feiertage.IstWerktag(D(2026, 4, 11)));
    }
}
