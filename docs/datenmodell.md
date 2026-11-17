# Datenmodell (SQLite)

## Speicherkonventionen
| Art | SQLite-Typ | Beispiel | Begründung |
|---|---|---|---|
| Geldbetrag (EUR oder Fremdwährung) | `INTEGER` in Cent / kleinster Einheit | `9167` = 91,67 | exakt, in SQL summierbar |
| Kurs, Steuersatz | `TEXT`, Dezimalstring mit Punkt | `"1.0800"`, `"0.19"` | volle Präzision, keine Gleitkommazahl |
| Datum | `TEXT` ISO-8601 | `"2026-03-15"` | sortierbar |
| Zeitstempel | `TEXT` ISO-8601 UTC | `"2026-03-15T10:22:01Z"` | Protokoll |
| Enum | `TEXT` | `"DRITTLAND"` | lesbar, migrationsfest |
| Hash | `TEXT` hex | SHA-256 | Beleg- und Protokollintegrität |

Alle Tabellen haben `id INTEGER PRIMARY KEY`. Fachliche Stammdaten haben `aktiv` und `angelegt_am`.

## ER-Diagramm

```mermaid
erDiagram
  UNTERNEHMEN ||--|{ BETRIEB : umfasst
  UNTERNEHMEN ||--o{ BESTEUERUNGSART_HISTORIE : "KU/Regel ab Stichtag"
  UNTERNEHMEN ||--o{ EINSTELLUNG : konfiguriert
  BETRIEB ||--o{ BELEG : erfasst
  GESCHAEFTSPARTNER ||--o{ BELEG : Partner
  EUER_KATEGORIE ||--o{ BELEG : "EÜR-Zuordnung"
  EUER_KATEGORIE ||--o{ EUER_ZEILEN_MAPPING : "je Jahr"
  BELEG ||--o| BELEG_STEUER : Steuerergebnis
  BELEG ||--o{ BELEG_DOKUMENT : verknüpft
  DOKUMENT ||--o{ BELEG_DOKUMENT : verknüpft
  BELEG |o--o{ BELEG : "storniert/korrigiert"
  IMPORT_LAUF |o--o{ BELEG : importiert
  CSV_MAPPING_PROFIL ||--o{ IMPORT_LAUF : nutzt
  BELEG_VORLAGE }o--o| GESCHAEFTSPARTNER : vorbelegt
  STEUERREGEL ||--o{ BELEG_STEUER : angewandt
  STEUERSATZ ||--o{ BELEG_STEUER : angewandt
  WECHSELKURS ||--o{ BELEG_STEUER : "USt-Kurs"
  KENNZAHL_MAPPING ||--o{ BELEG_STEUER : "Kz"
  PERIODE ||--o{ BELEG_STEUER : "USt-Periode"
  GRENZWERT_19 }o--|| UNTERNEHMEN : "je Jahr"
  PROTOKOLL }o--o| BELEG : "betrifft (generisch)"

  UNTERNEHMEN { int id PK  text name  text steuernummer  text ust_idnr }
  BETRIEB { int id PK  int unternehmen_id FK  text name  text kurzzeichen  int aktiv }
  GESCHAEFTSPARTNER { int id PK  text name  text land_iso  text kategorie  text ust_idnr  text tax_id  text std_steuerschluessel  int ist_prop_firma  int aktiv }
  BELEG { int id PK  text belegnummer  int betrieb_id FK  int partner_id FK  text belegtyp  text richtung  text rechnungsdatum  text leistungsdatum  text zahlungsdatum  int betrag_fw  text waehrung  text bankkurs  int betrag_eur_bank  int euer_kategorie_id FK  text status  int storno_von_id FK  int korrektur_von_id FK  int bezug_beleg_id FK  int import_lauf_id FK  text dubletten_schluessel  text notiz }
  BELEG_STEUER { int id PK  int beleg_id FK  int regel_id FK  text leistungsart  text leistungsort  text steuerbarkeit  text steuerschuldner  int satz_id FK  text satz  int kurs_id FK  text kurs  text besteuerungsart  text kennzahl_schluessel  text entstehungsdatum  int bmg_eur  int steuer_eur  int vorsteuer_eur  int umsatz19_eur  text ust_periode  text begruendung_kette  int kennzahl_mapping_id FK  int ist_manuell  text begruendung  text berechnet_am  text engine_version }
  STEUERREGEL { int id PK  text code  int prioritaet  text belegtypen  text richtung  text partner_kategorie  text bedingung  text gueltig_ab  text gueltig_bis  text leistungsart  text steuerbarkeit  text leistungsort  text steuerschuldner  text satz_schluessel  text kennzahl_schluessel  text entstehung  int zaehlt_zu_19_umsatz  text rechtsgrundlage  text status }
  STEUERSATZ { int id PK  text schluessel  text satz  text gueltig_ab  text gueltig_bis  text status }
  KENNZAHL_MAPPING { int id PK  int jahr  text schluessel  text kz_bmg  text kz_steuer  text kz_vorsteuer  text bezeichnung  text status }
  WECHSELKURS { int id PK  text waehrung  text monat  text kurs  text quelle  int import_lauf_id  text status }
  GRENZWERT_19 { int id PK  int jahr  int grenze_vorjahr_eur  int grenze_lfd_jahr_eur  text rechtsgrundlage  text status }
  BESTEUERUNGSART_HISTORIE { int id PK  int unternehmen_id FK  text gueltig_ab  text art  text begruendung }
  EINSTELLUNG { int id PK  int unternehmen_id FK  text schluessel  text wert  text gueltig_ab }
  PERIODE { int id PK  int jahr  int monat  text art  text status  text festgeschrieben_am  text festgeschrieben_von }
  DOKUMENT { int id PK  text sha256 UK  text dateiname  text relativer_pfad  int groesse  text mime  text importiert_am }
  BELEG_DOKUMENT { int beleg_id FK  int dokument_id FK }
  IMPORT_LAUF { int id PK  int profil_id FK  text datei  text datei_sha256  text zeitpunkt  int anzahl_ok  int anzahl_dubletten  int anzahl_fehler }
  CSV_MAPPING_PROFIL { int id PK  text name  text trennzeichen  text dezimalzeichen  text datumsformat  text spalten_json }
  BELEG_VORLAGE { int id PK  text name  int partner_id FK  text belegtyp  int betrag_fw  text waehrung  int euer_kategorie_id }
  EUER_KATEGORIE { int id PK  text code  text bezeichnung  text art }
  EUER_ZEILEN_MAPPING { int id PK  int jahr  int euer_kategorie_id FK  text zeile  text bezeichnung  text status }
  PROTOKOLL { int id PK  text zeitpunkt  text benutzer  text entitaet  int entitaet_id  text aktion  text feld  text alt  text neu  text begruendung  text prev_hash  text hash }
```

