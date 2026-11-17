using PropTax.Persistence;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

/// <summary>
/// Ein Datenordner = eine Buchhaltung: <c>proptax.db</c> + <c>belege/</c>. Einstiegspunkt für UI und Tests.
/// </summary>
public sealed class Arbeitsbereich
{
    public const string DateinameDb = "proptax.db";

    private Arbeitsbereich(string datenordner, string benutzer, TimeProvider zeit)
    {
        Datenordner = Path.GetFullPath(datenordner);
        Benutzer = benutzer;
        Zeit = zeit;
        Db = new Datenbank(Path.Combine(Datenordner, DateinameDb));
    }

    public static string StandardOrdner =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PropTax");

    public string Datenordner { get; }
    public string BelegOrdner => Path.Combine(Datenordner, "belege");
    public string Benutzer { get; }
    public TimeProvider Zeit { get; }
    public int SchemaVersion { get; private set; }
    internal Datenbank Db { get; }
    internal DateTimeOffset Jetzt => Zeit.GetUtcNow();

    /// <summary>Öffnet (und initialisiert bei Bedarf) den Datenordner: Migration + Startdaten.</summary>
    public static Arbeitsbereich Oeffne(string datenordner, string benutzer = "Inhaber", TimeProvider? zeit = null)
    {
        var ab = new Arbeitsbereich(datenordner, benutzer, zeit ?? TimeProvider.System);
        Directory.CreateDirectory(ab.Datenordner);
        Directory.CreateDirectory(ab.BelegOrdner);
        ab.SchemaVersion = ab.Db.Migriere(ab.Zeit);
        using var s = ab.Db.Sitzung();
        Startdaten.Einspielen(s, JsonRegelwerk.LadeStartwerte(), ab.Jetzt, benutzer);
        s.Commit();
        return ab;
    }

    public StammdatenService Stammdaten => new(this);
    public BelegService Belege => new(this);
    public PeriodenService Perioden => new(this);
    public KursService Kurse => new(this);
    public ImportService Import => new(this);
    public DokumentService Dokumente => new(this);
    public EinstellungenService Einstellungen => new(this);
    public ProtokollService Protokoll => new(this);

    internal Sitzung Sitzung() => Db.Sitzung();
}

/// <summary>Fachlicher Fehler, der dem Nutzer als Meldung angezeigt wird.</summary>
public sealed class FachlicherFehler(string message) : InvalidOperationException(message);
