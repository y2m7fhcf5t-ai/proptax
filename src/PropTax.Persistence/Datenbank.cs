using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PropTax.Persistence;

/// <summary>Zugriff auf die SQLite-Datei inkl. versionierter Schema-Migration (eingebettete SQL-Skripte).</summary>
public sealed partial class Datenbank
{
    static Datenbank() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    private readonly string? _passwort;

    public Datenbank(string pfad, string? passwort = null)
    {
        Pfad = pfad;
        _passwort = string.IsNullOrEmpty(passwort) ? null : passwort;
    }

    public string Pfad { get; }
    public bool MitPasswort => _passwort is not null;

    /// <summary>
    /// Öffnet eine Verbindung mit aktivierten Fremdschlüsseln. Bei Passwort wird die DB per SQLite3 Multiple Ciphers
    /// entschlüsselt (PRAGMA key). Pooling ist an (Schlüsselableitung je Verbindung ist teuer); vor Dateioperationen
    /// (Backup/Restore/Rekey) <see cref="Freigeben"/> aufrufen.
    /// </summary>
    public SqliteConnection Oeffne()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Pfad, ForeignKeys = true, Pooling = true };
        if (_passwort is not null) cs.Password = _passwort;
        var c = new SqliteConnection(cs.ToString());
        c.Open();
        return c;
    }

    /// <summary>Schließt alle gepoolten Verbindungen (Datei wird freigegeben).</summary>
    public static void Freigeben() => SqliteConnection.ClearAllPools();

    /// <summary>True, wenn die Datei existiert und nicht mit dem Klartext-Header „SQLite format 3“ beginnt.</summary>
    public static bool IstVerschluesselt(string pfad)
    {
        if (!File.Exists(pfad) || new FileInfo(pfad).Length < 16) return false;
        var kopf = new byte[16];
        // FileShare.ReadWrite: SQLite hält die Datei ggf. offen; unter Windows schlägt File.OpenRead dann fehl.
        using (var f = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) f.ReadExactly(kopf);
        return System.Text.Encoding.ASCII.GetString(kopf) != "SQLite format 3\0";
    }

    /// <summary>Prüft, ob die DB mit dem aktuellen Schlüssel lesbar ist.</summary>
    public bool Lesbar()
    {
        try
        {
            using var c = Oeffne();
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master");
            return true;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 26) // SQLITE_NOTADB: falscher/fehlender Schlüssel
        {
            Freigeben();
            return false;
        }
    }

    /// <summary>Setzt, ändert (neu ≠ null) oder entfernt (neu = null) den Schlüssel. Liefert die Datenbank mit neuem Schlüssel.</summary>
    public Datenbank Umschluesseln(string? neu)
    {
        using (var c = Oeffne())
            c.Execute($"PRAGMA rekey = '{(neu ?? "").Replace("'", "''")}'");
        Freigeben();
        return new Datenbank(Pfad, neu);
    }

    /// <summary>Konsistente Kopie per Online-Backup-API; das Ziel erhält denselben Schlüssel.</summary>
    public void SichernNach(string ziel)
    {
        if (File.Exists(ziel)) File.Delete(ziel);
        using var quelle = Oeffne();
        using var dst = new Datenbank(ziel, _passwort).Oeffne();
        quelle.BackupDatabase(dst);
        dst.Close();
        Freigeben();
    }

    public Sitzung Sitzung() => new(Oeffne());

    public static IReadOnlyList<(int Version, string Name, string Sql)> Skripte()
    {
        var asm = typeof(Datenbank).Assembly;
        return asm.GetManifestResourceNames()
            .Select(n => (Name: n, M: SkriptName().Match(n)))
            .Where(x => x.M.Success)
            .Select(x => (int.Parse(x.M.Groups[1].Value), x.Name, Lies(asm, x.Name)))
            .OrderBy(x => x.Item1)
            .ToList();
    }

    /// <summary>Spielt alle noch nicht angewandten Skripte in je einer Transaktion ein. Liefert die Schema-Version.</summary>
    public int Migriere(TimeProvider? zeit = null)
    {
        using var c = Oeffne();
        c.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, angewandt_am TEXT NOT NULL)");
        var aktuell = c.ExecuteScalar<long?>("SELECT MAX(version) FROM schema_version") ?? 0;
        foreach (var (version, _, sql) in Skripte().Where(s => s.Version > aktuell))
        {
            using var tx = c.BeginTransaction();
            c.Execute(sql, transaction: tx);
            c.Execute("INSERT INTO schema_version (version, angewandt_am) VALUES (@version, @am)",
                new { version, am = Werte.Zeit((zeit ?? TimeProvider.System).GetUtcNow()) }, tx);
            tx.Commit();
            aktuell = version;
        }
        return (int)aktuell;
    }

    private static string Lies(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    [GeneratedRegex(@"^PropTax\.Schema\.(\d{4})_.*\.sql$")]
    private static partial Regex SkriptName();
}

/// <summary>Unit of Work: eine Verbindung + Transaktion; Repositories teilen sie.</summary>
public sealed class Sitzung : IDisposable
{
    internal Sitzung(SqliteConnection verbindung)
    {
        Verbindung = verbindung;
        Transaktion = verbindung.BeginTransaction();
    }

    public SqliteConnection Verbindung { get; }
    public SqliteTransaction Transaktion { get; }

    public StammdatenRepository Stammdaten => new(this);
    public BelegRepository Belege => new(this);
    public PeriodenRepository Perioden => new(this);
    public DokumentRepository Dokumente => new(this);
    public ImportRepository Importe => new(this);
    public RegelwerkRepository Regelwerk => new(this);
    public SqliteProtokoll Protokoll => new(this);

    public void Commit() => Transaktion.Commit();

    public void Dispose()
    {
        Transaktion.Dispose();
        Verbindung.Dispose();
    }
}
