using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.TaxEngine.Tests;

internal static class T
{
    public static DateOnly D(int j, int m, int t) => new(j, m, t);

    /// <summary>Startwerte (KU) + Test-Kurse USD: 03/2026 = 1,08; 04/2026 = 1,10; 05/2026 = 1,08.</summary>
    public static InMemoryRegelwerk NeuesRegelwerk() => JsonRegelwerk.LadeStartwerte()
        .SetzeKurs("USD", 2026, 3, 1.0800m)
        .SetzeKurs("USD", 2026, 4, 1.1000m)
        .SetzeKurs("USD", 2026, 5, 1.0800m);

    public static BelegEingabe Challenge(string beleg = "C1", DateOnly? datum = null, decimal usd = 99.00m)
    {
        var d = datum ?? D(2026, 3, 10);
        return new BelegEingabe
        {
            BelegRef = beleg, Betrieb = "Prop-Trading", Typ = Belegtyp.ChallengeGebuehr, Richtung = Richtung.Eingang,
            PartnerKategorie = PartnerKategorie.Drittland, PartnerLand = "AE",
            Rechnungsdatum = d, Leistungsdatum = d, Zahlungsdatum = d, Betrag = new CurrencyAmount(usd, "USD"),
        };
    }

    public static BelegEingabe AboEu(string beleg = "A1", DateOnly? datum = null, decimal eur = 30.00m)
    {
        var d = datum ?? D(2026, 2, 1);
        return new BelegEingabe
        {
            BelegRef = beleg, Betrieb = "Prop-Trading", Typ = Belegtyp.AboSoftware, Richtung = Richtung.Eingang,
            PartnerKategorie = PartnerKategorie.Eu, PartnerLand = "IE",
            Rechnungsdatum = d, Leistungsdatum = d, Zahlungsdatum = d, Betrag = new CurrencyAmount(eur, "EUR"),
        };
    }

    public static BelegEingabe Payout(string beleg = "P1", decimal usd = 2000.00m) => new()
    {
        BelegRef = beleg, Betrieb = "Prop-Trading", Typ = Belegtyp.Payout, Richtung = Richtung.Ausgang,
        PartnerKategorie = PartnerKategorie.Drittland, PartnerLand = "US",
        Leistungsdatum = D(2026, 5, 15), Zahlungsdatum = D(2026, 5, 15), Betrag = new CurrencyAmount(usd, "USD"),
    };

    public static BelegEingabe Verkauf(string beleg, string betrieb, DateOnly datum, decimal eur) => new()
    {
        BelegRef = beleg, Betrieb = betrieb, Typ = Belegtyp.Verkauf, Richtung = Richtung.Ausgang,
        PartnerKategorie = PartnerKategorie.Inland, PartnerLand = "DE",
        Rechnungsdatum = datum, Leistungsdatum = datum, Zahlungsdatum = datum, Betrag = new CurrencyAmount(eur, "EUR"),
    };
}

internal sealed class FesteZeit(DateTimeOffset jetzt) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => jetzt;
}

internal sealed class Quartalsperioden(params UstZeitraum[] festgeschrieben) : IPeriodenStatus
{
    public bool IstFestgeschrieben(DateOnly datum) =>
        festgeschrieben.Contains(UstZeitraum.Aus(datum, ZeitraumArt.Quartal));
}
