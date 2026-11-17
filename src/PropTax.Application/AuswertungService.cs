using System.Globalization;
using PropTax.Domain;
using PropTax.Domain.Werte;
using PropTax.Persistence;
using PropTax.Reporting;
using PropTax.TaxEngine.Auswertung;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

public sealed record DrillBeleg(long BelegId, string Belegnummer, DateOnly Datum, string Partner, string Betrieb, decimal BetragFw, string Waehrung, decimal Wert);

public sealed record KennzahlZeile(string Kennzahl, string Bezeichnung, KennzahlArt Art, decimal Betrag, decimal Formularwert, IReadOnlyList<DrillBeleg> Belege);

public sealed record ZeitraumZeile(UstZeitraum Zeitraum, decimal Steuer, decimal Vorsteuer, decimal Zahllast, Faelligkeit Faelligkeit,
    bool Pflicht, string PflichtGrund, bool Festgeschrieben, int AnzahlBelege);

public sealed record UstVaAnsicht(ZeitraumZeile Zeitraum, IReadOnlyList<KennzahlZeile> Kennzahlen, IReadOnlyList<DrillBeleg> Belege);

public sealed record GrenzAnsicht(GrenzStatus Status, decimal Vorjahresumsatz, bool VorjahrManuell, decimal Warnschwelle, Besteuerungsart ArtHeute);

public sealed record EuerPosition(long? BelegId, string Belegnummer, DateOnly Datum, string Betrieb, string Kategorie, string Art,
    decimal BetragEur, decimal? UstEur, decimal Kursdifferenz, string Quelle);

public sealed record EuerZeile(long? KategorieId, string Kategorie, string Art, string? AnlageZeile, decimal Betrag, int Anzahl);

public sealed record EuerAnsicht(int Jahr, string Betrieb, IReadOnlyList<EuerZeile> Zeilen, IReadOnlyList<EuerPosition> Positionen,
    decimal Einnahmen, decimal Ausgaben, decimal Gewinn, decimal Kursdifferenzen, IReadOnlyList<string> Hinweise);

public sealed record DashboardZeile(string Bezeichnung, decimal Gebuehren, decimal Payouts, decimal UstLast, int AnzahlGebuehren, int AnzahlPayouts)
{
    public decimal Netto => Payouts - Gebuehren - UstLast;
}

public sealed record DashboardAnsicht(int Jahr, IReadOnlyList<DashboardZeile> JeFirma, IReadOnlyList<DashboardZeile> JeMonat, DashboardZeile Summe);

public enum ExportFormat { Pdf, Xlsx, Zip, Csv }

