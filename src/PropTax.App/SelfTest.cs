using PropTax.Application;
using PropTax.Domain;
using PropTax.Domain.Werte;

namespace PropTax.App;

/// <summary>CI-Smoke-Test: Engine, Migration einer temporären DB und Abnahmefall 1 über alle Schichten.</summary>
internal static class SelfTest
{
    public static int Ausfuehren()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "proptax-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ab = Arbeitsbereich.Oeffne(ordner, "selftest");
            var partner = ab.Stammdaten.SpeicherePartner(new PartnerDaten { Name = "Selftest Prop", LandIso = "AE" });
            ab.Kurse.Setze(new KursDaten("USD", 2026, 3, 1.0800m, Kursquelle.Bmf));
            var e = ab.Belege.Vorschau(new BelegDaten
            {
                Belegnummer = "ST-1", BetriebId = ab.Stammdaten.Betriebe()[0].Id, PartnerId = partner, Typ = Belegtyp.ChallengeGebuehr,
                Richtung = Richtung.Eingang, Rechnungsdatum = new(2026, 3, 10), Leistungsdatum = new(2026, 3, 10), Betrag = new CurrencyAmount(99m, "USD"),
            });
            var ok = e.Bemessungsgrundlage.Betrag == 91.67m && e.Zahllast.Betrag == 17.42m && ab.SchemaVersion >= 1 && ab.Protokoll.PruefeKette();
            Console.WriteLine(ok ? $"SELFTEST OK (Schema v{ab.SchemaVersion}, Zahllast {e.Zahllast})" : $"SELFTEST FEHLER ({e.Zahllast})");
            return ok ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(ordner, true); } catch (IOException) { }
        }
    }
}
