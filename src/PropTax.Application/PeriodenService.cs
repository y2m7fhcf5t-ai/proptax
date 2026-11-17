using System.Globalization;
using PropTax.Domain;
using PropTax.Domain.GoBD;
using PropTax.Persistence;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Application;

public sealed record PeriodenStand(string Art, string Schluessel, bool Festgeschrieben, string? Am, string? Von);

/// <summary>Periodenstatus für Domain-Prüfungen: USt-Zeitraum (laut Einstellung) oder EÜR-Jahr festgeschrieben.</summary>
internal sealed class PeriodenStatus(Sitzung s, IRegelwerk rw) : IPeriodenStatus
{
    public bool IstFestgeschrieben(DateOnly datum) => UstFest(datum) || EuerFest(datum.Year);

    public bool UstFest(DateOnly datum) => s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, UstZeitraumFuer(datum).ToString());

    public bool EuerFest(int jahr) => s.Perioden.IstFestgeschrieben(PeriodenRepository.Euer, jahr.ToString(CultureInfo.InvariantCulture));

    public UstZeitraum UstZeitraumFuer(DateOnly datum) =>
        UstZeitraum.Aus(datum, Enum.Parse<ZeitraumArt>(rw.Einstellung("ust.voranmeldungszeitraum", datum), true));
}

public sealed class PeriodenService(Arbeitsbereich ab)
{
    public IReadOnlyList<PeriodenStand> Festgeschriebene()
    {
        using var s = ab.Sitzung();
        return s.Perioden.Alle().Select(p => new PeriodenStand(p.Art, p.Schluessel, p.Status == "FESTGESCHRIEBEN", p.FestgeschriebenAm, p.FestgeschriebenVon)).ToList();
    }

    public bool IstUstFestgeschrieben(UstZeitraum z)
    {
        using var s = ab.Sitzung();
        return s.Perioden.IstFestgeschrieben(PeriodenRepository.Ust, z.ToString());
    }

    /// <summary>Schreibt einen USt-Zeitraum endgültig fest. Nicht möglich, solange Entwürfe in diesem Zeitraum liegen.</summary>
    public void FestschreibenUst(UstZeitraum z, string begruendung)
    {
        Pflicht(begruendung);
        using var s = ab.Sitzung();
        var rw = s.Regelwerk.Laden();
        var status = new PeriodenStatus(s, rw);
        var entwuerfe = s.Belege.NachStatus("Entwurf")
            .Where(b => status.UstZeitraumFuer(Werte.Datum(b.Leistungsdatum)) == z).Select(b => b.Belegnummer).ToList();
        if (entwuerfe.Count > 0)
            throw new FachlicherFehler($"Festschreibung {z} nicht möglich: {entwuerfe.Count} Entwurf/Entwürfe ({string.Join(", ", entwuerfe.Take(5))}).");
        s.Perioden.Festschreiben(PeriodenRepository.Ust, z.ToString(), Werte.Zeit(ab.Jetzt), ab.Benutzer);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "periode", "UST " + z, "FESTSCHREIBUNG", null, "OFFEN", "FESTGESCHRIEBEN", begruendung);
        s.Commit();
    }

    /// <summary>Schreibt ein EÜR-Jahr fest. Nicht möglich, solange Entwürfe mit Zahlung in diesem Jahr existieren.</summary>
    public void FestschreibenEuer(int jahr, string begruendung)
    {
        Pflicht(begruendung);
        using var s = ab.Sitzung();
        var j = jahr.ToString(CultureInfo.InvariantCulture);
        var entwuerfe = s.Belege.NachStatus("Entwurf").Count(b => b.EuerPeriode == j);
        if (entwuerfe > 0) throw new FachlicherFehler($"Festschreibung EÜR {jahr} nicht möglich: {entwuerfe} Entwurf/Entwürfe mit Zahlung in {jahr}.");
        s.Perioden.Festschreiben(PeriodenRepository.Euer, j, Werte.Zeit(ab.Jetzt), ab.Benutzer);
        s.Protokoll.Anhaengen(ab.Jetzt, ab.Benutzer, "periode", "EUER " + j, "FESTSCHREIBUNG", null, "OFFEN", "FESTGESCHRIEBEN", begruendung);
        s.Commit();
    }

    internal static void Pflicht(string begruendung)
    {
        if (string.IsNullOrWhiteSpace(begruendung)) throw new FachlicherFehler("Eine Begründung ist Pflicht.");
    }
}
