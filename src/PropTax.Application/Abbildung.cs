using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.Persistence;
using PropTax.TaxEngine.Ermittlung;

namespace PropTax.Application;

/// <summary>Abbildung zwischen Zeilen (Persistenz) und Domänen-/Engine-Objekten.</summary>
internal static class Abbildung
{
    public static readonly string EngineVersion =
        typeof(Steuerermittlung).Assembly.GetName().Version?.ToString() ?? "0";

    public static BelegZeile ZuZeile(BelegDaten d, BelegStatus status, string angelegtAm, BelegZeile? basis = null)
    {
        var z = basis ?? new BelegZeile();
        z.Belegnummer = d.Belegnummer.Trim();
        z.BetriebId = d.BetriebId;
        z.PartnerId = d.PartnerId;
        z.Belegtyp = d.Typ.ToString();
        z.Richtung = d.Richtung.ToString();
        z.Rechnungsdatum = Werte.Datum(d.Rechnungsdatum);
        z.Leistungsdatum = Werte.Datum(d.Leistungsdatum);
        z.Zahlungsdatum = Werte.Datum(d.Zahlungsdatum);
        z.BetragFwCent = Werte.Cent(d.Betrag.Betrag);
        z.Waehrung = d.Betrag.Waehrung.ToUpperInvariant();
        z.AusgewieseneUstCent = Werte.Cent(d.AusgewieseneUst);
        z.Bankkurs = Werte.Dez(d.Bankkurs);
        z.EuerKategorieId = d.EuerKategorieId;
        z.EuerPeriode = d.Zahlungsdatum?.Year.ToString(CultureInfo.InvariantCulture);
        z.Status = status.ToString();
        z.BezugBelegId = d.BezugBelegId;
        z.DublettenSchluessel = DublettenSchluessel(d);
        z.Notiz = d.Notiz;
        if (basis is null) z.AngelegtAm = angelegtAm;
        return z;
    }

    public static BelegDaten ZuDaten(BelegZeile z) => new()
    {
        Belegnummer = z.Belegnummer,
        BetriebId = z.BetriebId,
        PartnerId = z.PartnerId,
        Typ = Enum.Parse<Belegtyp>(z.Belegtyp),
        Richtung = Enum.Parse<Richtung>(z.Richtung),
        Rechnungsdatum = Werte.DatumOpt(z.Rechnungsdatum),
        Leistungsdatum = Werte.Datum(z.Leistungsdatum),
        Zahlungsdatum = Werte.DatumOpt(z.Zahlungsdatum),
        Betrag = new CurrencyAmount(Werte.AusCent(z.BetragFwCent), z.Waehrung),
        AusgewieseneUst = Werte.MoneyAusCent(z.AusgewieseneUstCent),
        Bankkurs = Werte.DezOpt(z.Bankkurs),
        EuerKategorieId = z.EuerKategorieId,
        BezugBelegId = z.BezugBelegId,
        Notiz = z.Notiz,
    };

