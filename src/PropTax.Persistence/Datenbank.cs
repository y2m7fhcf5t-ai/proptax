using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PropTax.Persistence;

/// <summary>Zugriff auf die SQLite-Datei inkl. versionierter Schema-Migration (eingebettete SQL-Skripte).</summary>
public sealed partial class Datenbank
{
    static Datenbank() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public Datenbank(string pfad) => Pfad = pfad;

    public string Pfad { get; }

    /// <summary>
    /// Öffnet eine Verbindung mit aktivierten Fremdschlüsseln. Pooling ist aus, damit die Datei nach Gebrauch
    /// freigegeben wird (Backup/Restore). Verschlüsselung (SQLCipher) wird in Phase 5 hier ergänzt.
    /// </summary>
    public SqliteConnection Oeffne()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Pfad, ForeignKeys = true, Pooling = false };
        var c = new SqliteConnection(cs.ToString());
        c.Open();
        return c;
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
