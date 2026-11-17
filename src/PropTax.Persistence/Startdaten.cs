using Dapper;
using PropTax.TaxEngine.Regelwerk;

namespace PropTax.Persistence;

/// <summary>Initiale Daten einer neuen Datenbank. Wird nur ausgeführt, wenn noch kein Unternehmen existiert.</summary>
public static class Startdaten
{
    /// <summary>Vom Nutzer genannte Prop-Firmen. Land/Kategorie bewusst leer (nicht geraten) → vor dem Buchen pflegen.</summary>
    public static readonly string[] PropFirmen = ["IQ Capital", "Funded Next", "4Prop", "LucidTrading", "Tradifei"];

    public static readonly (string Code, string Bezeichnung, string Art)[] EuerKategorien =
    [
        ("PROP_GEBUEHR", "Prop-Firmen-Gebühren (Challenge, Reset, Activation)", "Ausgabe"),
        ("SOFTWARE_ABO", "Software, Plattformen, Datenfeeds", "Ausgabe"),
        ("UST_13B", "Gezahlte Umsatzsteuer §13b (Kleinunternehmer)", "Ausgabe"),
        ("WARENEINKAUF", "Wareneinkauf Onlinehandel", "Ausgabe"),
        ("SONST_AUSGABE", "Sonstige Betriebsausgaben", "Ausgabe"),
        ("PAYOUT", "Payouts Prop-Firmen", "Einnahme"),
        ("ERSTATTUNG", "Erstattungen von Betriebsausgaben", "Einnahme"),
        ("WARENVERKAUF", "Umsatzerlöse Onlinehandel", "Einnahme"),
        ("SONST_EINNAHME", "Sonstige Betriebseinnahmen", "Einnahme"),
    ];

    public static bool Einspielen(Sitzung s, InMemoryRegelwerk startwerte, DateTimeOffset jetzt, string benutzer)
    {
        var c = s.Verbindung;
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM unternehmen", transaction: s.Transaktion) > 0) return false;

        var unternehmenId = c.ExecuteScalar<long>("INSERT INTO unternehmen (name) VALUES ('Mein Unternehmen'); SELECT last_insert_rowid();",
            transaction: s.Transaktion);
        var stamm = s.Stammdaten;
        stamm.SpeichereBetrieb(new BetriebZeile { UnternehmenId = unternehmenId, Name = "Prop-Trading", Kurzzeichen = "PT" });
        stamm.SpeichereBetrieb(new BetriebZeile { UnternehmenId = unternehmenId, Name = "Onlinehandel", Kurzzeichen = "OH" });
        foreach (var name in PropFirmen)
            stamm.SpeicherePartner(new PartnerZeile { Name = name, IstPropFirma = 1, StdBelegtyp = "ChallengeGebuehr", Notiz = "Land/Kategorie laut Rechnung ergänzen" });
        foreach (var (code, bez, art) in EuerKategorien)
            stamm.SpeichereEuerKategorie(new EuerKategorieZeile { Code = code, Bezeichnung = bez, Art = art });
        s.Regelwerk.Einspielen(startwerte);
        s.Protokoll.Anhaengen(jetzt, benutzer, "datenbank", "1", "INITIALISIERUNG", null, null, null,
            "Neue Datenbank mit Startwerten angelegt (Regelwerk ungeprüft).");
        return true;
    }
}
