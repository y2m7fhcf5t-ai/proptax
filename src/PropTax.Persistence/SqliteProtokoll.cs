using Dapper;
using PropTax.Domain.GoBD;

namespace PropTax.Persistence;

/// <summary>Protokoll in der DB; Hash-Kette identisch zu <see cref="Protokoll"/> (Domain).</summary>
public sealed class SqliteProtokoll(Sitzung s) : IProtokoll
{
    public ProtokollEintrag Anhaengen(DateTimeOffset zeitpunkt, string benutzer, string entitaet, string entitaetId,
        string aktion, string? feld, string? alt, string? neu, string begruendung)
    {
        var letzte = s.Verbindung.QuerySingleOrDefault<ProtokollZeile>(
            "SELECT * FROM protokoll ORDER BY nr DESC LIMIT 1", transaction: s.Transaktion);
        var e = Protokoll.Naechster(letzte is null ? null : Abbilden(letzte), zeitpunkt, benutzer, entitaet, entitaetId,
            aktion, feld, alt, neu, begruendung);
        s.Verbindung.Execute("""
            INSERT INTO protokoll (nr, zeitpunkt, benutzer, entitaet, entitaet_id, aktion, feld, alt, neu, begruendung, prev_hash, hash)
            VALUES (@Nr, @Zeitpunkt, @Benutzer, @Entitaet, @EntitaetId, @Aktion, @Feld, @Alt, @Neu, @Begruendung, @PrevHash, @Hash)
            """, new
        {
            e.Nr, Zeitpunkt = Werte.Zeit(e.Zeitpunkt), e.Benutzer, e.Entitaet, e.EntitaetId, e.Aktion, e.Feld, e.Alt, e.Neu,
            e.Begruendung, e.PrevHash, e.Hash,
        }, s.Transaktion);
        return e;
    }

    public IReadOnlyList<ProtokollEintrag> Alle() =>
        s.Verbindung.Query<ProtokollZeile>("SELECT * FROM protokoll ORDER BY nr", transaction: s.Transaktion).Select(Abbilden).ToList();

    public IReadOnlyList<ProtokollEintrag> Zu(string entitaet, string entitaetId) =>
        s.Verbindung.Query<ProtokollZeile>("SELECT * FROM protokoll WHERE entitaet=@entitaet AND entitaet_id=@entitaetId ORDER BY nr",
            new { entitaet, entitaetId }, s.Transaktion).Select(Abbilden).ToList();

    public bool PruefeKette() => Protokoll.PruefeKette(Alle());

    private static ProtokollEintrag Abbilden(ProtokollZeile z) => new(z.Nr, Werte.Zeit(z.Zeitpunkt), z.Benutzer, z.Entitaet,
        z.EntitaetId, z.Aktion, z.Feld, z.Alt, z.Neu, z.Begruendung, z.PrevHash, z.Hash);
}
