namespace PropTax.Domain;

/// <summary>Ableitung der Partnerkategorie aus dem Land (ISO 3166-1 alpha-2). Übersteuerbar je Partner.</summary>
public static class Laender
{
    public const string Inland = "DE";

    /// <summary>EU-Mitgliedstaaten (Stand 2026; GR auch als EL). Nordirland (XI) ist nicht abgebildet.</summary>
    public static readonly IReadOnlySet<string> EuMitglieder = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "GR", "HR", "HU", "IE",
        "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
    };

    public static PartnerKategorie? KategorieAus(string? landIso)
    {
        if (string.IsNullOrWhiteSpace(landIso)) return null;
        var land = landIso.Trim();
        if (string.Equals(land, Inland, StringComparison.OrdinalIgnoreCase)) return PartnerKategorie.Inland;
        return EuMitglieder.Contains(land) ? PartnerKategorie.Eu : PartnerKategorie.Drittland;
    }
}
