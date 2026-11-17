using System.Globalization;
using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.Domain.Werte;
using PropTax.Persistence;
using PropTax.TaxEngine.Ermittlung;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

/// <summary>
/// Belegfluss: Entwurf → (Engine) → Gebucht mit Steuer-Snapshot. Gebuchte Belege in festgeschriebenen Perioden
/// werden nur per Storno/Korrektur berichtigt. Jede Aktion wird protokolliert.
/// </summary>
public sealed class BelegService(Arbeitsbereich ab)
{
    private const string Entitaet = "beleg";

    // ---------- Lesen ----------

    public IReadOnlyList<BelegAnsicht> Liste(BelegFilter? filter = null)
    {
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        IEnumerable<BelegZeile> belege = s.Belege.Alle();
        if (filter is not null)
        {
            if (filter.Von is { } von) belege = belege.Where(b => Werte.Datum(b.Leistungsdatum) >= von);
            if (filter.Bis is { } bis) belege = belege.Where(b => Werte.Datum(b.Leistungsdatum) <= bis);
            if (filter.BetriebId is { } bt) belege = belege.Where(b => b.BetriebId == bt);
            if (filter.PartnerId is { } pt) belege = belege.Where(b => b.PartnerId == pt);
            if (filter.Status is { } st) belege = belege.Where(b => b.Status == st.ToString());
            if (!string.IsNullOrWhiteSpace(filter.Suchtext))
            {
                var t = filter.Suchtext.Trim();
                belege = belege.Where(b => b.Belegnummer.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || (b.Notiz?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                    || ctx.Partner[b.PartnerId].Name.Contains(t, StringComparison.OrdinalIgnoreCase));
            }
        }
        return belege.Select(b => Ansicht(s, ctx, b)).ToList();
    }

    public BelegAnsicht Laden(long id)
    {
        using var s = ab.Sitzung();
        return Ansicht(s, Kontext.Laden(s), LadeZeile(s, id));
    }

    /// <summary>Alle gespeicherten Steuerergebnisse gebuchter Belege (Grundlage der Auswertungen).</summary>
    public IReadOnlyList<SteuerErgebnis> Steuerergebnisse()
    {
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        var belege = s.Belege.Alle().ToDictionary(b => b.Id);
        return s.Belege.AlleSteuern().Select(z => Abbildung.ZuErgebnis(z, belege[z.BelegId], ctx.Betriebe[belege[z.BelegId].BetriebId])).ToList();
    }

    public string NaechsteBelegnummer(DateOnly datum, string praefix = "B")
    {
        using var s = ab.Sitzung();
        var basis = $"{praefix}-{datum.Year}-";
        var n = s.Belege.Alle().Count(b => b.Belegnummer.StartsWith(basis, StringComparison.Ordinal)) + 1;
        while (s.Belege.NummerVergeben(basis + n.ToString("00000", CultureInfo.InvariantCulture))) n++;
        return basis + n.ToString("00000", CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<BelegAnsicht> MoeglicheDubletten(BelegDaten d, long ausserId = 0)
    {
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        return s.Belege.Dubletten(Abbildung.DublettenSchluessel(d), ausserId).Select(b => Ansicht(s, ctx, b)).ToList();
    }

    // ---------- Entwürfe ----------

    public long ErfasseEntwurf(BelegDaten d, long? importLaufId = null)
    {
        using var s = ab.Sitzung();
        var id = ErfasseEntwurf(s, d, importLaufId);
        s.Commit();
        return id;
    }

    internal long ErfasseEntwurf(Sitzung s, BelegDaten d, long? importLaufId, long? stornoVon = null, long? korrekturVon = null)
    {
        Validiere(s, d);
        if (s.Belege.NummerVergeben(d.Belegnummer.Trim())) throw new FachlicherFehler($"Belegnummer {d.Belegnummer} ist bereits vergeben.");
        var z = Abbildung.ZuZeile(d, BelegStatus.Entwurf, Werte.Zeit(ab.Jetzt));
        z.ImportLaufId = importLaufId;
        z.StornoVonId = stornoVon;
        z.KorrekturVonId = korrekturVon;
        var id = s.Belege.Einfuegen(z);
        Protokolliere(s, id, "ANLAGE", null, null, Abbildung.Beschreibe(d), importLaufId is null ? "Erfassung" : $"CSV-Import {importLaufId}");
        return id;
    }

    public void AendereEntwurf(long id, BelegDaten d)
    {
        using var s = ab.Sitzung();
        var z = LadeZeile(s, id);
        if (z.Status != nameof(BelegStatus.Entwurf)) throw new FachlicherFehler("Nur Entwürfe können frei bearbeitet werden; gebuchte Belege über „Ändern mit Begründung“.");
        Validiere(s, d);
        var andere = s.Belege.NachNummer(d.Belegnummer.Trim());
        if (andere is not null && andere.Id != id) throw new FachlicherFehler($"Belegnummer {d.Belegnummer} ist bereits vergeben.");
        var alt = Abbildung.ZuDaten(z);
        s.Belege.Aktualisieren(Abbildung.ZuZeile(d, BelegStatus.Entwurf, z.AngelegtAm, z));
        foreach (var (feld, a, n) in Abbildung.Unterschiede(alt, d))
            Protokolliere(s, id, "AENDERUNG", feld, a, n, "Entwurf bearbeitet");
        s.Commit();
    }

    public void LoescheEntwurf(long id, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        using var s = ab.Sitzung();
        var z = LadeZeile(s, id);
        if (z.Status != nameof(BelegStatus.Entwurf)) throw new FachlicherFehler("Gebuchte Belege können nicht gelöscht werden (Storno verwenden).");
        if (s.Dokumente.ZuBeleg(id).Count > 0) throw new FachlicherFehler("Entwurf hat zugeordnete Dokumente und kann nicht gelöscht werden.");
        s.Belege.Loeschen(id);
        Protokolliere(s, id, "LOESCHUNG", null, Abbildung.Beschreibe(Abbildung.ZuDaten(z)), null, begruendung);
        s.Commit();
    }

    // ---------- Steuer & Buchung ----------

    /// <summary>Berechnet das Steuerergebnis, ohne zu speichern (Vorschau in der Maske).</summary>
    public SteuerErgebnis Vorschau(long id, Uebersteuerung? u = null)
    {
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        return Berechne(s, ctx, LadeZeile(s, id), u);
    }

    /// <summary>Vorschau für noch nicht gespeicherte Daten (Live-Berechnung in der Erfassungsmaske).</summary>
    public SteuerErgebnis Vorschau(BelegDaten d, Uebersteuerung? u = null)
    {
        using var s = ab.Sitzung();
        Validiere(s, d);
        var ctx = Kontext.Laden(s);
        return Berechne(s, ctx, Abbildung.ZuZeile(d, BelegStatus.Entwurf, ""), u);
    }

    public SteuerErgebnis Buche(long id, Uebersteuerung? u = null)
    {
        using var s = ab.Sitzung();
        var e = Buche(s, id, u);
        s.Commit();
        return e;
    }

    internal SteuerErgebnis Buche(Sitzung s, long id, Uebersteuerung? u)
    {
        var ctx = Kontext.Laden(s);
        var z = LadeZeile(s, id);
        if (z.Status != nameof(BelegStatus.Entwurf)) throw new FachlicherFehler($"Beleg {z.Belegnummer} ist bereits gebucht.");
        var e = Berechne(s, ctx, z, u);
        PruefeOffen(s, ctx, e, z);
        s.Belege.SpeichereSteuer(Abbildung.ZuZeile(id, e, Werte.Zeit(ab.Jetzt)));
        z.Status = nameof(BelegStatus.Gebucht);
        s.Belege.Aktualisieren(z);
        Protokolliere(s, id, "BUCHUNG", null, null,
            $"{e.RegelCode} | {e.Zeitraum} | BMG {e.Bemessungsgrundlage.Betrag} | USt {e.Steuer.Betrag} | VSt {e.Vorsteuer.Betrag}",
            u?.Begruendung ?? "Buchung");
        if (u is not null) Protokolliere(s, id, "UEBERSTEUERUNG", null, null, BeschreibeUebersteuerung(u), u.Begruendung);
        return e;
    }

    /// <summary>Sammelbuchung; jeder Beleg in eigener Transaktion. Liefert Fehlermeldungen je Beleg.</summary>
    public IReadOnlyDictionary<long, string> BucheAlle(IEnumerable<long> ids)
    {
        var fehler = new Dictionary<long, string>();
        foreach (var id in ids)
        {
            try { Buche(id); }
            catch (Exception ex) when (ex is FachlicherFehler or RegelwerkException or PeriodeFestgeschriebenException)
            {
                fehler[id] = ex.Message;
            }
        }
        return fehler;
    }

    /// <summary>Ändert einen gebuchten Beleg in offener Periode; das Steuerergebnis wird neu berechnet.</summary>
    public SteuerErgebnis AendereGebucht(long id, BelegDaten d, string begruendung, Uebersteuerung? u = null)
    {
        PeriodenService.Pflicht(begruendung);
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        var z = LadeZeile(s, id);
        if (z.Status != nameof(BelegStatus.Gebucht)) throw new FachlicherFehler("Beleg ist nicht gebucht.");
        if (z.StornoVonId is not null || s.Belege.StornoZu(id) is not null) throw new FachlicherFehler("Stornierte Belege und Stornobelege sind unveränderlich.");
        var altSteuer = s.Belege.Steuer(id)!;
        PruefeOffen(s, ctx, Abbildung.ZuErgebnis(altSteuer, z, ""), z);

        Validiere(s, d);
        var andere = s.Belege.NachNummer(d.Belegnummer.Trim());
        if (andere is not null && andere.Id != id) throw new FachlicherFehler($"Belegnummer {d.Belegnummer} ist bereits vergeben.");
        var alt = Abbildung.ZuDaten(z);
        var neuZeile = Abbildung.ZuZeile(d, BelegStatus.Gebucht, z.AngelegtAm, z);
        var e = Berechne(s, ctx, neuZeile, u);
        PruefeOffen(s, ctx, e, neuZeile);

        s.Belege.Aktualisieren(neuZeile);
        s.Belege.SpeichereSteuer(Abbildung.ZuZeile(id, e, Werte.Zeit(ab.Jetzt)));
        foreach (var (feld, a, n) in Abbildung.Unterschiede(alt, d))
            Protokolliere(s, id, "AENDERUNG", feld, a, n, begruendung);
        Protokolliere(s, id, "NEUBERECHNUNG", null, $"{altSteuer.UstPeriode} | USt {Werte.AusCent(altSteuer.SteuerCent)}",
            $"{e.Zeitraum} | USt {e.Steuer.Betrag}", begruendung);
        s.Commit();
        return e;
    }

    /// <summary>Storno: Gegenbeleg mit negierten Beträgen und negiertem Steuerergebnis im Zeitraum des Stornodatums.</summary>
    public long Storniere(long id, DateOnly stornodatum, string begruendung)
    {
        using var s = ab.Sitzung();
        var stornoId = Storniere(s, id, stornodatum, begruendung);
        s.Commit();
        return stornoId;
    }

    internal long Storniere(Sitzung s, long id, DateOnly datum, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        var ctx = Kontext.Laden(s);
        var z = LadeZeile(s, id);
        if (z.Status != nameof(BelegStatus.Gebucht)) throw new FachlicherFehler("Nur gebuchte Belege können storniert werden (Entwürfe löschen).");
        if (z.StornoVonId is not null) throw new FachlicherFehler("Ein Stornobeleg kann nicht storniert werden.");
        if (s.Belege.StornoZu(id) is not null) throw new FachlicherFehler($"Beleg {z.Belegnummer} ist bereits storniert.");

        // Domain-Regel: Storno nur in offene Periode, mit Begründung, protokolliert (Fall 7).
        var status = new PeriodenStatus(s, ctx.Regelwerk);
        // Protokoll-Schlüssel ist (wie überall) die Beleg-ID.
        var original = new Buchung(id.ToString(CultureInfo.InvariantCulture), Werte.Datum(z.Leistungsdatum),
            new CurrencyAmount(Werte.AusCent(z.BetragFwCent), z.Waehrung), z.Belegnummer);
        var storno = new Belegaenderung(status, s.Protokoll, ab.Benutzer, ab.Zeit).Stornieren(original, datum, begruendung);
        var stornoNummer = z.Belegnummer + "-S";
        for (var n = 2; s.Belege.NummerVergeben(stornoNummer); n++) stornoNummer = $"{z.Belegnummer}-S{n}";

        var daten = Abbildung.ZuDaten(z) with
        {
            Belegnummer = stornoNummer,
            Rechnungsdatum = datum,
            Leistungsdatum = datum,
            Zahlungsdatum = z.Zahlungsdatum is null ? null : datum,
            Betrag = storno.Betrag,
            AusgewieseneUst = Werte.MoneyAusCent(z.AusgewieseneUstCent) is { } u ? -u : null,
            Notiz = "Storno: " + begruendung,
        };
        var zeile = Abbildung.ZuZeile(daten, BelegStatus.Gebucht, Werte.Zeit(ab.Jetzt));
        zeile.StornoVonId = id;
        var stornoId = s.Belege.Einfuegen(zeile);
        var originalErgebnis = Abbildung.ZuErgebnis(s.Belege.Steuer(id)!, z, ctx.Betriebe[z.BetriebId]);
        var e = Abbildung.Negiert(originalErgebnis, stornoNummer, datum, status.UstZeitraumFuer(datum), begruendung);
        s.Belege.SpeichereSteuer(Abbildung.ZuZeile(stornoId, e, Werte.Zeit(ab.Jetzt)));
        Protokolliere(s, stornoId, "ANLAGE", null, null, Abbildung.Beschreibe(daten), "Storno zu " + z.Belegnummer);
        return stornoId;
    }

    /// <summary>Korrektur: Storno des Originals + neuer, gebuchter Beleg mit Verweis.</summary>
    public (long StornoId, long NeuId) Korrigiere(long id, BelegDaten neu, DateOnly datum, string begruendung)
    {
        using var s = ab.Sitzung();
        var stornoId = Storniere(s, id, datum, begruendung);
        var neuId = ErfasseEntwurf(s, neu, null, korrekturVon: id);
        Buche(s, neuId, null);
        Protokolliere(s, id, "KORREKTUR", null, null, neu.Belegnummer, begruendung);
        s.Commit();
        return (stornoId, neuId);
    }

    /// <summary>Mehrfachbearbeitung: EÜR-Kategorie für mehrere Belege setzen (festgeschriebene werden übersprungen).</summary>
    public IReadOnlyDictionary<long, string> SetzeEuerKategorie(IEnumerable<long> ids, long? kategorieId, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        var fehler = new Dictionary<long, string>();
        foreach (var id in ids)
        {
            using var s = ab.Sitzung();
            var ctx = Kontext.Laden(s);
            var z = LadeZeile(s, id);
            var steuer = s.Belege.Steuer(id);
            var status = new PeriodenStatus(s, ctx.Regelwerk);
            if ((steuer is not null && s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, steuer.UstPeriode))
                || (z.EuerPeriode is not null && status.EuerFest(int.Parse(z.EuerPeriode, CultureInfo.InvariantCulture))))
            {
                fehler[id] = $"{z.Belegnummer}: Periode festgeschrieben.";
                continue;
            }
            var alt = z.EuerKategorieId;
            z.EuerKategorieId = kategorieId;
            s.Belege.Aktualisieren(z);
            Protokolliere(s, id, "AENDERUNG", "EuerKategorieId", alt?.ToString(CultureInfo.InvariantCulture),
                kategorieId?.ToString(CultureInfo.InvariantCulture), begruendung);
            s.Commit();
        }
        return fehler;
    }

    /// <summary>
    /// Berechnet gebuchte, nicht übersteuerte Belege in offenen Perioden neu (z. B. nach Kurs- oder Regeländerung).
    /// Liefert die Anzahl geänderter Ergebnisse.
    /// </summary>
    public int NeuBerechnen(string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        using var s = ab.Sitzung();
        var ctx = Kontext.Laden(s);
        var geaendert = 0;
        foreach (var z in s.Belege.NachStatus(nameof(BelegStatus.Gebucht)).Where(b => b.StornoVonId is null))
        {
            var alt = s.Belege.Steuer(z.Id)!;
            if (alt.IstManuell != 0 || s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, alt.UstPeriode)) continue;
            SteuerErgebnis e;
            try { e = Berechne(s, ctx, z, null); }
            catch (RegelwerkException) { continue; }
            var neu = Abbildung.ZuZeile(z.Id, e, Werte.Zeit(ab.Jetzt));
            if (neu.UstPeriode == alt.UstPeriode && neu.BmgCent == alt.BmgCent && neu.SteuerCent == alt.SteuerCent
                && neu.VorsteuerCent == alt.VorsteuerCent && neu.KennzahlenJson == alt.KennzahlenJson && neu.Umsatz19Cent == alt.Umsatz19Cent)
                continue;
            if (s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, neu.UstPeriode)) continue;
            s.Belege.SpeichereSteuer(neu);
            Protokolliere(s, z.Id, "NEUBERECHNUNG", null, $"{alt.UstPeriode} | USt {Werte.AusCent(alt.SteuerCent)}",
                $"{neu.UstPeriode} | USt {Werte.AusCent(neu.SteuerCent)}", begruendung);
            geaendert++;
        }
        s.Commit();
        return geaendert;
    }

