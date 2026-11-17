using Microsoft.Data.Sqlite;
using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.Application.Tests;

internal sealed class FesteZeit(DateTimeOffset jetzt) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => jetzt;
}

public abstract class Basis : IDisposable
{
    protected static DateOnly D(int j, int m, int t) => new(j, m, t);

    protected Basis()
    {
        Ordner = Path.Combine(Path.GetTempPath(), "proptax-app-" + Guid.NewGuid().ToString("N"));
        Ab = Arbeitsbereich.Oeffne(Ordner, "Tester", new FesteZeit(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero)));
        PropTrading = Ab.Stammdaten.Betriebe().Single(b => b.Name == "Prop-Trading").Id;
        Onlinehandel = Ab.Stammdaten.Betriebe().Single(b => b.Name == "Onlinehandel").Id;
        var fn = Ab.Stammdaten.Partner().Single(p => p.Name == "Funded Next");
        FundedNext = Ab.Stammdaten.SpeicherePartner(fn with { LandIso = "AE" });
        IrischerAnbieter = Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "Data Feeds Ltd", LandIso = "IE" });
        Kunde = Ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "Endkunde", LandIso = "DE" });
        Ab.Kurse.Setze(new KursDaten("USD", 2026, 3, 1.0800m, Kursquelle.Bmf));
        Ab.Kurse.Setze(new KursDaten("USD", 2026, 4, 1.1000m, Kursquelle.Bmf));
    }

    protected string Ordner { get; }
    protected Arbeitsbereich Ab { get; }
    protected long PropTrading { get; }
    protected long Onlinehandel { get; }
    protected long FundedNext { get; }
    protected long IrischerAnbieter { get; }
    protected long Kunde { get; }

    protected BelegDaten Challenge(string nr = "C1", DateOnly? datum = null, decimal usd = 99m) => new()
    {
        Belegnummer = nr, BetriebId = PropTrading, PartnerId = FundedNext, Typ = Belegtyp.ChallengeGebuehr, Richtung = Richtung.Eingang,
        Rechnungsdatum = datum ?? D(2026, 3, 10), Leistungsdatum = datum ?? D(2026, 3, 10), Zahlungsdatum = datum ?? D(2026, 3, 10),
        Betrag = new CurrencyAmount(usd, "USD"),
    };

    protected long Gebucht(BelegDaten d)
    {
        var id = Ab.Belege.ErfasseEntwurf(d);
        Ab.Belege.Buche(id);
        return id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Ordner, true);
        GC.SuppressFinalize(this);
    }
}
