using Dapper;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Persistence.Tests;

public class VerschluesselungTests : IDisposable
{
    private readonly TempDb _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Verschluesseln_Passwort_aendern_Entschluesseln()
    {
        Assert.False(Datenbank.IstVerschluesselt(_t.Db.Pfad));
        Assert.True(_t.Db.Lesbar());

        var enc = _t.Db.Umschluesseln("richtig-geheim-123");
        Assert.True(enc.MitPasswort);
        Assert.True(Datenbank.IstVerschluesselt(_t.Db.Pfad));
        Assert.False(new Datenbank(_t.Db.Pfad).Lesbar());
        Assert.False(new Datenbank(_t.Db.Pfad, "falsch").Lesbar());
        Assert.True(enc.Lesbar());
        using (var s = enc.Sitzung()) Assert.Equal(2, s.Stammdaten.Betriebe().Count);

        var neu = enc.Umschluesseln("noch-geheimer-456");
        Assert.False(enc.Lesbar());
        Assert.True(neu.Lesbar());
        Assert.Equal(3, neu.Migriere()); // Migration funktioniert verschlüsselt

        var klar = neu.Umschluesseln(null);
        Assert.False(Datenbank.IstVerschluesselt(_t.Db.Pfad));
        Assert.True(new Datenbank(_t.Db.Pfad).Lesbar());
        Assert.False(klar.MitPasswort);
    }

    [Fact]
    public void Sicherung_bleibt_verschluesselt_und_konsistent()
    {
        var enc = _t.Db.Umschluesseln("pw-mit-'quote'-1");
        var ziel = Path.Combine(_t.Ordner, "kopie.db");
        enc.SichernNach(ziel);
        Assert.True(Datenbank.IstVerschluesselt(ziel));
        Assert.False(new Datenbank(ziel).Lesbar());
        var kopie = new Datenbank(ziel, "pw-mit-'quote'-1");
        using var c = kopie.Oeffne();
        Assert.Equal(5, c.ExecuteScalar<long>("SELECT COUNT(*) FROM geschaeftspartner WHERE ist_prop_firma=1"));
        using var s = kopie.Sitzung();
        Assert.True(s.Protokoll.PruefeKette());
    }

    [Fact]
    public void Unverschluesselte_Sicherung_und_Header_Erkennung()
    {
        var ziel = Path.Combine(_t.Ordner, "klar.db");
        _t.Db.SichernNach(ziel);
        _t.Db.SichernNach(ziel); // überschreibt
        Assert.False(Datenbank.IstVerschluesselt(ziel));
        Assert.False(Datenbank.IstVerschluesselt(Path.Combine(_t.Ordner, "gibtsnicht.db")));
        File.WriteAllText(Path.Combine(_t.Ordner, "kurz.db"), "x");
        Assert.False(Datenbank.IstVerschluesselt(Path.Combine(_t.Ordner, "kurz.db")));
        Assert.Equal(5, new Datenbank(ziel).Sitzung().Stammdaten.Partner().Count);
    }
}