## Tabellen – Erläuterungen

### Stammdaten
- **unternehmen**: genau ein Datensatz, das umsatzsteuerliche Unternehmen. Die §19-Prüfung läuft auf dieser Ebene.
- **betrieb**: z. B. „Prop-Trading“, „Onlinehandel“. Die EÜR wird je Betrieb geführt.
- **geschaeftspartner**: `kategorie` ∈ `INLAND | EU | DRITTLAND`, abgeleitet aus `land_iso`, aber übersteuerbar.
  `std_steuerschluessel` belegt die Regelwahl vor. Prop-Firmen kommen als Seed-Daten mit `ist_prop_firma = 1`.
- **belegvorlage**: für wiederkehrende Belege (z. B. > 250 gleichartige Challenge-Rechnungen).

### Belege
- **beleg.belegtyp**: `ChallengeGebuehr | Reset | Activation | FundedGebuehr | AboSoftware | Payout | Erstattung | Verkauf | SonstigeAusgabe | SonstigeEinnahme` (Enum-Namen wie im Code)
- **beleg.richtung**: `EINGANG` (Leistungsbezug) | `AUSGANG` (eigene Leistung/Einnahme)
- **Drei Daten:** `rechnungsdatum`, `leistungsdatum` und `zahlungsdatum`. Die EÜR nutzt das Zahlungsdatum (§11 EStG),
  die USt-Periode wird über `steuerregel.entstehung` bestimmt (siehe `docs/steuerregeln.md`, O-07).
