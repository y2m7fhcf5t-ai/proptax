using System.Globalization;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.Persistence;

namespace PropTax.Application;

public sealed class StammdatenService(Arbeitsbereich ab)
{
    public IReadOnlyList<BetriebDaten> Betriebe()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.Betriebe().Select(b => new BetriebDaten(b.Id, b.Name, b.Kurzzeichen, b.Aktiv != 0)).ToList();
    }

    public long SpeichereBetrieb(BetriebDaten d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new FachlicherFehler("Name des Betriebs fehlt.");
        using var s = ab.Sitzung();
        var alt = d.Id == 0 ? null : s.Stammdaten.Betrieb(d.Id) ?? throw new FachlicherFehler($"Betrieb {d.Id} nicht gefunden.");
        var id = s.Stammdaten.SpeichereBetrieb(new BetriebZeile
        {
            Id = d.Id, UnternehmenId = s.Stammdaten.UnternehmenId(), Name = d.Name.Trim(), Kurzzeichen = d.Kurzzeichen, Aktiv = d.Aktiv ? 1 : 0,
        });
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "betrieb", id.ToString(CultureInfo.InvariantCulture),
            alt is null ? "ANLAGE" : "AENDERUNG", null, alt?.Name, d.Name, "Stammdatenpflege");
        s.Commit();
        return id;
    }

    public IReadOnlyList<PartnerDaten> Partner()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.Partner().Select(ZuDaten).ToList();
    }

    public PartnerDaten? Partner(long id)
    {
        using var s = ab.Sitzung();
        var p = s.Stammdaten.Partner(id);
        return p is null ? null : ZuDaten(p);
    }

    /// <summary>
    /// Legt einen Partner an oder ändert ihn. Ist keine Kategorie angegeben, wird sie aus dem Land abgeleitet
    /// (DE → Inland, EU-Mitglied → EU, sonst Drittland). Jede Feldänderung wird protokolliert.
    /// </summary>
    public long SpeicherePartner(PartnerDaten d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new FachlicherFehler("Name des Partners fehlt.");
        var land = string.IsNullOrWhiteSpace(d.LandIso) ? null : d.LandIso.Trim().ToUpperInvariant();
        if (land is not null && land.Length != 2) throw new FachlicherFehler("Land bitte als ISO-Code mit 2 Buchstaben (z. B. US, AE, GB, IE).");
        var neu = d with { Name = d.Name.Trim(), LandIso = land, Kategorie = d.Kategorie ?? Laender.KategorieAus(land) };

        using var s = ab.Sitzung();
        var andere = s.Stammdaten.PartnerNachName(neu.Name);
        if (andere is not null && andere.Id != d.Id) throw new FachlicherFehler($"Partner „{neu.Name}“ existiert bereits.");
        var alt = d.Id == 0 ? null : ZuDaten(s.Stammdaten.Partner(d.Id) ?? throw new FachlicherFehler($"Partner {d.Id} nicht gefunden."));
        var id = s.Stammdaten.SpeicherePartner(ZuZeile(neu));
        var idText = id.ToString(CultureInfo.InvariantCulture);
        if (alt is null)
            s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "partner", idText, "ANLAGE", null, null, $"{neu.Name} ({neu.LandIso}, {neu.Kategorie})", "Stammdatenpflege");
        else
            foreach (var p in typeof(PartnerDaten).GetProperties().Where(p => p.Name != nameof(PartnerDaten.Id)))
            {
                var a = Convert.ToString(p.GetValue(alt), CultureInfo.InvariantCulture);
                var n = Convert.ToString(p.GetValue(neu), CultureInfo.InvariantCulture);
                if (a != n) s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "partner", idText, "AENDERUNG", p.Name, a, n, "Stammdatenpflege");
            }
        s.Commit();
        return id;
    }

    public IReadOnlyList<EuerKategorieDaten> EuerKategorien()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.EuerKategorien().Select(k => new EuerKategorieDaten(k.Id, k.Code, k.Bezeichnung, k.Art)).ToList();
    }

    public string Unternehmen()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.UnternehmenName();
    }

    public void SetzeUnternehmen(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new FachlicherFehler("Name fehlt.");
        using var s = ab.Sitzung();
        var alt = s.Stammdaten.UnternehmenName();
        s.Stammdaten.SetzeUnternehmenName(name.Trim());
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "unternehmen", "1", "AENDERUNG", "Name", alt, name.Trim(), "Stammdatenpflege");
        s.Commit();
    }

    /// <summary>Zuordnung EÜR-Kategorie → Zeile der Anlage EÜR je Jahr (vom Nutzer gepflegt, Status ungeprüft).</summary>
    public IReadOnlyList<EuerZeilenZuordnung> EuerZeilen(int jahr)
    {
        using var s = ab.Sitzung();
        var zeilen = s.Stammdaten.EuerZeilen(jahr).ToDictionary(z => z.EuerKategorieId);
        return s.Stammdaten.EuerKategorien().Select(k => new EuerZeilenZuordnung(jahr, k.Id, k.Bezeichnung, k.Art,
            zeilen.TryGetValue(k.Id, out var z) ? z.Zeile : null)).ToList();
    }

    public void SetzeEuerZeile(int jahr, long kategorieId, string? zeile, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        using var s = ab.Sitzung();
        var alt = s.Stammdaten.EuerZeilen(jahr).FirstOrDefault(z => z.EuerKategorieId == kategorieId)?.Zeile;
        s.Stammdaten.SetzeEuerZeile(jahr, kategorieId, zeile, null);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "euer_zeile", $"{jahr}/{kategorieId}", "AENDERUNG", "Zeile", alt, zeile, begruendung);
        s.Commit();
    }

    public IReadOnlyList<VorlageDaten> Vorlagen()
    {
        using var s = ab.Sitzung();
        return s.Stammdaten.Vorlagen().Select(v => new VorlageDaten
        {
            Id = v.Id, Name = v.Name, BetriebId = v.BetriebId, PartnerId = v.PartnerId, Typ = Enum.Parse<Belegtyp>(v.Belegtyp),
            Richtung = Enum.Parse<Richtung>(v.Richtung), EuerKategorieId = v.EuerKategorieId,
            Betrag = v.BetragFwCent is null || v.Waehrung is null ? null : new CurrencyAmount(Werte.AusCent(v.BetragFwCent.Value), v.Waehrung),
        }).ToList();
    }

    public long SpeichereVorlage(VorlageDaten v)
    {
        if (string.IsNullOrWhiteSpace(v.Name)) throw new FachlicherFehler("Name der Vorlage fehlt.");
        using var s = ab.Sitzung();
        var id = s.Stammdaten.SpeichereVorlage(new VorlageZeile
        {
            Id = v.Id, Name = v.Name.Trim(), BetriebId = v.BetriebId, PartnerId = v.PartnerId, Belegtyp = v.Typ.ToString(),
            Richtung = v.Richtung.ToString(), BetragFwCent = v.Betrag is null ? null : Werte.Cent(v.Betrag.Value.Betrag),
            Waehrung = v.Betrag?.Waehrung, EuerKategorieId = v.EuerKategorieId,
        });
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "vorlage", id.ToString(CultureInfo.InvariantCulture), v.Id == 0 ? "ANLAGE" : "AENDERUNG",
            null, null, v.Name, "Vorlagenpflege");
        s.Commit();
        return id;
    }

    /// <summary>Belegdaten aus einer Vorlage (Datum und Belegnummer werden gesetzt).</summary>
    public BelegDaten AusVorlage(long vorlageId, DateOnly datum, string belegnummer)
    {
        var v = Vorlagen().SingleOrDefault(x => x.Id == vorlageId) ?? throw new FachlicherFehler($"Vorlage {vorlageId} nicht gefunden.");
        return new BelegDaten
        {
            Belegnummer = belegnummer,
            BetriebId = v.BetriebId ?? Betriebe().First().Id,
            PartnerId = v.PartnerId ?? throw new FachlicherFehler("Vorlage ohne Partner."),
            Typ = v.Typ,
            Richtung = v.Richtung,
            Rechnungsdatum = datum,
            Leistungsdatum = datum,
            Zahlungsdatum = datum,
            Betrag = v.Betrag ?? new CurrencyAmount(0m, "EUR"),
            EuerKategorieId = v.EuerKategorieId,
        };
    }

    internal static PartnerDaten ZuDaten(PartnerZeile p) => new()
    {
        Id = p.Id, Name = p.Name, LandIso = p.LandIso,
        Kategorie = p.Kategorie is null ? null : Enum.Parse<PartnerKategorie>(p.Kategorie),
        UstIdnr = p.UstIdnr, TaxId = p.TaxId, StdBelegtyp = p.StdBelegtyp is null ? null : Enum.Parse<Belegtyp>(p.StdBelegtyp),
        IstPropFirma = p.IstPropFirma != 0, Aktiv = p.Aktiv != 0, Notiz = p.Notiz,
    };

    private static PartnerZeile ZuZeile(PartnerDaten d) => new()
    {
        Id = d.Id, Name = d.Name, LandIso = d.LandIso, Kategorie = d.Kategorie?.ToString(), UstIdnr = d.UstIdnr, TaxId = d.TaxId,
        StdBelegtyp = d.StdBelegtyp?.ToString(), IstPropFirma = d.IstPropFirma ? 1 : 0, Aktiv = d.Aktiv ? 1 : 0, Notiz = d.Notiz,
    };
}
