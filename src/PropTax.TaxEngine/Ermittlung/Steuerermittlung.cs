using System.Globalization;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.TaxEngine.Ermittlung;

/// <summary>
/// Ermittelt für einen Beleg Leistungsart, Leistungsort, Steuerschuldner, Bemessungsgrundlage, Steuer,
/// Vorsteuer, Kennzahlen und §19-Umsatzbeitrag. Alle fachlichen Parameter stammen aus <see cref="IRegelwerk"/>.
/// </summary>
public sealed class Steuerermittlung(IRegelwerk regelwerk)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public SteuerErgebnis Ermittle(BelegEingabe beleg, Uebersteuerung? uebersteuerung = null)
    {
        if (uebersteuerung is not null && string.IsNullOrWhiteSpace(uebersteuerung.Begruendung))
            throw new ArgumentException("Eine manuelle Übersteuerung erfordert eine Begründung.", nameof(uebersteuerung));

        return beleg.Typ == Belegtyp.Erstattung
            ? ErmittleErstattung(beleg, uebersteuerung)
            : ErmittleRegulaer(beleg, uebersteuerung);
    }

    private SteuerErgebnis ErmittleRegulaer(BelegEingabe b, Uebersteuerung? u)
    {
        var log = new List<string>();
        var regel = Uebersteuere(WaehleRegel(b, log), u, log);
        var entstehung = u?.Entstehungsdatum ?? Entstehungsdatum(b, regel, log);
        var art = regelwerk.Besteuerungsart(entstehung);
        log.Add($"Besteuerungsart am {entstehung.ToString("d", De)}: {art}.");

        decimal? kurs = null;
        if (b.Betrag.Waehrung != "EUR")
        {
            kurs = u?.Kurs ?? regelwerk.Kurs(b.Betrag.Waehrung, entstehung.Year, entstehung.Month, Kursquelle.Bmf);
            log.Add($"Umrechnung §16 Abs. 6 UStG: 1 EUR = {kurs.Value.ToString(De)} {b.Betrag.Waehrung} ({entstehung.Month:00}/{entstehung.Year}).");
        }
        var eur = (kurs is null ? new Money(b.Betrag.Betrag) : b.Betrag.InEuro(kurs.Value)).Gerundet();

        decimal? satz = u?.Satz ?? (regel.SatzSchluessel is null ? null : regelwerk.Satz(regel.SatzSchluessel, entstehung));
        var betraege = Berechne(b, regel, art, eur, satz, entstehung, log);
        return Baue(b, regel, art, kurs, satz, entstehung, betraege, log, u);
    }

    /// <summary>§17 UStG: Korrektur der Bemessungsgrundlage im Zeitraum der Erstattung (O-06).</summary>
    private SteuerErgebnis ErmittleErstattung(BelegEingabe b, Uebersteuerung? u)
    {
        var bezug = b.Bezug ?? throw new RegelwerkException($"Erstattung {b.BelegRef} ohne Bezug auf einen Ursprungsbeleg.");
        var original = bezug.Original;
        var oe = bezug.OriginalErgebnis;
        if (b.Betrag.Waehrung != original.Betrag.Waehrung)
            throw new RegelwerkException($"Erstattung {b.BelegRef} in {b.Betrag.Waehrung}, Ursprungsbeleg in {original.Betrag.Waehrung}.");

        var log = new List<string> { $"Erstattung zu {original.BelegRef} (§17 UStG), Ursprungsregel {oe.RegelCode}." };
        var basis = regelwerk.Regeln().FirstOrDefault(r => r.Code == oe.RegelCode)
            ?? throw new RegelwerkException($"Ursprungsregel '{oe.RegelCode}' ist nicht mehr im Regelwerk vorhanden.");
        var regel = Uebersteuere(basis with
        {
            Steuerbarkeit = oe.Steuerbarkeit,
            Steuerschuldner = oe.Steuerschuldner,
            KennzahlSchluessel = oe.KennzahlSchluessel,
        }, u, log);

        // TODO-STEUER O-06: Korrekturzeitpunkt = Zahlungseingang der Erstattung.
        var datum = u?.Entstehungsdatum ?? b.Zahlungsdatum ?? b.Rechnungsdatum ?? b.Leistungsdatum;
        decimal? kurs = null;
        if (oe.Kurs is not null)
        {
            // TODO-STEUER O-06: Kurs der Erstattung (Originalkurs vs. Monatskurs Erstattung) konfigurierbar.
            var modus = regelwerk.Einstellung("erstattung.kurs", datum);
            kurs = u?.Kurs ?? (modus == "ORIGINAL"
                ? oe.Kurs
                : regelwerk.Kurs(b.Betrag.Waehrung, datum.Year, datum.Month, Kursquelle.Bmf));
            log.Add($"Erstattungskurs ({modus}): 1 EUR = {kurs!.Value.ToString(De)} {b.Betrag.Waehrung}.");
        }
        var eur = (kurs is null ? new Money(b.Betrag.Betrag) : b.Betrag.InEuro(kurs.Value)).Gerundet();
        var satz = u?.Satz ?? oe.Satz;

        // Berechnung mit den Merkmalen des Ursprungsbelegs (Richtung, Typ, Besteuerungsart), dann negiert.
        var basisBeleg = original with { Betrag = b.Betrag, AusgewieseneUst = b.AusgewieseneUst };
        var p = Berechne(basisBeleg, regel, oe.Besteuerungsart, eur, satz, datum, log);
        var negiert = new Betraege(-p.Bmg, -p.Steuer, -p.Vorsteuer, -p.Umsatz19, p.KeineKennzahlen);
        var ergebnis = Baue(b, regel, oe.Besteuerungsart, kurs, satz, datum, negiert, log, u);
        return ergebnis with { Betrieb = string.IsNullOrEmpty(b.Betrieb) ? original.Betrieb : b.Betrieb };
    }

    private Steuerregel WaehleRegel(BelegEingabe b, List<string> log)
    {
        var kandidaten = regelwerk.Regeln().Where(r => Passt(r, b)).ToList();
        if (kandidaten.Count == 0)
            throw new RegelwerkException(
                $"Keine Steuerregel für {b.Typ}/{b.Richtung}/{b.PartnerKategorie} am {b.Leistungsdatum.ToString("d", De)} (Beleg {b.BelegRef}).");

        var max = kandidaten.Max(r => r.Prioritaet);
        var top = kandidaten.Where(r => r.Prioritaet == max).ToList();
        if (top.Count > 1)
            throw new RegelwerkException($"Mehrdeutige Steuerregeln für Beleg {b.BelegRef}: {string.Join(", ", top.Select(r => r.Code))}.");

        var regel = top[0];
        log.Add($"Regel {regel.Code} ({regel.Rechtsgrundlage}; Status {regel.Status}).");
        log.Add($"Leistungsart {regel.Leistungsart}, Leistungsort {regel.Leistungsort}, Steuerschuldner {regel.Steuerschuldner}.");
        return regel;
    }

    private bool Passt(Steuerregel r, BelegEingabe b) =>
        (r.Belegtypen.Count == 0 || r.Belegtypen.Contains(b.Typ))
        && (r.Richtung is null || r.Richtung == b.Richtung)
        && (r.PartnerKategorie is null || r.PartnerKategorie == b.PartnerKategorie)
        && r.GueltigAb <= b.Leistungsdatum
        && (r.GueltigBis is null || b.Leistungsdatum <= r.GueltigBis)
        && BedingungErfuellt(r.Bedingung, b.Leistungsdatum);

    private bool BedingungErfuellt(string? bedingung, DateOnly datum)
    {
        if (string.IsNullOrWhiteSpace(bedingung)) return true;
        var teile = bedingung.Split('=', 2, StringSplitOptions.TrimEntries);
        return regelwerk.Einstellung(teile[0], datum) == teile[1];
    }

    private static Steuerregel Uebersteuere(Steuerregel regel, Uebersteuerung? u, List<string> log)
    {
        if (u is null) return regel;
        log.Add($"Manuell übersteuert: {u.Begruendung}");
        return regel with
        {
            Steuerbarkeit = u.Steuerbarkeit ?? regel.Steuerbarkeit,
            Steuerschuldner = u.Steuerschuldner ?? regel.Steuerschuldner,
            KennzahlSchluessel = u.KennzahlSchluessel ?? regel.KennzahlSchluessel,
        };
    }

    /// <summary>Entstehungszeitpunkt (O-07); bestimmt USt-Zeitraum und Monatskurs.</summary>
    private static DateOnly Entstehungsdatum(BelegEingabe b, Steuerregel regel, List<string> log)
    {
        var datum = regel.Entstehung switch
        {
            Entstehung.LeistungVaZeitraum => b.Leistungsdatum,
            Entstehung.RechnungSpaetestensFolgemonat => Min(b.Rechnungsdatum ?? DateOnly.MaxValue, EndeFolgemonat(b.Leistungsdatum)),
            Entstehung.LeistungUndRechnung => Max(b.Leistungsdatum, b.Rechnungsdatum ?? b.Leistungsdatum),
            _ => b.Zahlungsdatum ?? throw new RegelwerkException($"Beleg {b.BelegRef}: Zahlungsdatum erforderlich (Vereinnahmung)."),
        };

        // TODO-STEUER O-07: §13b Abs. 4 S. 2 UStG – Vorauszahlung vor Leistungsausführung.
        if (regel.Steuerschuldner != Steuerschuldner.Leistender
            && b.Zahlungsdatum is { } zahlung && zahlung < b.Leistungsdatum && zahlung < datum)
        {
            datum = zahlung;
            log.Add("Vorauszahlung vor Leistungsausführung: Entstehung mit Zahlung (§13b Abs. 4 S. 2 UStG).");
        }
        log.Add($"Entstehung ({regel.Entstehung}): {datum.ToString("d", De)}.");
        return datum;
    }

    private static DateOnly EndeFolgemonat(DateOnly d) => new DateOnly(d.Year, d.Month, 1).AddMonths(2).AddDays(-1);
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private sealed record Betraege(Money Bmg, Money Steuer, Money Vorsteuer, Money Umsatz19, bool KeineKennzahlen);

    private Betraege Berechne(BelegEingabe b, Steuerregel regel, Besteuerungsart art, Money eur, decimal? satz,
        DateOnly datum, List<string> log)
    {
        var umsatz19 = regel.ZaehltZu19Umsatz;
        switch (regel.Steuerbarkeit)
        {
            case Steuerbarkeit.KeinLeistungsaustausch:
                log.Add("Kein Leistungsaustausch: keine umsatzsteuerliche Wirkung.");
                return new Betraege(Money.Null, Money.Null, Money.Null, Money.Null, true);

            case Steuerbarkeit.NichtSteuerbar:
                log.Add("Nicht steuerbar: keine Steuer.");
                return new Betraege(eur, Money.Null, Money.Null, umsatz19 ? eur : Money.Null, false);
        }

        if (regel.Steuerschuldner != Steuerschuldner.Leistender)
        {
            var s = PflichtSatz(satz, regel);
            var steuer = new Money(eur.Betrag * s).Gerundet();
            // §13b Abs. 8 UStG: §19 nicht anzuwenden. Vorsteuerabzug nur bei Regelbesteuerung (O-01).
            var vst = art == Besteuerungsart.Regelbesteuerung ? steuer : Money.Null;
            log.Add($"§13b: Steuer {steuer} = {eur} × {s.ToString(De)}; Vorsteuer {vst}.");
            return new Betraege(eur, steuer, vst, umsatz19 ? eur : Money.Null, false);
        }

        if (b.Richtung == Richtung.Eingang)
        {
            var ust = b.AusgewieseneUst ?? Money.Null;
            var vst = art == Besteuerungsart.Regelbesteuerung ? ust : Money.Null;
            log.Add($"Inlandsrechnung: ausgewiesene USt {ust}, Vorsteuer {vst}.");
            return new Betraege(eur - ust, Money.Null, vst, Money.Null, false);
        }

        if (art == Besteuerungsart.Kleinunternehmer)
        {
            log.Add("Kleinunternehmer: Umsatz steuerfrei nach §19 UStG, zählt zum Gesamtumsatz.");
            return new Betraege(eur, Money.Null, Money.Null, umsatz19 ? eur : Money.Null, true);
        }

        var satzWert = PflichtSatz(satz, regel);
        // TODO-STEUER: Brutto/Netto-Interpretation des Betrags je Belegtyp konfigurierbar.
        var modus = regelwerk.Einstellung($"betrag_ist.{b.Typ}", datum);
        Money bmg, st;
        switch (modus)
        {
            case "BRUTTO":
                bmg = new Money(eur.Betrag / (1m + satzWert)).Gerundet();
                st = eur - bmg;
                break;
            case "NETTO":
                bmg = eur;
                st = new Money(eur.Betrag * satzWert).Gerundet();
                break;
            default:
                throw new RegelwerkException($"Unbekannter Wert '{modus}' für betrag_ist.{b.Typ} (BRUTTO|NETTO).");
        }
        log.Add($"Regelbesteuerung ({modus}): BMG {bmg}, Steuer {st}.");
        return new Betraege(bmg, st, Money.Null, umsatz19 ? bmg : Money.Null, false);
    }

    private static decimal PflichtSatz(decimal? satz, Steuerregel regel) =>
        satz ?? throw new RegelwerkException($"Regel {regel.Code} ist steuerpflichtig, hat aber keinen Steuersatz.");

    private SteuerErgebnis Baue(BelegEingabe b, Steuerregel regel, Besteuerungsart art, decimal? kurs, decimal? satz,
        DateOnly entstehung, Betraege p, List<string> log, Uebersteuerung? u)
    {
        var zeitraumArt = Enum.Parse<ZeitraumArt>(regelwerk.Einstellung("ust.voranmeldungszeitraum", entstehung), ignoreCase: true);
        return new SteuerErgebnis
        {
            BelegRef = b.BelegRef,
            Betrieb = b.Betrieb,
            RegelCode = regel.Code,
            Leistungsart = regel.Leistungsart,
            Steuerbarkeit = regel.Steuerbarkeit,
            Leistungsort = regel.Leistungsort,
            Steuerschuldner = regel.Steuerschuldner,
            Besteuerungsart = art,
            KennzahlSchluessel = regel.KennzahlSchluessel,
            Satz = satz,
            Kurs = kurs,
            Entstehungsdatum = entstehung,
            Zeitraum = UstZeitraum.Aus(entstehung, zeitraumArt),
            Bemessungsgrundlage = p.Bmg,
            Steuer = p.Steuer,
            Vorsteuer = p.Vorsteuer,
            Umsatz19 = p.Umsatz19,
            Kennzahlen = p.KeineKennzahlen ? [] : Kennzahlen(regel.KennzahlSchluessel, entstehung.Year, p),
            Begruendung = log,
            IstManuell = u is not null,
            ManuelleBegruendung = u?.Begruendung,
        };
    }

    private List<KennzahlPosition> Kennzahlen(string? schluessel, int jahr, Betraege p)
    {
        var liste = new List<KennzahlPosition>();
        if (schluessel is null) return liste;
        var m = regelwerk.Kennzahl(jahr, schluessel);
        void Add(string? kz, KennzahlArt art, Money betrag)
        {
            if (kz is not null && betrag != Money.Null) liste.Add(new KennzahlPosition(kz, art, betrag));
        }
        Add(m.KzBmg, KennzahlArt.Bemessungsgrundlage, p.Bmg);
        Add(m.KzSteuer, KennzahlArt.Steuer, p.Steuer);
        Add(m.KzVorsteuer, KennzahlArt.Vorsteuer, p.Vorsteuer);
        return liste;
    }
}
