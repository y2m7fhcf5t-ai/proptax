using System.Text.Json;
using PropTax.Domain;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Persistence;

/// <summary>Liest/schreibt die Regelwerkstabellen. <see cref="Laden"/> liefert eine Momentaufnahme als <see cref="IRegelwerk"/>.</summary>
public sealed class RegelwerkRepository(Sitzung s) : Repository(s)
{
    public InMemoryRegelwerk Laden()
    {
        var rw = new InMemoryRegelwerk();
        rw.RegelListe.AddRange(Query<RegelZeile>("SELECT * FROM steuerregel ORDER BY id").Select(r => new Steuerregel
        {
            Code = r.Code,
            Prioritaet = (int)r.Prioritaet,
            Belegtypen = JsonSerializer.Deserialize<List<string>>(r.Belegtypen)!.Select(Enum.Parse<Belegtyp>).ToList(),
            Richtung = r.Richtung is null ? null : Enum.Parse<Richtung>(r.Richtung),
            PartnerKategorie = r.PartnerKategorie is null ? null : Enum.Parse<PartnerKategorie>(r.PartnerKategorie),
            Bedingung = r.Bedingung,
            GueltigAb = Werte.Datum(r.GueltigAb),
            GueltigBis = Werte.DatumOpt(r.GueltigBis),
            Leistungsart = Enum.Parse<Leistungsart>(r.Leistungsart),
            Steuerbarkeit = Enum.Parse<Steuerbarkeit>(r.Steuerbarkeit),
            Leistungsort = r.Leistungsort,
            Steuerschuldner = Enum.Parse<Steuerschuldner>(r.Steuerschuldner),
            SatzSchluessel = r.SatzSchluessel,
            KennzahlSchluessel = r.KennzahlSchluessel,
            Entstehung = Enum.Parse<Entstehung>(r.Entstehung),
            ZaehltZu19Umsatz = r.ZaehltZu19Umsatz != 0,
            Rechtsgrundlage = r.Rechtsgrundlage,
            Status = Enum.Parse<PruefStatus>(r.Status),
        }));
        rw.Steuersaetze.AddRange(Query<SatzZeile>("SELECT * FROM steuersatz").Select(z =>
            new Steuersatz(z.Schluessel, Werte.Dez(z.Satz), Werte.Datum(z.GueltigAb), Werte.DatumOpt(z.GueltigBis), Enum.Parse<PruefStatus>(z.Status))));
        rw.Kennzahlen.AddRange(Query<KzZeile>("SELECT * FROM kennzahl_mapping").Select(z =>
            new Kennzahlzuordnung((int)z.Jahr, z.Schluessel, z.KzBmg, z.KzSteuer, z.KzVorsteuer, z.Bezeichnung, Enum.Parse<PruefStatus>(z.Status))));
        rw.Kurse.AddRange(Kurse().Select(k => new Wechselkurs(k.Waehrung, (int)k.Jahr, (int)k.Monat, Werte.Dez(k.Kurs), Enum.Parse<Kursquelle>(k.Quelle))));
        rw.Grenzwerte19.AddRange(Query<GrenzZeile>("SELECT * FROM grenzwert_19").Select(z => new Grenzwert19((int)z.Jahr,
            Werte.MoneyAusCent(z.GrenzeVorjahrCent), Werte.MoneyAusCent(z.GrenzeLfdJahrCent), z.Rechtsgrundlage, Enum.Parse<PruefStatus>(z.Status))));
        rw.Besteuerungsarten.AddRange(Query<ArtZeile>("SELECT * FROM besteuerungsart_historie ORDER BY gueltig_ab, id")
            .Select(z => new BesteuerungsartAb(Werte.Datum(z.GueltigAb), Enum.Parse<Besteuerungsart>(z.Art))));
        rw.Einstellungen.AddRange(Query<EinstellungZeile>("SELECT * FROM einstellung ORDER BY ab, id")
            .Select(z => new Einstellung(z.Schluessel, z.Wert, Werte.Datum(z.Ab))));
        return rw;
    }

