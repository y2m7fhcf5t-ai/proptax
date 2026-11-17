using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PropTax.Domain.GoBD;

/// <summary>
/// Append-only-Änderungsprotokoll. Jeder Eintrag enthält den Hash des Vorgängers;
/// nachträgliche Änderungen, Löschungen oder Lücken werden durch <see cref="PruefeKette()"/> erkannt.
/// </summary>
public sealed class Protokoll : IProtokoll
{
    public const string StartHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly List<ProtokollEintrag> _eintraege;

    public Protokoll(IEnumerable<ProtokollEintrag>? bestehend = null)
    {
        _eintraege = bestehend?.ToList() ?? [];
        if (!PruefeKette(_eintraege))
            throw new InvalidOperationException("Das geladene Protokoll ist nicht integer (Hash-Kette verletzt).");
    }

    public IReadOnlyList<ProtokollEintrag> Eintraege => _eintraege;

    public ProtokollEintrag Anhaengen(DateTimeOffset zeitpunkt, string benutzer, string entitaet, string entitaetId,
        string aktion, string? feld, string? alt, string? neu, string begruendung)
    {
        var e = Naechster(_eintraege.Count == 0 ? null : _eintraege[^1], zeitpunkt, benutzer, entitaet, entitaetId,
            aktion, feld, alt, neu, begruendung);
        _eintraege.Add(e);
        return e;
    }

    public bool PruefeKette() => PruefeKette(_eintraege);

    public static bool PruefeKette(IReadOnlyList<ProtokollEintrag> eintraege)
    {
        var prev = StartHash;
        for (var i = 0; i < eintraege.Count; i++)
        {
            var e = eintraege[i];
            if (e.Nr != i + 1 || e.PrevHash != prev || e.Hash != BerechneHash(e)) return false;
            prev = e.Hash;
        }
        return true;
    }

    /// <summary>Erzeugt den Folgeeintrag (Nr, PrevHash, Hash) zum letzten Eintrag einer Kette.</summary>
    public static ProtokollEintrag Naechster(ProtokollEintrag? letzter, DateTimeOffset zeitpunkt, string benutzer,
        string entitaet, string entitaetId, string aktion, string? feld, string? alt, string? neu, string begruendung)
    {
        var e = new ProtokollEintrag((letzter?.Nr ?? 0) + 1, zeitpunkt, benutzer, entitaet, entitaetId, aktion,
            feld, alt, neu, begruendung, letzter?.Hash ?? StartHash, "");
        return e with { Hash = BerechneHash(e) };
    }

    public static string BerechneHash(ProtokollEintrag e)
    {
        var felder = new[]
        {
            e.Nr.ToString(CultureInfo.InvariantCulture), e.Zeitpunkt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            e.Benutzer, e.Entitaet, e.EntitaetId, e.Aktion, e.Feld ?? "\0", e.Alt ?? "\0", e.Neu ?? "\0", e.Begruendung, e.PrevHash,
        };
        var bytes = Encoding.UTF8.GetBytes(string.Join('\u001F', felder));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
