using PropTax.Domain;
using PropTax.Persistence;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

public sealed class EinstellungenService(Arbeitsbereich ab)
{
    public const string PayoutBehandlung = "ust.payout_behandlung";
    public static readonly string[] PayoutOptionen = ["NICHT_STEUERBAR", "STEUERBAR", "KEIN_LEISTUNGSAUSTAUSCH"];

    /// <summary>Momentaufnahme des Regelwerks (Regeln, Sätze, Kennzahlen, Kurse, Einstellungen).</summary>
    public InMemoryRegelwerk Regelwerk()
    {
        using var s = ab.Sitzung();
        return s.Regelwerk.Laden();
    }

    public string Wert(string schluessel, DateOnly datum) => Regelwerk().Einstellung(schluessel, datum);

    public IReadOnlyList<BesteuerungsartAb> BesteuerungsartHistorie() => Regelwerk().Besteuerungsarten;

    public void SetzeEinstellung(string schluessel, string wert, DateOnly ab_, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        if (schluessel == PayoutBehandlung && !PayoutOptionen.Contains(wert))
            throw new FachlicherFehler($"Ungültige Payout-Behandlung „{wert}“.");
        using var s = ab.Sitzung();
        var alt = s.Regelwerk.Laden().Einstellungen.Where(e => e.Schluessel == schluessel && e.Ab <= ab_).OrderBy(e => e.Ab).LastOrDefault()?.Wert;
        s.Regelwerk.SetzeEinstellung(schluessel, wert, ab_);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "einstellung", schluessel, "AENDERUNG", "ab " + Werte.Datum(ab_), alt, wert, begruendung);
        s.Commit();
    }

    /// <summary>Wechsel KU ↔ Regelbesteuerung mit Stichtag (z. B. aus dem §19-Umschaltvorschlag).</summary>
    public void SetzeBesteuerungsart(DateOnly ab_, Besteuerungsart art, string begruendung)
    {
        PeriodenService.Pflicht(begruendung);
        using var s = ab.Sitzung();
        var alt = s.Regelwerk.Laden().Besteuerungsart(ab_);
        s.Regelwerk.SetzeBesteuerungsart(ab_, art, begruendung);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "besteuerungsart", Werte.Datum(ab_), "AENDERUNG", null, alt.ToString(), art.ToString(), begruendung);
        s.Commit();
    }
}

public sealed class ProtokollService(Arbeitsbereich ab)
{
    public IReadOnlyList<Domain.GoBD.ProtokollEintrag> Alle()
    {
        using var s = ab.Sitzung();
        return s.Protokoll.Alle();
    }

    public IReadOnlyList<Domain.GoBD.ProtokollEintrag> Zu(string entitaet, string id)
    {
        using var s = ab.Sitzung();
        return s.Protokoll.Zu(entitaet, id);
    }

    public bool PruefeKette()
    {
        using var s = ab.Sitzung();
        return s.Protokoll.PruefeKette();
    }
}