    /// <summary>Schreibt ein komplettes Regelwerk (Startwerte) in leere Tabellen.</summary>
    public void Einspielen(InMemoryRegelwerk rw)
    {
        foreach (var r in rw.RegelListe)
            Exec("""
                INSERT INTO steuerregel (code, prioritaet, belegtypen, richtung, partner_kategorie, bedingung, gueltig_ab, gueltig_bis,
                  leistungsart, steuerbarkeit, leistungsort, steuerschuldner, satz_schluessel, kennzahl_schluessel, entstehung,
                  zaehlt_zu_19_umsatz, rechtsgrundlage, status)
                VALUES (@Code, @Prioritaet, @Belegtypen, @Richtung, @PartnerKategorie, @Bedingung, @GueltigAb, @GueltigBis,
                  @Leistungsart, @Steuerbarkeit, @Leistungsort, @Steuerschuldner, @SatzSchluessel, @KennzahlSchluessel, @Entstehung,
                  @Zaehlt, @Rechtsgrundlage, @Status)
                """, new
            {
                r.Code, r.Prioritaet, Belegtypen = JsonSerializer.Serialize(r.Belegtypen.Select(b => b.ToString())),
                Richtung = r.Richtung?.ToString(), PartnerKategorie = r.PartnerKategorie?.ToString(), r.Bedingung,
                GueltigAb = Werte.Datum(r.GueltigAb == DateOnly.MinValue ? new DateOnly(1900, 1, 1) : r.GueltigAb),
                GueltigBis = Werte.Datum(r.GueltigBis), Leistungsart = r.Leistungsart.ToString(), Steuerbarkeit = r.Steuerbarkeit.ToString(),
                r.Leistungsort, Steuerschuldner = r.Steuerschuldner.ToString(), r.SatzSchluessel, r.KennzahlSchluessel,
                Entstehung = r.Entstehung.ToString(), Zaehlt = r.ZaehltZu19Umsatz ? 1 : 0, r.Rechtsgrundlage, Status = r.Status.ToString(),
            });
        foreach (var z in rw.Steuersaetze)
            Exec("INSERT INTO steuersatz (schluessel, satz, gueltig_ab, gueltig_bis, status) VALUES (@a, @b, @c, @d, @e)",
                new { a = z.Schluessel, b = Werte.Dez(z.Satz), c = Werte.Datum(z.GueltigAb), d = Werte.Datum(z.GueltigBis), e = z.Status.ToString() });
        foreach (var k in rw.Kennzahlen)
            Exec("""
                INSERT INTO kennzahl_mapping (jahr, schluessel, kz_bmg, kz_steuer, kz_vorsteuer, bezeichnung, status)
                VALUES (@Jahr, @Schluessel, @KzBmg, @KzSteuer, @KzVorsteuer, @Bezeichnung, @Status)
                """, new { k.Jahr, k.Schluessel, k.KzBmg, k.KzSteuer, k.KzVorsteuer, k.Bezeichnung, Status = k.Status.ToString() });
        foreach (var g in rw.Grenzwerte19)
            Exec("""
                INSERT INTO grenzwert_19 (jahr, grenze_vorjahr_cent, grenze_lfd_jahr_cent, rechtsgrundlage, status)
                VALUES (@Jahr, @V, @L, @Rechtsgrundlage, @Status)
                """, new { g.Jahr, V = Werte.Cent(g.GrenzeVorjahr), L = Werte.Cent(g.GrenzeLaufendesJahr), g.Rechtsgrundlage, Status = g.Status.ToString() });
        foreach (var b in rw.Besteuerungsarten) SetzeBesteuerungsart(b.Ab, b.Art, "Startwert");
        foreach (var e in rw.Einstellungen) SetzeEinstellung(e.Schluessel, e.Wert, e.Ab);
        foreach (var k in rw.Kurse) SpeichereKurs(k.Waehrung, k.Jahr, k.Monat, k.Kurs, k.Quelle, null);
    }

    public IReadOnlyList<KursZeile> Kurse() => Query<KursZeile>("SELECT * FROM wechselkurs ORDER BY waehrung, jahr, monat, quelle").ToList();

    public void SpeichereKurs(string waehrung, int jahr, int monat, decimal kurs, Kursquelle quelle, long? importLaufId) => Exec("""
        INSERT INTO wechselkurs (waehrung, jahr, monat, kurs, quelle, import_lauf_id) VALUES (@waehrung, @jahr, @monat, @kurs, @quelle, @importLaufId)
        ON CONFLICT(waehrung, jahr, monat, quelle) DO UPDATE SET kurs=@kurs, import_lauf_id=@importLaufId
        """, new { waehrung = waehrung.ToUpperInvariant(), jahr, monat, kurs = Werte.Dez(kurs), quelle = quelle.ToString(), importLaufId });

    public void SetzeEinstellung(string schluessel, string wert, DateOnly ab) =>
        Exec("INSERT INTO einstellung (schluessel, wert, ab) VALUES (@schluessel, @wert, @ab)",
            new { schluessel, wert, ab = Werte.Datum(ab == DateOnly.MinValue ? new DateOnly(1900, 1, 1) : ab) });

    public void SetzeBesteuerungsart(DateOnly ab, Besteuerungsart art, string begruendung) =>
        Exec("INSERT INTO besteuerungsart_historie (gueltig_ab, art, begruendung) VALUES (@ab, @art, @begruendung)",
            new { ab = Werte.Datum(ab), art = art.ToString(), begruendung });

#pragma warning disable CS8618
    private sealed class RegelZeile
    {
        public string Code { get; set; } public long Prioritaet { get; set; } public string Belegtypen { get; set; }
        public string? Richtung { get; set; } public string? PartnerKategorie { get; set; } public string? Bedingung { get; set; }
        public string GueltigAb { get; set; } public string? GueltigBis { get; set; } public string Leistungsart { get; set; }
        public string Steuerbarkeit { get; set; } public string Leistungsort { get; set; } public string Steuerschuldner { get; set; }
        public string? SatzSchluessel { get; set; } public string? KennzahlSchluessel { get; set; } public string Entstehung { get; set; }
        public long ZaehltZu19Umsatz { get; set; } public string Rechtsgrundlage { get; set; } public string Status { get; set; }
    }
    private sealed class SatzZeile { public string Schluessel { get; set; } public string Satz { get; set; } public string GueltigAb { get; set; } public string? GueltigBis { get; set; } public string Status { get; set; } }
    private sealed class KzZeile { public long Jahr { get; set; } public string Schluessel { get; set; } public string? KzBmg { get; set; } public string? KzSteuer { get; set; } public string? KzVorsteuer { get; set; } public string Bezeichnung { get; set; } public string Status { get; set; } }
    private sealed class GrenzZeile { public long Jahr { get; set; } public long GrenzeVorjahrCent { get; set; } public long GrenzeLfdJahrCent { get; set; } public string Rechtsgrundlage { get; set; } public string Status { get; set; } }
    private sealed class ArtZeile { public string GueltigAb { get; set; } public string Art { get; set; } }
    private sealed class EinstellungZeile { public string Schluessel { get; set; } public string Wert { get; set; } public string Ab { get; set; } }
#pragma warning restore CS8618
}
