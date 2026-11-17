# Steuerregeln

> Erfassungs- und Berechnungswerkzeug, **keine Steuerberatung**. Alle Regeln, Sätze, Grenzen und Kennzahlen
> sind vor produktivem Einsatz fachlich zu validieren (Steuerberatung).
> Status: `validiert` (gesetzlich eindeutig, geprüft) | `ungeprüft` (umgesetzt, fachlich nicht bestätigt) | `offen` (Entscheidung nötig).

Quelle der Startwerte: `src/PropTax.TaxEngine/Startwerte/regelwerk.startwerte.json`. Alle Werte stehen in Tabellen und
sind konfigurierbar, im Code gibt es keine fachlichen Konstanten.

## Allgemeine Rechenregeln

| ID | Regel | Grundlage | Umsetzung | Test | Status |
|---|---|---|---|---|---|
| R-01 | Umrechnung Fremdwährung mit monatlichem BMF-Kurs des Entstehungsmonats; Notation 1 EUR = x FW | §16 Abs. 6 UStG | `Steuerermittlung`, `IRegelwerk.Kurs`; fehlender Kurs → Fehler | Fall 1, `Fehlender_Kurs_ist_Fehler` | ungeprüft |
| R-02 | Rundung je Beleg: BMG = round(FW/Kurs, 2), Steuer = round(BMG × Satz, 2), kaufmännisch | – (Konvention) | `Money.Gerundet()` | `Rundung_ist_kaufmaennisch`, Fall 1 | ungeprüft |
| R-03 | Steuersatz datumsabhängig: 19 % / 16 % (01.07.–31.12.2020) / 19 %; ermäßigt 7 % / 5 % / 7 % | §12 UStG, Zweites Corona-Steuerhilfegesetz | Tabelle `steuersatz` | `Steuersatz_datumsabhaengig` | **validiert** |
| R-04 | Regelauswahl: höchste Priorität, bei Gleichstand oder ohne Treffer Fehler | – | `WaehleRegel` | `Mehrdeutige_Regeln…`, `Keine_passende_Regel…` | validiert (technisch) |
| R-05 | Manuelle Übersteuerung nur mit Begründung; Ergebnis wird mit `IstManuell` markiert | GoBD (Nachvollziehbarkeit) | `Uebersteuerung` | `Uebersteuerung_*` | validiert (technisch) |

## Regeln der Regeltabelle

| Code | Fall | Leistungsart / -ort | Steuerschuldner | Kennzahlen (Schlüssel) | Entstehung | §19-Umsatz | Grundlage | Status |
|---|---|---|---|---|---|---|---|---|
| `EINGANG_DRITTLAND_SONST_LEIST` | Prop-Gebühren und Abos von Drittland-Unternehmern (US, AE, UK) | sonstige Leistung, Inland (B2B) | Empfänger | 52 / 53 / VSt 67 (`13B_ABS2_NR1`) | Rechnung, spätestens Ablauf Folgemonat | nein | §3a Abs. 2, §13b Abs. 2 Nr. 1, Abs. 2 S. 1, Abs. 5, Abs. 8 UStG | ungeprüft |
| `EINGANG_EU_SONST_LEIST` | dto. von EU-Unternehmern | sonstige Leistung, Inland | Empfänger | 46 / 47 / VSt 67 (`13B_ABS1`) | Ablauf des VA-Zeitraums der Leistung | nein | §3a Abs. 2, §13b Abs. 1 UStG | ungeprüft |
| `EINGANG_INLAND` | Inlandsrechnungen | sonstige Leistung, Inland | Leistender | VSt 66 (`VST_RECHNUNG`), nur bei Regelbesteuerung | später von Leistung und Rechnung | nein | §15 Abs. 1 Nr. 1 UStG | ungeprüft |
| `PAYOUT_NICHT_STEUERBAR` | Payout, Einstellung `NICHT_STEUERBAR` (Startwert) | sonstige Leistung, **Ausland** | – | 45 (`NICHT_STEUERBAR_UEBRIGE`) | Vereinnahmung | **nein** | O-04 | **offen** |
| `PAYOUT_STEUERBAR` | Payout, Einstellung `STEUERBAR` | sonstige Leistung, Inland | Leistender | KU: keine; Regel: 81 | Vereinnahmung | **ja** | O-04 | **offen** |
| `PAYOUT_KEIN_LEISTUNGSAUSTAUSCH` | Payout, Einstellung `KEIN_LEISTUNGSAUSTAUSCH` | – | – | keine | Vereinnahmung | nein | O-04 | **offen** |
| `AUSGANG_INLAND_REGELSATZ` | Verkäufe / sonstige Einnahmen Inland (Onlinehandel) | Lieferung, Inland | Leistender | KU: keine (steuerfrei §19); Regel: 81 | Vereinnahmung (Ist, §20) | ja | §1, §12 Abs. 1, §13 Abs. 1 Nr. 1 b, §19, §20 UStG | ungeprüft |

