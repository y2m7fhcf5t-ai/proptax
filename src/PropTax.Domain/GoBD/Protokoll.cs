namespace PropTax.Domain.GoBD;

/// <summary>Append-only-Änderungsprotokoll mit SHA-256-Hash-Kette.</summary>
public sealed class Protokoll
{
    public Protokoll(IEnumerable<ProtokollEintrag>? bestehend = null) => throw new NotImplementedException();

    public IReadOnlyList<ProtokollEintrag> Eintraege => throw new NotImplementedException();

    public ProtokollEintrag Anhaengen(DateTimeOffset zeitpunkt, string benutzer, string entitaet, string entitaetId,
        string aktion, string? feld, string? alt, string? neu, string begruendung) => throw new NotImplementedException();

    public bool PruefeKette() => throw new NotImplementedException();

    public static bool PruefeKette(IReadOnlyList<ProtokollEintrag> eintraege) => throw new NotImplementedException();
}
