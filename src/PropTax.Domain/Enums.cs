namespace PropTax.Domain;

public enum Belegtyp
{
    ChallengeGebuehr, Reset, Activation, FundedGebuehr, AboSoftware,
    Payout, Erstattung, Verkauf, SonstigeAusgabe, SonstigeEinnahme,
}

public enum Richtung { Eingang, Ausgang }

public enum PartnerKategorie { Inland, Eu, Drittland }

public enum Leistungsart { SonstigeLeistung, Lieferung, KeinLeistungsaustausch }

public enum Steuerbarkeit { Steuerbar, NichtSteuerbar, KeinLeistungsaustausch }

public enum Steuerschuldner { Leistender, Empfaenger13bAbs1, Empfaenger13bAbs2Nr1 }

/// <summary>Zeitpunkt, der USt-Periode und Monatskurs bestimmt (siehe docs/steuerregeln.md O-07).</summary>
public enum Entstehung { LeistungVaZeitraum, RechnungSpaetestensFolgemonat, LeistungUndRechnung, Vereinnahmung }

public enum Besteuerungsart { Kleinunternehmer, Regelbesteuerung }

public enum PruefStatus { Validiert, Ungeprueft, Offen }

public enum Kursquelle { Bmf, Bank, Manuell }

public enum ZeitraumArt { Monat, Quartal, Jahr }