### Wirkung der Besteuerungsart
- **Kleinunternehmer:** Die §13b-Steuer wird geschuldet, §19 gilt nicht für ihre Berechnung (§13b Abs. 8 UStG).
  Es gibt **keinen Vorsteuerabzug** (Ausschluss folgt aus §19 UStG, siehe O-01). Zahllast = volle §13b-Steuer.
  Eigene Umsätze sind steuerfrei, erscheinen in keiner Kennzahl und zählen zum Gesamtumsatz nach §19.
- **Regelbesteuerung:** §13b-Steuer und Vorsteuer (Kz 67) sind gleich hoch, die Zahllast aus §13b ist 0.
- Maßgeblich ist die Besteuerungsart am **Entstehungsdatum** (Tabelle `besteuerungsart_historie`).

## Beispielrechnungen (= Abnahmefälle)

| Fall | Eingabe | Rechnung | Ergebnis | Test |
|---|---|---|---|---|
| 1 | KU, Challenge 99,00 USD, VAE, 03/2026, Kurs 1,0800 | 99 / 1,08 = 91,666… → **91,67**; × 19 % = 17,4173 → **17,42** | Kz 52 = 91,67, Kz 53 = 17,42, keine Kz 67, Zahllast **17,42** | `Fall1_*` |
| 2 | wie 1, Regelbesteuerung | wie 1 | Kz 52/53 = 91,67/17,42, Kz 67 = 17,42, Zahllast **0,00**; Formular Kz 52 = **91** | `Fall2_*` |
| 3 | KU, Abo 30,00 EUR, IE | 30,00 × 19 % = 5,70 | Kz 46/47 = 30,00/5,70, Zahllast **5,70** | `Fall3_*` |
| 4 | Refund 99 USD am 05.04.2026 zu Fall 1 | Originalkurs 1,08 | Q2/2026: BMG −91,67, Steuer −17,42; Variante Monatskurs 1,10 → −90,00/−17,10 | `Fall4_*` |
| 5 | Payout 2.000 USD, 05/2026, Kurs 1,08 → 1.851,85 € | – | nicht steuerbar: Kz 45 = 1.851,85, §19 +0 · steuerbar (KU): §19 +1.851,85, keine Kz · steuerbar (Regel, brutto): Kz 81 = 1.556,18, USt 295,67 | `Fall5_*` |
| 6 | Onlinehandel 60.000 € + Prop 25.000 € + Prop 16.000 € | kumuliert 85.000 € (≥ 80 %) → 101.000 € (> 100.000 €) | Warnung bei V2 und V3; Umschaltvorschlag ab 15.09.2026 (V3); V3 danach: Kz 81 = 13.445,38, USt 2.554,62 | `Fall6_*` |
| 7 | Änderung in festgeschriebenem Q1 | – | Exception; Storno nur in offenem Zeitraum, Protokoll mit Hash-Kette | `Fall7_*` |

## Offene und ungeprüfte Punkte

