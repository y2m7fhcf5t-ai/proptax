# PropTax – Kurz-Handbuch

> **Wichtig:** PropTax ist ein Erfassungs- und Berechnungswerkzeug, **keine Steuerberatung**. Alle Regeln, Sätze,
> Grenzen, Kennzahlen und Zuordnungen sind Startwerte und vor produktivem Einsatz fachlich zu prüfen
> (siehe `docs/steuerregeln.md`, Spalte „Status“).

## 1. Start
- `PropTax.App.exe` herunterladen (GitHub › Actions › Artefakt bzw. Release) und starten – keine Installation.
- Windows SmartScreen kann beim ersten Start warnen (Datei ist nicht signiert): „Weitere Informationen“ › „Trotzdem ausführen“.
- Beim ersten Start wird der **Datenordner** `%LOCALAPPDATA%\PropTax` angelegt:
  - `proptax.db` – Datenbank (optional verschlüsselt)
  - `belege\` – abgelegte Belegdateien (Name = SHA-256-Prüfsumme)
  - `backup\` – Sicherungen
- Anderen Ordner verwenden: *Einstellungen › Datenordner wechseln…* (danach neu starten).

## 2. Ersteinrichtung (einmalig)
1. **Stammdaten › Geschäftspartner:** Für jede Prop-Firma (IQ Capital, Funded Next, 4Prop, LucidTrading, Tradifei …)
   das **Land des Rechnungsstellers laut Rechnung** eintragen (ISO-Code, z. B. `US`, `AE`, `GB`, `IE`).
   Daraus ergibt sich Inland / EU / Drittland. Ohne Land sind Belege nur als Entwurf speicherbar.
2. **Stammdaten › Betriebe:** „Prop-Trading“ und „Onlinehandel“ sind angelegt; weitere bei Bedarf.
3. **Kurse:** BMF-Umsatzsteuer-Umrechnungskurse je Monat eintragen oder als CSV importieren
   (`Währung;Monat;Kurs`, z. B. `USD;2026-03;1,0800`, oder Kopfzeile `Währung;2026-01;2026-02;…`).
   Notation: **1 EUR = x Fremdwährung**.
4. **Einstellungen:** Besteuerungsart (Kleinunternehmer/Regelbesteuerung mit Stichtag) und Payout-Behandlung
   (fachlich offen, O-04) prüfen.
5. **Auswertungen › EÜR:** Zeilen der Anlage EÜR je Kategorie pflegen (nicht vorbelegt).
6. Optional **Einstellungen › Verschlüsselung** aktivieren (siehe 8).

## 3. Belege erfassen
- **Belege › Neuer Beleg:** Partner, Typ, Daten (Rechnung/Leistung/Zahlung), Betrag + Währung. „Berechnen“ zeigt
  Steuerergebnis mit Begründung (Regel, Kurs, Kennzahlen). **Buchen** speichert den Steuer-Snapshot.
- **Vorlagen** (Stammdaten › Vorlagen) für wiederkehrende Challenge-Käufe.
- **Erstattung:** Typ „Erstattung“ + Ursprungsbeleg wählen → Korrektur im Erstattungszeitraum (§17).
- **Übersteuerung:** nur mit Begründung; wird protokolliert und in Auswertungen als „manuell“ markiert.
- **Mehrfachauswahl** in der Liste: Entwürfe gemeinsam buchen, EÜR-Kategorie setzen.
- **Ändern:** Entwürfe frei; gebuchte Belege nur mit Begründung und nur in offenen Perioden.
  Sonst **Stornieren** (Gegenbeleg im Stornodatum) bzw. Storno + Neuerfassung.
- Hinweise in der Liste: ⚠ mögliche Dublette, ⚠ Partner ohne Land, ✎ manuell, 📎 Belegdatei.

## 4. Importieren
- **Import › CSV-Belegimport:** Datei wählen → Format und Spalten werden erkannt → Standardwerte (Betrieb, Typ,
  Partner, Währung) setzen → Vorschau prüfen (✓ / ⚠ Dublette / ✗ Fehler) → „Als Entwürfe importieren“.
  Zuordnung als **Profil** speichern. Dieselbe Datei wird nicht doppelt importiert.
- Danach im Reiter Belege die Entwürfe markieren und **Entwürfe buchen**.
- **Import › PDF-Belege:** Ordner importieren → Vorschläge (Belegnummer im Dateinamen, Datum + Betrag) übernehmen
  oder manuell zuordnen. Dateien werden unveränderlich mit Prüfsumme abgelegt.

## 5. Perioden festschreiben
- **Perioden:** Nach Abgabe der Voranmeldung das Quartal festschreiben, nach Abschluss das EÜR-Jahr.
  Vorher alle Entwürfe buchen/löschen. Festschreibung ist **endgültig** (GoBD).

## 6. Auswertungen
- **USt-VA:** Kennzahlen (cent-genau und Formularwert), Belege je Kennzahl, Zahllast, Fälligkeit; Export XLSX/PDF.
- **Fristen & Zahllast:** je Quartal Fälligkeit (Wochenende/Feiertag verschoben), Voranmeldungspflicht
  (Kleinunternehmer nur bei §13b-Steuer), Zahllast.
- **§19-Monitor:** Gesamtumsatz aller Betriebe, Warnung bei 80 %/100 %, Umschaltvorschlag übernehmen;
  Vorjahresumsatz für frühere Jahre manuell eintragen.
- **EÜR:** je Betrieb oder gesamt nach Zahlungsdatum, Kursdifferenzen, USt-Zahllast als Ausgabe; Export XLSX/CSV/PDF.
- **Finanzamt-Nachweis:** alle Prop-Gebühren des Jahres als PDF, XLSX oder ZIP inkl. Belegdateien.
- **Dashboard:** Gebühren vs. Payouts, USt-Last und Netto je Prop-Firma und Monat.
- Belegliste und Änderungsprotokoll lassen sich ebenfalls exportieren (Reiter Belege bzw. Protokoll).

## 7. Datensicherung
- **Automatisch** beim Schließen: `backup\auto_<Datum>.zip`, die letzten 30 bleiben erhalten.
- **Manuell:** *Einstellungen › Jetzt sichern* (wird nie automatisch gelöscht). Zusätzlich empfohlen: Kopie auf
  externen Datenträger/Cloud.
- **Wiederherstellen:** Sicherung markieren (oder ZIP wählen), ggf. Passwort der Sicherung eingeben, Begründung,
  „wiederherstellen“. Vorher wird automatisch eine Sicherheitskopie (`vor_wiederherstellung_…`) angelegt;
  PropTax beendet sich danach – neu starten.
- Jede Sicherung enthält ein Manifest mit Prüfsummen; beschädigte oder manipulierte Sicherungen werden abgelehnt.

## 8. Verschlüsselung
- *Einstellungen › Verschlüsselung:* Passwort (mind. 10 Zeichen) zweimal eingeben → „Verschlüsseln“.
  Ab dann fragt PropTax beim Start nach dem Passwort (3 Versuche).
- **Ohne Passwort sind die Daten verloren** – es gibt keine Wiederherstellung. Passwort sicher aufbewahren.
- Verschlüsselt wird die **Datenbank**. Belegdateien (`belege\`) und das ZIP der Sicherung sind nicht zusätzlich
  verschlüsselt → für das Gerät BitLocker verwenden.
- Ältere, unverschlüsselte Sicherungen danach mit „Unverschlüsselte Sicherungen löschen“ entfernen.
- Passwortänderung gilt nur für die aktuelle Datenbank; ältere Sicherungen behalten ihr damaliges Passwort.

## 9. Protokoll (GoBD)
- Jede Anlage, Änderung, Buchung, Stornierung, Festschreibung, Sicherung und jeder Export wird mit Benutzer,
  Zeitpunkt, alt/neu und Begründung protokolliert. Die Einträge sind per SHA-256 verkettet; der Reiter
  *Protokoll* zeigt, ob die Kette intakt ist.

## 10. Grenzen
- Keine ELSTER-Übermittlung, keine ESt/GewSt-Berechnung, kein DATEV-Export, keine OSS-Verfahren.
- Feiertage nur bundeseinheitlich (keine Landesfeiertage).
- Offene steuerliche Punkte: `docs/steuerregeln.md` (O-01 … O-14).
