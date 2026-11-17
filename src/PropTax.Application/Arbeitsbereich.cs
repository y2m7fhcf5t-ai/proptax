using PropTax.Persistence;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

/// <summary>
/// Ein Datenordner = eine Buchhaltung: <c>proptax.db</c> + <c>belege/</c>. Einstiegspunkt für UI und Tests.
/// </summary>
public sealed class Arbeitsbereich
{
    public const string DateinameDb = "proptax.db";

    private Arbeitsbereich(string datenordner, string benutzer, TimeProvider zeit, string? passwort)
    {
        Datenordner = Path.GetFullPath(datenordner);
        Benutzer = benutzer;
        Zeit = zeit;
        Db = new Datenbank(Path.Combine(Datenordner, DateinameDb), passwort);
    }

    public static string StandardOrdner =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PropTax");

    public string Datenordner { get; }
    public string BelegOrdner => Path.Combine(Datenordner, "belege");
    public string SicherungsOrdner => Path.Combine(Datenordner, "backup");
    public string DbPfad => Path.Combine(Datenordner, DateinameDb);
    public bool IstVerschluesselt => Datenbank.IstVerschluesselt(DbPfad);
    public static string Version =>
        typeof(Arbeitsbereich).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0";
    public string Benutzer { get; }
    public TimeProvider Zeit { get; }
    public int SchemaVersion { get; private set; }
    internal Datenbank Db { get; set; }

    /// <summary>Schließt alle gepoolten DB-Verbindungen (z. B. vor dem Löschen/Verschieben des Datenordners).</summary>
    public static void VerbindungenSchliessen() => Datenbank.Freigeben();

    /// <summary>True, wenn die Datenbank im Ordner verschlüsselt ist (Passwortabfrage beim Start nötig).</summary>
    public static bool BenoetigtPasswort(string datenordner) => Datenbank.IstVerschluesselt(Path.Combine(datenordner, DateinameDb));

    /// <summary>Höchste Schema-Version, die dieses Programm kennt.</summary>
    public static int UnterstuetzteSchemaVersion => Datenbank.Skripte().Max(s => s.Version);
    internal DateTimeOffset Jetzt => Zeit.GetUtcNow();

    /// <summary>
    /// Öffnet (und initialisiert bei Bedarf) den Datenordner: Migration + Startdaten. Ist die Datenbank verschlüsselt,
    /// wird das Passwort benötigt (<see cref="PasswortErforderlichException"/>).
    /// </summary>
    public static Arbeitsbereich Oeffne(string datenordner, string benutzer = "Inhaber", TimeProvider? zeit = null, string? passwort = null)
    {
        var ab = new Arbeitsbereich(datenordner, benutzer, zeit ?? TimeProvider.System, passwort);
        if (ab.IstVerschluesselt && (string.IsNullOrEmpty(passwort) || !ab.Db.Lesbar()))
            throw new PasswortErforderlichException(string.IsNullOrEmpty(passwort)
                ? "Die Datenbank ist verschlüsselt. Bitte Passwort eingeben."
                : "Das Passwort ist falsch.");
        if (!ab.IstVerschluesselt && !string.IsNullOrEmpty(passwort))
            ab.Db = new Datenbank(ab.DbPfad);
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
    public AuswertungService Auswertungen => new(this);
    public SicherungService Sicherungen => new(this);
    public SicherheitService Sicherheit => new(this);

    internal Sitzung Sitzung() => Db.Sitzung();
}

/// <summary>Fachlicher Fehler, der dem Nutzer als Meldung angezeigt wird.</summary>
public class FachlicherFehler(string message) : InvalidOperationException(message);

/// <summary>Die Datenbank ist verschlüsselt und das Passwort fehlt oder ist falsch.</summary>
public sealed class PasswortErforderlichException(string message) : FachlicherFehler(message);