- **status**: `Entwurf | Gebucht`. „Storniert“ wird abgeleitet (es existiert ein Beleg mit `storno_von_id` = id),
  damit das Original auch in festgeschriebenen Perioden unverändert bleibt.
- **storno_von_id / korrektur_von_id**: Ein Storno ist ein Gegenbeleg mit negativen Beträgen, eine Korrektur ein neuer Beleg.
- **euer_periode**: Jahr des Zahlungsdatums (§11 EStG), NULL = noch nicht bezahlt.
- **bezug_beleg_id**: Eine Erstattung verweist auf die ursprüngliche Gebühr (§17 UStG).
- **dubletten_schluessel**: SHA-256 über normalisierte Felder (Partner, Datum, Betrag, Währung, Belegnummer).
  Er ist nicht `UNIQUE`, Dubletten werden beim Import gewarnt.
- **bankkurs / betrag_eur_bank**: optional für die EÜR mit tatsächlichem Kurs. Die Kursdifferenz ergibt sich aus der
  Gegenüberstellung mit `beleg_steuer.bmg_eur`.

### Steuer
- **beleg_steuer**: Snapshot des Engine-Ergebnisses. Er speichert `satz` und `kurs` als Wert **und** als Referenz,
  damit spätere Stammdatenänderungen festgeschriebene Werte nicht verändern. `engine_version` dient der Nachvollziehbarkeit.
  Bei `ist_manuell = 1` ist `begruendung` Pflicht (CHECK-Constraint).
- **steuerregel**: Eine Regel trifft zu, wenn alle nicht-NULL-Match-Felder passen und das Datum im Gültigkeitszeitraum liegt.
  Die höchste `prioritaet` gewinnt, bei Gleichstand liefert die Engine einen Fehler (keine stille Auswahl).
  - `steuerbarkeit`: `STEUERBAR | NICHT_STEUERBAR | KEIN_LEISTUNGSAUSTAUSCH`
  - `steuerschuldner`: `LEISTENDER | EMPFAENGER_13B_ABS1 | EMPFAENGER_13B_ABS2_NR1`
  - `entstehung`: `LeistungVaZeitraum | RechnungSpaetestensFolgemonat | LeistungUndRechnung | Vereinnahmung`
  - `belegtypen`: JSON-Liste, leer = alle; `bedingung`: optional `einstellungsschluessel=wert` (z. B. Payout-Behandlung)
  - `zaehlt_zu_19_umsatz`: Beitrag zum Gesamtumsatz nach §19
- **kennzahl_mapping**: `schluessel` (z. B. `13B_ABS1_SONST_LEIST`) → Kz je Jahr. Startwerte sind als `UNGEPRUEFT` markiert.
- **wechselkurs**: `quelle` ∈ `BMF | BANK | MANUELL`. Pro (`waehrung`, `monat`, `quelle`) gibt es genau einen Kurs (UNIQUE).
  Die Notation lautet `1 EUR = kurs Fremdwährung`.
- **grenzwert_19**, **besteuerungsart_historie**: Die Grenzen werden je Jahr gepflegt, die Umschaltung erfolgt mit Stichtag.