    /// <summary>
    /// Mögliche Dublette: gleicher Partner, Typ, Leistungsdatum, Betrag, Währung. Nur Warnung, denn mehrere
    /// identische Challenge-Käufe am selben Tag sind legitim.
    /// </summary>
    public static string DublettenSchluessel(BelegDaten d)
    {
        var roh = string.Join('|', d.PartnerId, d.Typ, Werte.Datum(d.Leistungsdatum), Werte.Cent(d.Betrag.Betrag), d.Betrag.Waehrung.ToUpperInvariant());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(roh)))[..32];
    }

    public static BelegEingabe ZuEingabe(BelegZeile z, PartnerKategorie kategorie, string betrieb, Erstattungsbezug? bezug) => new()
    {
        BelegRef = z.Belegnummer,
        Betrieb = betrieb,
        Typ = Enum.Parse<Belegtyp>(z.Belegtyp),
        Richtung = Enum.Parse<Richtung>(z.Richtung),
        PartnerKategorie = kategorie,
        Rechnungsdatum = Werte.DatumOpt(z.Rechnungsdatum),
        Leistungsdatum = Werte.Datum(z.Leistungsdatum),
        Zahlungsdatum = Werte.DatumOpt(z.Zahlungsdatum),
        Betrag = new CurrencyAmount(Werte.AusCent(z.BetragFwCent), z.Waehrung),
        AusgewieseneUst = Werte.MoneyAusCent(z.AusgewieseneUstCent),
        Bezug = bezug,
    };

    private sealed record KzDto(string Kz, string Art, string Betrag);

    public static BelegSteuerZeile ZuZeile(long belegId, SteuerErgebnis e, string berechnetAm) => new()
    {
        BelegId = belegId,
        RegelCode = e.RegelCode,
        Leistungsart = e.Leistungsart.ToString(),
        Steuerbarkeit = e.Steuerbarkeit.ToString(),
        Leistungsort = e.Leistungsort,
        Steuerschuldner = e.Steuerschuldner.ToString(),
        Besteuerungsart = e.Besteuerungsart.ToString(),
        KennzahlSchluessel = e.KennzahlSchluessel,
        Satz = Werte.Dez(e.Satz),
        Kurs = Werte.Dez(e.Kurs),
        Entstehungsdatum = Werte.Datum(e.Entstehungsdatum),
        UstPeriode = e.Zeitraum.ToString(),
        BmgCent = Werte.Cent(e.Bemessungsgrundlage),
        SteuerCent = Werte.Cent(e.Steuer),
        VorsteuerCent = Werte.Cent(e.Vorsteuer),
        Umsatz19Cent = Werte.Cent(e.Umsatz19),
        KennzahlenJson = JsonSerializer.Serialize(e.Kennzahlen.Select(k => new KzDto(k.Kennzahl, k.Art.ToString(), Werte.Dez(k.Betrag.Betrag)))),
        BegruendungJson = JsonSerializer.Serialize(e.Begruendung),
        IstManuell = e.IstManuell ? 1 : 0,
        ManuelleBegruendung = e.ManuelleBegruendung,
        BerechnetAm = berechnetAm,
        EngineVersion = EngineVersion,
    };

    public static SteuerErgebnis ZuErgebnis(BelegSteuerZeile z, BelegZeile b, string betrieb) => new()
    {
        BelegRef = b.Belegnummer,
        Betrieb = betrieb,
        RegelCode = z.RegelCode,
        Leistungsart = Enum.Parse<Leistungsart>(z.Leistungsart),
        Steuerbarkeit = Enum.Parse<Steuerbarkeit>(z.Steuerbarkeit),
        Leistungsort = z.Leistungsort,
        Steuerschuldner = Enum.Parse<Steuerschuldner>(z.Steuerschuldner),
        Besteuerungsart = Enum.Parse<Besteuerungsart>(z.Besteuerungsart),
        KennzahlSchluessel = z.KennzahlSchluessel,
        Satz = Werte.DezOpt(z.Satz),
        Kurs = Werte.DezOpt(z.Kurs),
        Entstehungsdatum = Werte.Datum(z.Entstehungsdatum),
        Zeitraum = ParseZeitraum(z.UstPeriode),
        Bemessungsgrundlage = Werte.MoneyAusCent(z.BmgCent),
        Steuer = Werte.MoneyAusCent(z.SteuerCent),
        Vorsteuer = Werte.MoneyAusCent(z.VorsteuerCent),
        Umsatz19 = Werte.MoneyAusCent(z.Umsatz19Cent),
        Kennzahlen = JsonSerializer.Deserialize<List<KzDto>>(z.KennzahlenJson)!
            .Select(k => new KennzahlPosition(k.Kz, Enum.Parse<KennzahlArt>(k.Art), new Money(Werte.Dez(k.Betrag)))).ToList(),
        Begruendung = JsonSerializer.Deserialize<List<string>>(z.BegruendungJson)!,
        IstManuell = z.IstManuell != 0,
        ManuelleBegruendung = z.ManuelleBegruendung,
    };

    public static UstZeitraum ParseZeitraum(string s)
    {
        var jahr = int.Parse(s[..4], CultureInfo.InvariantCulture);
        if (s.Length == 4) return new UstZeitraum(jahr, ZeitraumArt.Jahr, 1);
        return s[5] == 'Q'
            ? new UstZeitraum(jahr, ZeitraumArt.Quartal, s[6] - '0')
            : new UstZeitraum(jahr, ZeitraumArt.Monat, int.Parse(s[5..], CultureInfo.InvariantCulture));
    }

    /// <summary>Negiertes Steuerergebnis für Stornobelege (Korrektur im Zeitraum des Stornodatums).</summary>
    public static SteuerErgebnis Negiert(SteuerErgebnis e, string belegRef, DateOnly datum, UstZeitraum zeitraum, string grund) => e with
    {
        BelegRef = belegRef,
        Entstehungsdatum = datum,
        Zeitraum = zeitraum,
        Bemessungsgrundlage = -e.Bemessungsgrundlage,
        Steuer = -e.Steuer,
        Vorsteuer = -e.Vorsteuer,
        Umsatz19 = -e.Umsatz19,
        Kennzahlen = e.Kennzahlen.Select(k => k with { Betrag = -k.Betrag }).ToList(),
        Begruendung = [$"Storno von {e.BelegRef}: {grund}", .. e.Begruendung],
    };

    public static string Beschreibe(BelegDaten d) => string.Join(" | ",
        d.Belegnummer, d.Typ, Werte.Datum(d.Leistungsdatum),
        $"{d.Betrag.Betrag.ToString(CultureInfo.InvariantCulture)} {d.Betrag.Waehrung}", $"Partner {d.PartnerId}", $"Betrieb {d.BetriebId}");

    /// <summary>Feldweise Unterschiede (für das Protokoll).</summary>
    public static IEnumerable<(string Feld, string? Alt, string? Neu)> Unterschiede(BelegDaten a, BelegDaten n)
    {
        static string? F(object? o) => o switch
        {
            null => null,
            DateOnly d => Werte.Datum(d),
            CurrencyAmount c => $"{c.Betrag.ToString(CultureInfo.InvariantCulture)} {c.Waehrung}",
            Money m => m.Betrag.ToString(CultureInfo.InvariantCulture),
            decimal x => x.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(o, CultureInfo.InvariantCulture),
        };
        foreach (var p in typeof(BelegDaten).GetProperties())
        {
            var alt = F(p.GetValue(a));
            var neu = F(p.GetValue(n));
            if (alt != neu) yield return (p.Name, alt, neu);
        }
    }
}