    // ---------- intern ----------

    private sealed record Kontext(
        InMemoryRegelwerk Regelwerk,
        IReadOnlyDictionary<long, PartnerZeile> Partner,
        IReadOnlyDictionary<long, string> Betriebe)
    {
        public static Kontext Laden(Sitzung s) => new(
            s.Regelwerk.Laden(),
            s.Stammdaten.Partner().ToDictionary(p => p.Id),
            s.Stammdaten.Betriebe().ToDictionary(b => b.Id, b => b.Name));
    }

    private static BelegZeile LadeZeile(Sitzung s, long id) =>
        s.Belege.Laden(id) ?? throw new FachlicherFehler($"Beleg {id} nicht gefunden.");

    private static void Validiere(Sitzung s, BelegDaten d)
    {
        if (string.IsNullOrWhiteSpace(d.Belegnummer)) throw new FachlicherFehler("Belegnummer fehlt.");
        if (d.Betrag.Betrag <= 0m) throw new FachlicherFehler("Betrag muss positiv sein (Erstattungen/Stornos über eigene Funktionen).");
        if (d.Betrag.Waehrung is not { Length: 3 }) throw new FachlicherFehler("Währung bitte als ISO-Code (EUR, USD, …).");
        if (s.Stammdaten.Betrieb(d.BetriebId) is null) throw new FachlicherFehler("Betrieb fehlt oder unbekannt.");
        if (s.Stammdaten.Partner(d.PartnerId) is null) throw new FachlicherFehler("Partner fehlt oder unbekannt.");
        if (d.Typ == Belegtyp.Erstattung && d.BezugBelegId is null) throw new FachlicherFehler("Erstattung benötigt einen Ursprungsbeleg.");
        if (d.AusgewieseneUst is { } ust && ust.Betrag < 0m) throw new FachlicherFehler("Ausgewiesene USt darf nicht negativ sein.");
        _ = Werte.Cent(d.Betrag.Betrag);
    }