### Perioden & GoBD
- **periode**: `art` ∈ `UST_MONAT | UST_QUARTAL | UST_JAHR | EUER_JAHR`, `status` ∈ `OFFEN | FESTGESCHRIEBEN`.
  Trigger verhindern `UPDATE`/`DELETE` an `beleg` und `beleg_steuer`, wenn die zugehörige Periode festgeschrieben ist.
- **protokoll**: nur `INSERT` (Trigger verbieten UPDATE/DELETE). Berechnung:
  `hash = SHA256(prev_hash || zeitpunkt || benutzer || entitaet || entitaet_id || aktion || feld || alt || neu || begruendung)`.
  Eine Integritätsprüfung über die gesamte Kette ist als Funktion vorgesehen (Phase 3).
- **dokument**: Ablage unter `belege/<sha256[0..2]>/<sha256>.<ext>`. Gleiche Dateien werden nur einmal gespeichert.

### Einstellungen (Schlüssel, Startwerte)
| Schlüssel | Startwert | Hinweis |
|---|---|---|
| `ust.voranmeldungszeitraum` | `Quartal` | laut Nutzer vierteljährlich |
| `ust.dauerfristverlaengerung` | `false` | wird laut Nutzer nicht genutzt; Funktion bleibt abschaltbar vorhanden |
| `ust.payout_behandlung` | `NICHT_STEUERBAR` | `NICHT_STEUERBAR \| STEUERBAR \| KEIN_LEISTUNGSAUSTAUSCH`, O-04 |
| `ust.formular_rundung` | `Abschneiden` | O-08 |
| `erstattung.kurs` | `ORIGINAL` | `ORIGINAL \| ERSTATTUNGSMONAT`, O-06 |
| `betrag_ist.<Belegtyp>` | `BRUTTO` | `BRUTTO \| NETTO`, O-10 |
| `euer.kurs_quelle` | `Bmf` | alternativ `Bank` |
| `ui.benutzer` | `Inhaber` | Name im Protokoll |

## Nachträge Phase 2
- Engine-Modelle (`Steuerregel`, `Steuersatz`, `Kennzahlzuordnung`, `Wechselkurs`, `Grenzwert19`, `Einstellung`)
  bilden die Tabellen 1:1 ab; die Persistenz implementiert in Phase 3 `IRegelwerk` gegen SQLite.
- Startwerte werden beim Anlegen einer neuen DB aus `regelwerk.startwerte.json` übernommen.
- Einstellungen haben einen Stichtag `ab`; maßgeblich ist der jüngste Wert mit `ab <= Datum`.

## Nachträge Phase 3
- **Quelle der Wahrheit** für Tabellen und Trigger ist `src/PropTax.Persistence/Schema/0001_initial.sql`;
  dieses Dokument erläutert es. Neue Versionen nur als weiteres Skript (`0002_…`).
- `periode.art` ∈ `UST | EUER`, `schluessel` = `2026-Q1` / `2026-03` / `2026` bzw. EÜR-Jahr.
- `beleg_steuer` speichert zusätzlich `kennzahlen_json` und `begruendung_json` (Drill-down ohne Neuberechnung).
- `import_lauf.art` ∈ `BELEGE | KURSE`; `datei_sha256` verhindert unbeabsichtigten Doppelimport.
- `csv_mapping_profil.profil_json` speichert das komplette Mapping (Trennzeichen, Formate, Spalten, Standardwerte).
- `geschaeftspartner.kategorie = NULL` bedeutet ungeklärt; solche Belege sind nicht buchbar.

## Nachträge Phase 4 (Schema v2)
- `0002_auswertungen.sql` ergänzt Einstellungen, falls nicht vorhanden: `ust.va_frist_tag` (10), `ust19.warnschwelle` (0.8),
  `euer.ust_zahlung` (`FAELLIGKEIT_10_TAGE`). Manueller Vorjahresumsatz: `ust19.vorjahresumsatz.<jahr>`.
- `euer_zeilen_mapping` wird vom Nutzer gepflegt (leer ausgeliefert).