/// <summary>Auswertungen auf Basis der gespeicherten Steuer-Snapshots (keine Neuberechnung).</summary>
public sealed class AuswertungService(Arbeitsbereich ab)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Belegtyp[] GebuehrTypen = [Belegtyp.ChallengeGebuehr, Belegtyp.Reset, Belegtyp.Activation, Belegtyp.FundedGebuehr];

    private static readonly Dictionary<Belegtyp, string> StandardKategorie = new()
    {
        [Belegtyp.ChallengeGebuehr] = "PROP_GEBUEHR", [Belegtyp.Reset] = "PROP_GEBUEHR", [Belegtyp.Activation] = "PROP_GEBUEHR",
        [Belegtyp.FundedGebuehr] = "PROP_GEBUEHR", [Belegtyp.AboSoftware] = "SOFTWARE_ABO", [Belegtyp.Payout] = "PAYOUT",
        [Belegtyp.Erstattung] = "ERSTATTUNG", [Belegtyp.Verkauf] = "WARENVERKAUF", [Belegtyp.SonstigeAusgabe] = "SONST_AUSGABE",
        [Belegtyp.SonstigeEinnahme] = "SONST_EINNAHME",
    };

    // ---------- Datenbasis ----------

    private sealed record Paar(BelegZeile Beleg, SteuerErgebnis Ergebnis);

    private sealed class Daten
    {
        public required InMemoryRegelwerk Rw { get; init; }
        public required IReadOnlyList<BelegZeile> Belege { get; init; }
        public required IReadOnlyList<Paar> Gebucht { get; init; }
        public required IReadOnlyDictionary<long, PartnerZeile> Partner { get; init; }
        public required IReadOnlyDictionary<long, string> Betriebe { get; init; }
        public required IReadOnlyList<EuerKategorieZeile> Kategorien { get; init; }
        public required ISet<(string Art, string Schluessel)> Fest { get; init; }
        public required string Unternehmen { get; init; }
        public required Func<int, IReadOnlyList<EuerZeilenZeile>> EuerZeilen { get; init; }
        public required IReadOnlyDictionary<long, IReadOnlyList<DokumentZeile>> Dokumente { get; init; }

        public ZeitraumArt VaArt(int jahr) =>
            Enum.Parse<ZeitraumArt>(Rw.Einstellung("ust.voranmeldungszeitraum", new DateOnly(jahr, 1, 1)), true);

        public Faelligkeit Faelligkeit(UstZeitraum z)
        {
            var ende = Fristen.Ende(z);
            var tag = int.Parse(Rw.Einstellung("ust.va_frist_tag", ende), CultureInfo.InvariantCulture);
            var dfv = string.Equals(Rw.Einstellung("ust.dauerfristverlaengerung", ende), "true", StringComparison.OrdinalIgnoreCase);
            return Fristen.VaFaelligkeit(z, tag, dfv);
        }
    }

    private Daten Lade()
    {
        using var s = ab.Sitzung();
        var betriebe = s.Stammdaten.Betriebe().ToDictionary(b => b.Id, b => b.Name);
        var belege = s.Belege.Alle();
        var nachId = belege.ToDictionary(b => b.Id);
        var gebucht = s.Belege.AlleSteuern().Select(z => new Paar(nachId[z.BelegId], Abbildung.ZuErgebnis(z, nachId[z.BelegId], betriebe[nachId[z.BelegId].BetriebId]))).ToList();
        var zeilenCache = new Dictionary<int, IReadOnlyList<EuerZeilenZeile>>();
        foreach (var j in belege.Select(b => b.EuerPeriode).Where(p => p is not null).Distinct())
        {
            var jahr = int.Parse(j!, CultureInfo.InvariantCulture);
            zeilenCache[jahr] = s.Stammdaten.EuerZeilen(jahr);
        }
        foreach (var j in gebucht.Select(p => p.Ergebnis.Zeitraum.Jahr).Distinct().Where(j => !zeilenCache.ContainsKey(j)))
            zeilenCache[j] = s.Stammdaten.EuerZeilen(j);
        return new Daten
        {
            Rw = s.Regelwerk.Laden(),
            Belege = belege,
            Gebucht = gebucht,
            Partner = s.Stammdaten.Partner().ToDictionary(p => p.Id),
            Betriebe = betriebe,
            Kategorien = s.Stammdaten.EuerKategorien(),
            Fest = s.Perioden.Alle().Where(p => p.Status == "FESTGESCHRIEBEN").Select(p => (p.Art, p.Schluessel)).ToHashSet(),
            Unternehmen = s.Stammdaten.UnternehmenName(),
            EuerZeilen = j => zeilenCache.TryGetValue(j, out var z) ? z : [],
            Dokumente = belege.ToDictionary(b => b.Id, b => s.Dokumente.ZuBeleg(b.Id)),
        };
    }

    // ---------- USt-VA, Zahllast, Fristen ----------

    public IReadOnlyList<UstZeitraum> Zeitraeume(int jahr)
    {
        var d = Lade();
        return ZeitraeumeIntern(d, jahr);
    }

    private static List<UstZeitraum> ZeitraeumeIntern(Daten d, int jahr)
    {
        var art = d.VaArt(jahr);
        return Enumerable.Range(1, art == ZeitraumArt.Monat ? 12 : 4).Select(n => new UstZeitraum(jahr, art, n)).ToList();
    }

    /// <summary>Zahllast-Rechner und Fristenübersicht: je Voranmeldungszeitraum des Jahres.</summary>
    public IReadOnlyList<ZeitraumZeile> Zahllasten(int jahr)
    {
        var d = Lade();
        return ZeitraeumeIntern(d, jahr).Select(z => Zeile(d, z)).ToList();
    }

    private static ZeitraumZeile Zeile(Daten d, UstZeitraum z)
    {
        var e = d.Gebucht.Where(p => p.Ergebnis.Zeitraum == z).Select(p => p.Ergebnis).ToList();
        var steuer = e.Sum(x => x.Steuer.Betrag);
        var vst = e.Sum(x => x.Vorsteuer.Betrag);
        var art = d.Rw.Besteuerungsart(Fristen.Ende(z));
        // TODO-STEUER O-03: KU nur Voranmeldung bei Steuerschuld nach §13b (§18 Abs. 4a UStG).
        var (pflicht, grund) = art == Besteuerungsart.Regelbesteuerung
            ? (true, "Regelbesteuerung")
            : steuer != 0m ? (true, "KU mit §13b-Steuer (§18 Abs. 4a UStG)") : (false, "KU ohne §13b-Steuer – keine Voranmeldung");
        return new ZeitraumZeile(z, steuer, vst, steuer - vst, d.Faelligkeit(z), pflicht, grund, d.Fest.Contains(("UST", z.ToString())), e.Count);
    }

    /// <summary>USt-VA-Vorschau mit Bezeichnungen, Formularwerten und Drill-down bis auf den Beleg.</summary>
    public UstVaAnsicht UstVa(UstZeitraum z)
    {
        var d = Lade();
        var paare = d.Gebucht.Where(p => p.Ergebnis.Zeitraum == z).ToList();
        var rundung = Enum.Parse<FormularRundung>(d.Rw.Einstellung("ust.formular_rundung", Fristen.Ende(z)), true);
        var vorschau = UstVaAggregation.Berechne(paare.Select(p => p.Ergebnis), z, rundung);
        var bezeichnungen = Bezeichnungen(d.Rw, z.Jahr);
        var nachNummer = paare.ToDictionary(p => p.Ergebnis.BelegRef);
        DrillBeleg Drill(SteuerErgebnis e, decimal wert)
        {
            var b = nachNummer[e.BelegRef].Beleg;
            return new DrillBeleg(b.Id, b.Belegnummer, Werte.Datum(b.Leistungsdatum), d.Partner[b.PartnerId].Name, d.Betriebe[b.BetriebId],
                Werte.AusCent(b.BetragFwCent), b.Waehrung, wert);
        }
        var kennzahlen = vorschau.Kennzahlen.Select(k => new KennzahlZeile(k.Kennzahl, bezeichnungen.GetValueOrDefault(k.Kennzahl, "–"), k.Art,
            k.Betrag.Betrag, k.Formularwert, k.Belege.Select(e => Drill(e, e.Kz(k.Kennzahl).Betrag)).ToList())).ToList();
        return new UstVaAnsicht(Zeile(d, z), kennzahlen, paare.Select(p => Drill(p.Ergebnis, p.Ergebnis.Zahllast.Betrag)).ToList());
    }

    private static Dictionary<string, string> Bezeichnungen(InMemoryRegelwerk rw, int jahr)
    {
        var e = new List<(string Kz, string Text)>();
        foreach (var k in rw.Kennzahlen.Where(k => k.Jahr == jahr))
        {
            if (k.KzBmg is not null) e.Add((k.KzBmg, $"{k.Bezeichnung} – Bemessungsgrundlage"));
            if (k.KzSteuer is not null) e.Add((k.KzSteuer, $"{k.Bezeichnung} – Steuer"));
            if (k.KzVorsteuer is not null) e.Add((k.KzVorsteuer, $"Vorsteuer: {k.Bezeichnung}"));
        }
        return e.GroupBy(x => x.Kz).ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(x => x.Text).Distinct()));
    }

    // ---------- §19 ----------

    public GrenzAnsicht Grenze(int jahr)
    {
        var d = Lade();
        var schluessel = $"ust19.vorjahresumsatz.{jahr}";
        var manuell = d.Rw.Einstellungen.Any(e => e.Schluessel == schluessel);
        var vorjahr = manuell
            ? decimal.Parse(d.Rw.Einstellung(schluessel, new DateOnly(jahr, 12, 31)), NumberStyles.Number, CultureInfo.InvariantCulture)
            : d.Gebucht.Where(p => p.Ergebnis.Entstehungsdatum.Year == jahr - 1).Sum(p => p.Ergebnis.Umsatz19.Betrag);
        var schwelle = decimal.Parse(d.Rw.Einstellung("ust19.warnschwelle", new DateOnly(jahr, 1, 1)), NumberStyles.Number, CultureInfo.InvariantCulture);
        var status = Kleinunternehmergrenze.Pruefe(UmsatzPosition.Aus(d.Gebucht.Select(p => p.Ergebnis)), jahr, Money.Euro(vorjahr), d.Rw, schwelle);
        return new GrenzAnsicht(status, vorjahr, manuell, schwelle, d.Rw.Besteuerungsart(DateOnly.FromDateTime(ab.Jetzt.LocalDateTime)));
    }

    /// <summary>Übernimmt den Umschaltvorschlag des §19-Monitors (Regelbesteuerung ab Stichtag).</summary>
    public DateOnly UebernehmeUmschaltung(int jahr, string begruendung)
    {
        var v = Grenze(jahr).Status.Vorschlag ?? throw new FachlicherFehler($"Für {jahr} liegt kein Umschaltvorschlag vor.");
        ab.Einstellungen.SetzeBesteuerungsart(v.AbDatum, Besteuerungsart.Regelbesteuerung, $"{begruendung} ({v.Begruendung})");
        return v.AbDatum;
    }

    /// <summary>Vorjahresumsatz manuell erfassen (für Jahre vor Nutzung der Software).</summary>
    public void SetzeVorjahresumsatz(int jahr, decimal umsatz, string begruendung) =>
        ab.Einstellungen.SetzeEinstellung($"ust19.vorjahresumsatz.{jahr}", umsatz.ToString(CultureInfo.InvariantCulture), new DateOnly(jahr, 1, 1), begruendung);

    // ---------- EÜR ----------

    /// <summary>EÜR nach Zahlungsprinzip (§11 EStG) für einen Betrieb oder alle (betriebId = null).</summary>
    public EuerAnsicht Euer(int jahr, long? betriebId = null)
    {
        var d = Lade();
        var j = jahr.ToString(CultureInfo.InvariantCulture);
        var hinweise = new List<string>();
        var kategorien = d.Kategorien.ToDictionary(k => k.Code);
        var steuer = d.Gebucht.ToDictionary(p => p.Beleg.Id, p => p.Ergebnis);
        var positionen = new List<EuerPosition>();
        var bankFehlt = 0;

        foreach (var b in d.Belege.Where(b => b.Status == "Gebucht" && b.EuerPeriode == j && (betriebId is null || b.BetriebId == betriebId)))
        {
            var typ = Enum.Parse<Belegtyp>(b.Belegtyp);
            var kat = b.EuerKategorieId is { } kid ? d.Kategorien.FirstOrDefault(k => k.Id == kid) : kategorien.GetValueOrDefault(StandardKategorie[typ]);
            var art = kat?.Art ?? (b.Richtung == "Ausgang" || typ == Belegtyp.Erstattung ? "Einnahme" : "Ausgabe");
            var datum = Werte.Datum(b.Zahlungsdatum!);
            var fw = Werte.AusCent(b.BetragFwCent);
            decimal eur;
            string quelle;
            if (b.Waehrung == "EUR") { eur = fw; quelle = "EUR"; }
            else
            {
                // TODO-STEUER O-13: Kurs der EÜR (BMF-Monatskurs des Zahlungsmonats oder Bankkurs) und Behandlung der Kursdifferenz.
                var kursQuelle = d.Rw.Einstellung("euer.kurs_quelle", datum);
                if (string.Equals(kursQuelle, "Bank", StringComparison.OrdinalIgnoreCase) && Werte.DezOpt(b.Bankkurs) is { } bk)
                {
                    eur = Math.Round(fw / bk, 2, MidpointRounding.AwayFromZero);
                    quelle = $"Bank {bk.ToString(De)}";
                }
                else
                {
                    if (string.Equals(kursQuelle, "Bank", StringComparison.OrdinalIgnoreCase)) bankFehlt++;
                    decimal kurs;
                    try { kurs = d.Rw.Kurs(b.Waehrung, datum.Year, datum.Month, Kursquelle.Bmf); }
                    catch (RegelwerkException)
                    {
                        hinweise.Add($"{b.Belegnummer}: kein BMF-Kurs {b.Waehrung} {datum.Month:00}/{datum.Year} – Beleg nicht in der EÜR berücksichtigt.");
                        continue;
                    }
                    eur = Math.Round(fw / kurs, 2, MidpointRounding.AwayFromZero);
                    quelle = $"BMF {datum.Month:00}/{datum.Year} {kurs.ToString(De)}";
                }
            }
            decimal? ustEur = null;
            var diff = 0m;
            if (b.Waehrung != "EUR" && steuer.TryGetValue(b.Id, out var e))
            {
                ustEur = Brutto(e);
                diff = eur - ustEur.Value;
            }
            positionen.Add(new EuerPosition(b.Id, b.Belegnummer, datum, d.Betriebe[b.BetriebId], kat?.Bezeichnung ?? "(ohne Kategorie)", art, eur, ustEur, diff, quelle));
        }
        if (bankFehlt > 0) hinweise.Add($"{bankFehlt} Fremdwährungsbeleg(e) ohne Bankkurs – BMF-Kurs des Zahlungsmonats verwendet.");

        // USt-Zahllast als Betriebsausgabe/-einnahme im (angenommenen) Zahlungszeitpunkt = Fälligkeit (TODO-STEUER O-12).
        var ustKat = kategorien.GetValueOrDefault("UST_13B");
        foreach (var g in d.Gebucht.Where(p => betriebId is null || p.Beleg.BetriebId == betriebId)
                     .GroupBy(p => (p.Ergebnis.Zeitraum, p.Beleg.BetriebId)))
        {
            var zahllast = g.Sum(p => p.Ergebnis.Zahllast.Betrag);
            if (zahllast == 0m || g.Key.Zeitraum.Art == ZeitraumArt.Jahr) continue;
            var f = d.Faelligkeit(g.Key.Zeitraum);
            var regel = d.Rw.Einstellung("euer.ust_zahlung", f.Tatsaechlich);
            var zuordnungsjahr = f.Tatsaechlich.Year;
            if (regel == "FAELLIGKEIT_10_TAGE" && f.Tatsaechlich.Month == 1 && f.Tatsaechlich.Day <= 10 && Fristen.Ende(g.Key.Zeitraum).Year == f.Tatsaechlich.Year - 1)
                zuordnungsjahr--; // §11 Abs. 2 S. 2 EStG (regelmäßig wiederkehrende Ausgabe)
            if (zuordnungsjahr != jahr) continue;
            positionen.Add(new EuerPosition(null, $"USt {g.Key.Zeitraum}", f.Tatsaechlich, d.Betriebe[g.Key.BetriebId],
                zahllast > 0 ? ustKat?.Bezeichnung ?? "Gezahlte Umsatzsteuer" : "Erstattete Umsatzsteuer",
                zahllast > 0 ? "Ausgabe" : "Einnahme", Math.Abs(zahllast), null, 0m, $"Fälligkeit {f.Tatsaechlich:dd.MM.yyyy}"));
        }

        var mapping = d.EuerZeilen(jahr).ToDictionary(z => z.EuerKategorieId, z => z.Zeile);
        var katNachName = d.Kategorien.GroupBy(k => k.Bezeichnung).ToDictionary(g => g.Key, g => g.First());
        var zeilen = positionen.GroupBy(p => (p.Kategorie, p.Art)).Select(g =>
        {
            var k = katNachName.GetValueOrDefault(g.Key.Kategorie);
            return new EuerZeile(k?.Id, g.Key.Kategorie, g.Key.Art, k is not null ? mapping.GetValueOrDefault(k.Id) : null, g.Sum(p => p.BetragEur), g.Count());
        }).OrderBy(z => z.Art == "Einnahme" ? 0 : 1).ThenBy(z => z.Kategorie).ToList();
        var ohneZeile = zeilen.Count(z => z.AnlageZeile is null);
        if (ohneZeile > 0) hinweise.Add($"{ohneZeile} Kategorie(n) ohne Zeile der Anlage EÜR {jahr} – unter Auswertungen › EÜR pflegen (TODO-STEUER O-14).");
        var unbezahlt = d.Belege.Count(b => b.Status == "Gebucht" && b.EuerPeriode is null && (betriebId is null || b.BetriebId == betriebId));
        if (unbezahlt > 0) hinweise.Add($"{unbezahlt} gebuchte(r) Beleg(e) ohne Zahlungsdatum – nicht in der EÜR.");

        var einnahmen = positionen.Where(p => p.Art == "Einnahme").Sum(p => p.BetragEur);
        var ausgaben = positionen.Where(p => p.Art == "Ausgabe").Sum(p => p.BetragEur);
        return new EuerAnsicht(jahr, betriebId is null ? "Gesamt" : d.Betriebe[betriebId.Value], zeilen,
            positionen.OrderBy(p => p.Datum).ThenBy(p => p.Belegnummer).ToList(), einnahmen, ausgaben, einnahmen - ausgaben,
            positionen.Sum(p => p.Kursdifferenz), hinweise);
    }

    /// <summary>EUR-Betrag lt. USt-Snapshot inkl. vom Leistenden ausgewiesener Steuer.</summary>
    private static decimal Brutto(SteuerErgebnis e) =>
        e.Bemessungsgrundlage.Betrag + (e.Steuerschuldner == Steuerschuldner.Leistender ? e.Steuer.Betrag : 0m);

    // ---------- Dashboard ----------

    public DashboardAnsicht Dashboard(int jahr, long? partnerId = null)
    {
        var d = Lade();
        bool IstGebuehr(Paar p) => GebuehrTypen.Contains(Enum.Parse<Belegtyp>(p.Beleg.Belegtyp))
            || (p.Beleg.Belegtyp == nameof(Belegtyp.Erstattung) && p.Beleg.BezugBelegId is { } bz && d.Belege.Any(x => x.Id == bz && GebuehrTypen.Contains(Enum.Parse<Belegtyp>(x.Belegtyp))));
        bool IstPayout(Paar p) => p.Beleg.Belegtyp == nameof(Belegtyp.Payout);
        var relevant = d.Gebucht.Where(p => Werte.Datum(p.Beleg.Leistungsdatum).Year == jahr && (IstGebuehr(p) || IstPayout(p))
            && (partnerId is null || p.Beleg.PartnerId == partnerId)).ToList();

        DashboardZeile Summiere(string name, IEnumerable<Paar> ps)
        {
            var l = ps.ToList();
            var geb = l.Where(IstGebuehr).ToList();
            var pay = l.Where(IstPayout).ToList();
            return new DashboardZeile(name, geb.Sum(p => Brutto(p.Ergebnis)), pay.Sum(p => Brutto(p.Ergebnis)), geb.Sum(p => p.Ergebnis.Zahllast.Betrag),
                geb.Count(p => p.Beleg.StornoVonId is null && p.Beleg.Belegtyp != nameof(Belegtyp.Erstattung)), pay.Count(p => p.Beleg.StornoVonId is null));
        }

        var jeFirma = relevant.GroupBy(p => d.Partner[p.Beleg.PartnerId].Name).Select(g => Summiere(g.Key, g)).OrderByDescending(z => z.Gebuehren).ToList();
        var jeMonat = Enumerable.Range(1, 12).Select(m => Summiere(new DateOnly(jahr, m, 1).ToString("MMMM", De),
            relevant.Where(p => Werte.Datum(p.Beleg.Leistungsdatum).Month == m))).ToList();
        return new DashboardAnsicht(jahr, jeFirma, jeMonat, Summiere("Summe", relevant));
    }

    // ---------- Nachweis & Exporte ----------

    public NachweisBericht Nachweis(int jahr)
    {
        var d = Lade();
        var nummern = d.Belege.ToDictionary(b => b.Id, b => b.Belegnummer);
        var zeilen = d.Gebucht
            .Where(p => p.Ergebnis.Zeitraum.Jahr == jahr && d.Partner[p.Beleg.PartnerId].IstPropFirma != 0
                && (GebuehrTypen.Contains(Enum.Parse<Belegtyp>(p.Beleg.Belegtyp)) || p.Beleg.Belegtyp == nameof(Belegtyp.Erstattung)))
            .OrderBy(p => p.Ergebnis.Entstehungsdatum).ThenBy(p => p.Beleg.Belegnummer)
            .Select(p =>
            {
                var b = p.Beleg;
                var e = p.Ergebnis;
                var hinweis = b.StornoVonId is { } s ? $"Storno zu {nummern[s]}"
                    : b.KorrekturVonId is { } k ? $"Korrektur zu {nummern[k]}"
                    : b.BezugBelegId is { } bz ? $"Erstattung zu {nummern[bz]}" : null;
                if (e.IstManuell) hinweis = string.Join(" · ", new[] { hinweis, $"manuell: {e.ManuelleBegruendung}" }.Where(x => x is not null));
                var partner = d.Partner[b.PartnerId];
                return new NachweisZeile(Werte.Datum(b.Leistungsdatum), b.Belegnummer, partner.Name, partner.LandIso,
                    App.AnzeigeTyp(Enum.Parse<Belegtyp>(b.Belegtyp)), Werte.AusCent(b.BetragFwCent), b.Waehrung, e.Kurs, e.Bemessungsgrundlage.Betrag,
                    e.Steuerschuldner == Steuerschuldner.Leistender ? 0m : e.Steuer.Betrag,
                    string.Join("/", e.Kennzahlen.Where(x => x.Art != KennzahlArt.Vorsteuer).Select(x => x.Kennzahl)),
                    e.Zeitraum.ToString(), hinweis, d.Dokumente[b.Id].Select(x => x.Dateiname).ToList());
            }).ToList();
        return new NachweisBericht(d.Unternehmen, jahr, ab.Jetzt, zeilen);
    }

    /// <summary>Exportiert den Nachweis als PDF, XLSX oder ZIP (inkl. Belegdateien) und protokolliert den Export.</summary>
    public void ExportiereNachweis(int jahr, string ziel, ExportFormat format)
    {
        var bericht = Nachweis(jahr);
        using (var f = File.Create(ziel))
        {
            switch (format)
            {
                case ExportFormat.Pdf: NachweisExport.Pdf(bericht, f); break;
                case ExportFormat.Xlsx: NachweisExport.Xlsx(bericht, f); break;
                case ExportFormat.Zip:
                    var d = Lade();
                    var nummern = bericht.Zeilen.Select(z => z.Belegnummer).ToHashSet();
                    var anhaenge = d.Belege.Where(b => nummern.Contains(b.Belegnummer))
                        .SelectMany(b => d.Dokumente[b.Id].Select(x => new BelegAnhang(b.Belegnummer, x.Dateiname, Path.Combine(ab.Datenordner, x.RelativerPfad))));
                    NachweisExport.Zip(bericht, anhaenge, f);
                    break;
                default: throw new FachlicherFehler($"Format {format} für den Nachweis nicht unterstützt.");
            }
        }
        Protokolliere("nachweis", jahr.ToString(CultureInfo.InvariantCulture), $"{format}: {Path.GetFileName(ziel)} ({bericht.Zeilen.Count} Belege)");
    }

    public Tabelle UstVaTabelle(UstZeitraum z)
    {
        var v = UstVa(z);
        var zeilen = v.Kennzahlen.Select(k => (IReadOnlyList<object?>)[k.Kennzahl, k.Bezeichnung, k.Art.ToString(), k.Betrag, k.Formularwert, k.Belege.Count]).ToList();
        zeilen.Add(["", "Zahllast", "", v.Zeitraum.Zahllast, v.Zeitraum.Zahllast, v.Belege.Count]);
        return new Tabelle($"USt-VA-Vorschau {z}", ["Kz", "Bezeichnung", "Art", "Betrag", "Formularwert", "Belege"], zeilen,
            [$"Fällig {v.Zeitraum.Faelligkeit.Tatsaechlich:dd.MM.yyyy} · {v.Zeitraum.PflichtGrund}", "Kennzahlen-Zuordnung ungeprüft – keine Steuerberatung."]);
    }

    public Tabelle EuerTabelle(int jahr, long? betriebId = null)
    {
        var e = Euer(jahr, betriebId);
        var zeilen = e.Zeilen.Select(z => (IReadOnlyList<object?>)[z.Art, z.Kategorie, z.AnlageZeile ?? "nicht zugeordnet", z.Betrag, z.Anzahl]).ToList();
        zeilen.Add(["", "Summe Einnahmen", "", e.Einnahmen, null]);
        zeilen.Add(["", "Summe Ausgaben", "", e.Ausgaben, null]);
        zeilen.Add(["", "Gewinn/Verlust", "", e.Gewinn, null]);
        zeilen.Add(["", "davon Kursdifferenzen (nachrichtlich)", "", e.Kursdifferenzen, null]);
        return new Tabelle($"EÜR {jahr} – {e.Betrieb}", ["Art", "Kategorie", "Anlage-EÜR-Zeile", "Betrag EUR", "Positionen"], zeilen, e.Hinweise);
    }

    /// <summary>Exportiert eine Auswertungstabelle als XLSX oder CSV.</summary>
    public void ExportiereTabelle(Tabelle t, string ziel, ExportFormat format)
    {
        using (var f = File.Create(ziel))
        {
            if (format == ExportFormat.Xlsx) TabellenExport.Xlsx(t, f);
            else if (format == ExportFormat.Csv) TabellenExport.Csv(t, f);
            else throw new FachlicherFehler($"Format {format} für Tabellen nicht unterstützt.");
        }
        Protokolliere("auswertung", t.Titel, $"{format}: {Path.GetFileName(ziel)}");
    }

    private void Protokolliere(string entitaet, string id, string neu)
    {
        using var s = ab.Sitzung();
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, entitaet, id, "EXPORT", null, null, neu, "Export");
        s.Commit();
    }

    private static class App
    {
        public static string AnzeigeTyp(Belegtyp t) => t switch
        {
            Belegtyp.ChallengeGebuehr => "Challenge-Gebühr",
            Belegtyp.Reset => "Reset",
            Belegtyp.Activation => "Activation",
            Belegtyp.FundedGebuehr => "Funded-Gebühr",
            Belegtyp.Erstattung => "Erstattung",
            _ => t.ToString(),
        };
    }
}
