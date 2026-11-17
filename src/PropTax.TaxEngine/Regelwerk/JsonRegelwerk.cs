using System.Text.Json;
using System.Text.Json.Serialization;
using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.TaxEngine.Regelwerk;

/// <summary>Lädt Regelwerk-Startwerte aus JSON (eingebettete Ressource oder Text).</summary>
public static class JsonRegelwerk
{
    private const string Ressource = "PropTax.Startwerte.regelwerk.json";

    private static readonly JsonSerializerOptions Optionen = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    public static InMemoryRegelwerk LadeStartwerte()
    {
        using var stream = typeof(JsonRegelwerk).Assembly.GetManifestResourceStream(Ressource)
            ?? throw new RegelwerkException($"Ressource {Ressource} fehlt.");
        using var reader = new StreamReader(stream);
        return Lade(reader.ReadToEnd());
    }

    public static InMemoryRegelwerk Lade(string json)
    {
        var d = JsonSerializer.Deserialize<Dokument>(json, Optionen) ?? new Dokument();
        var rw = new InMemoryRegelwerk();
        rw.RegelListe.AddRange(d.Regeln);
        rw.Steuersaetze.AddRange(d.Steuersaetze);
        rw.Kennzahlen.AddRange(d.Kennzahlen);
        rw.Kurse.AddRange(d.Kurse);
        rw.Grenzwerte19.AddRange(d.Grenzwerte19.Select(g => new Grenzwert19(g.Jahr, Money.Euro(g.GrenzeVorjahr),
            Money.Euro(g.GrenzeLaufendesJahr), g.Rechtsgrundlage, g.Status)));
        rw.Besteuerungsarten.AddRange(d.Besteuerungsarten);
        rw.Einstellungen.AddRange(d.Einstellungen);
        return rw;
    }

    private sealed record GrenzwertDto(int Jahr, decimal GrenzeVorjahr, decimal GrenzeLaufendesJahr, string Rechtsgrundlage, PruefStatus Status);

    private sealed record Dokument
    {
        public List<Steuerregel> Regeln { get; init; } = [];
        public List<Steuersatz> Steuersaetze { get; init; } = [];
        public List<Kennzahlzuordnung> Kennzahlen { get; init; } = [];
        public List<Wechselkurs> Kurse { get; init; } = [];
        public List<GrenzwertDto> Grenzwerte19 { get; init; } = [];
        public List<BesteuerungsartAb> Besteuerungsarten { get; init; } = [];
        public List<Einstellung> Einstellungen { get; init; } = [];
    }
}