    private SteuerErgebnis Berechne(Sitzung s, Kontext ctx, BelegZeile z, Uebersteuerung? u)
    {
        var partner = ctx.Partner[z.PartnerId];
        var kategorie = partner.Kategorie is null
            ? throw new FachlicherFehler($"Partner „{partner.Name}“ hat keine Kategorie (Inland/EU/Drittland). Bitte Land in den Stammdaten pflegen.")
            : Enum.Parse<PartnerKategorie>(partner.Kategorie);

        Erstattungsbezug? bezug = null;
        if (z.BezugBelegId is { } bezugId)
        {
            var o = LadeZeile(s, bezugId);
            var oSteuer = s.Belege.Steuer(bezugId) ?? throw new FachlicherFehler($"Ursprungsbeleg {o.Belegnummer} ist nicht gebucht.");
            var oKat = Enum.Parse<PartnerKategorie>(ctx.Partner[o.PartnerId].Kategorie!);
            bezug = new Erstattungsbezug(Abbildung.ZuEingabe(o, oKat, ctx.Betriebe[o.BetriebId], null),
                Abbildung.ZuErgebnis(oSteuer, o, ctx.Betriebe[o.BetriebId]));
        }
        var eingabe = Abbildung.ZuEingabe(z, kategorie, ctx.Betriebe[z.BetriebId], bezug);
        return new Steuerermittlung(ctx.Regelwerk).Ermittle(eingabe, u);
    }