| ID | Thema | Hinweis / aktuelle Umsetzung | Status |
|----|-------|---------|--------|
| O-01 | Fundstelle Vorsteuerausschluss KU bei §13b | §13b Abs. 8 UStG schließt nur die Anwendung von §19 bei der Steuerberechnung aus. Der Ausschluss des Vorsteuerabzugs ergibt sich aus §19 UStG selbst (ab 2025 neu gefasst). Genaue Fundstelle prüfen. | offen |
| O-02 | §19-Grenzen ab 2025 | 25.000 € Vorjahr / 100.000 € lfd. Jahr; Wegfall ab dem Umsatz, mit dem 100.000 € überschritten werden. Gesamtumsatz = Summe `Umsatz19` nach Entstehungsdatum (bei Ist: Vereinnahmung). Warnschwelle 80 % als Parameter. | ungeprüft |
| O-03 | Voranmeldungspflicht KU bei §13b | Nur für Zeiträume mit Steuerschuld (§18 Abs. 4a UStG). VA-Zeitraum **vierteljährlich** (Einstellung). Dauerfristverlängerung wird nicht genutzt (`false`). | ungeprüft |
| O-04 | USt-Behandlung Payouts | Drei Optionen per Einstellung `ust.payout_behandlung`; Startwert `NICHT_STEUERBAR`. Leistungsort und Leistungsaustausch fachlich klären. | **offen** |
| O-05 | Kennzahlen USt-VA | Startwerte 2025/2026: 45, 46/47, 52/53, 66, 67, 81. Für jedes neue Jahr pflegen; fehlende Zuordnung → Fehler. | ungeprüft |
| O-06 | Erstattungen §17 UStG | Korrektur im Zeitraum des Erstattungseingangs; Kurs per `erstattung.kurs` = `ORIGINAL` (Start) oder `ERSTATTUNGSMONAT`; Teilerstattungen anteilig. | ungeprüft |
| O-07 | Entstehung §13b-Steuer vs. Ist-Versteuerung | Ist-Versteuerung (§20) gilt nur für eigene Umsätze. §13b Abs. 1: Ablauf VA-Zeitraum der Leistung; Abs. 2: Rechnungsausstellung, spätestens Ablauf Folgemonat; Vorauszahlung: mit Zahlung (Abs. 4 S. 2). Bestimmt USt-Zeitraum und Monatskurs; EÜR nach Zahlung (§11 EStG). | ungeprüft |
| O-08 | Formularrundung | BMG-Kennzahlen im Formular in vollen Euro (Start: abschneiden, alternativ kaufmännisch), Steuer centgenau. Interne Werte bleiben centgenau. | ungeprüft |
| O-09 | Onlinehandel: OSS / Fernverkäufe EU, B2C-Drittland | Laut Nutzer keine EU-B2C-Fernverkäufe → nicht umgesetzt. Bei Änderung neu bewerten. | geschlossen (nicht relevant) |
| O-10 | Brutto/Netto-Interpretation eigener Umsätze bei Regelbesteuerung | Einstellung `betrag_ist.<Belegtyp>` = `BRUTTO` (Start) oder `NETTO`. | ungeprüft |
| O-11 | Erstattung nach Wechsel der Besteuerungsart | Korrektur mit der Besteuerungsart des Ursprungsbelegs (inkl. Vorsteuer). Bei Wechsel KU ↔ Regel ggf. §15a/§17-Sonderfälle prüfen. | offen |

## Hinweise aus Phase 3 (Stammdaten)
- Vorbelegte Prop-Firmen (IQ Capital, Funded Next, 4Prop, LucidTrading, Tradifei) haben **kein Land**: Kategorie
  (Inland/EU/Drittland) ist bewusst nicht geraten. Maßgeblich ist der **Rechnungssteller laut Rechnung**
  (z. B. kann eine Marke mehrere Gesellschaften in verschiedenen Ländern nutzen). Ohne Kategorie sind Belege nicht buchbar.
- Kategorie wird aus dem Land abgeleitet: DE → Inland, EU-Mitglied → EU, sonst Drittland (GB seit 2021 Drittland);
  je Partner übersteuerbar. Nordirland (XI) ist nicht abgebildet.
- Storno: negiertes Steuerergebnis im USt-Zeitraum des Stornodatums (Korrektur einer Fehlbuchung).
  Fachliche Erstattungen laufen dagegen über Belegtyp „Erstattung“ (§17, O-06).