    private static void PruefeOffen(Sitzung s, Kontext ctx, SteuerErgebnis e, BelegZeile z)
    {
        if (s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, e.Zeitraum.ToString()))
            throw new PeriodeFestgeschriebenException(e.Entstehungsdatum);
        if (z.EuerPeriode is not null && new PeriodenStatus(s, ctx.Regelwerk).EuerFest(int.Parse(z.EuerPeriode, CultureInfo.InvariantCulture)))
            throw new PeriodeFestgeschriebenException(Werte.Datum(z.Zahlungsdatum!));
    }

    private BelegAnsicht Ansicht(Sitzung s, Kontext ctx, BelegZeile z)
    {
        var steuer = s.Belege.Steuer(z.Id);
        var partner = ctx.Partner[z.PartnerId];
        return new BelegAnsicht(z.Id, Abbildung.ZuDaten(z), Enum.Parse<BelegStatus>(z.Status), ctx.Betriebe[z.BetriebId], partner.Name,
            partner.Kategorie is null ? null : Enum.Parse<PartnerKategorie>(partner.Kategorie),
            steuer is null ? null : Abbildung.ZuErgebnis(steuer, z, ctx.Betriebe[z.BetriebId]),
            z.StornoVonId, s.Belege.StornoZu(z.Id), z.KorrekturVonId, s.Dokumente.ZuBeleg(z.Id).Count,
            z.StornoVonId is null && s.Belege.Dubletten(z.DublettenSchluessel, z.Id).Any(d => d.StornoVonId is null));
    }

    private void Protokolliere(Sitzung s, long id, string aktion, string? feld, string? alt, string? neu, string begruendung) =>
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, Entitaet, id.ToString(CultureInfo.InvariantCulture), aktion, feld, alt, neu, begruendung);

    private static string BeschreibeUebersteuerung(Uebersteuerung u) => string.Join("; ", new[]
    {
        u.Steuerbarkeit is null ? null : $"Steuerbarkeit={u.Steuerbarkeit}",
        u.Steuerschuldner is null ? null : $"Steuerschuldner={u.Steuerschuldner}",
        u.Satz is null ? null : $"Satz={u.Satz.Value.ToString(CultureInfo.InvariantCulture)}",
        u.Kurs is null ? null : $"Kurs={u.Kurs.Value.ToString(CultureInfo.InvariantCulture)}",
        u.KennzahlSchluessel is null ? null : $"Kennzahl={u.KennzahlSchluessel}",
        u.Entstehungsdatum is null ? null : $"Entstehung={Werte.Datum(u.Entstehungsdatum.Value)}",
    }.Where(x => x is not null));
}
