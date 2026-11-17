# Steuerregeln

> Erfassungs- und Berechnungswerkzeug, **keine Steuerberatung**. Alle Regeln sind vor
> produktivem Einsatz fachlich zu validieren. Status: `validiert` | `ungeprüft` | `offen`.

## Vorlage je Regel
- **ID / Titel**
- **Rechtsgrundlage**
- **Logik** (Kurzbeschreibung, Konfigurationsschlüssel)
- **Beispielrechnung**
- **Testfall** (Abnahmefall-Nr. / Testname)
- **Status**

## Offene Punkte aus Phase 0

| ID | Thema | Hinweis | Status |
|----|-------|---------|--------|
| O-01 | Fundstelle Vorsteuerausschluss KU bei §13b | §13b Abs. 8 UStG regelt, dass §19 bei Berechnung der §13b-Steuer nicht anzuwenden ist. Der Ausschluss des Vorsteuerabzugs ergibt sich aus §19 UStG selbst (ab 2025 neu gefasst) – genaue Fundstelle prüfen. | offen |
| O-02 | §19-Grenzen ab 2025 | 25.000 € Vorjahr / 100.000 € lfd. Jahr; Wegfall ab dem Umsatz, mit dem 100.000 € überschritten werden. Werte je Jahr konfigurierbar. | ungeprüft |
| O-03 | Voranmeldungspflicht KU bei §13b | Nur für Zeiträume mit Steuerschuld (§18 Abs. 4a UStG); Auswirkung auf Fristen/Dauerfristverlängerung klären. | offen |
| O-04 | USt-Behandlung Payouts | nicht steuerbar / steuerbar / kein Leistungsaustausch – konfigurierbar. | offen |
| O-05 | Kennzahlen USt-VA | Startwerte Kz 46/47, 52/53, 67, 45 je Jahr pflegen. | ungeprüft |
| O-06 | Erstattungen §17 UStG | Korrektur im Erstattungszeitraum. | ungeprüft |

## Rechenprüfung Abnahmefall 1
99,00 USD / 1,0800 = 91,6666… → **91,67 EUR**; 91,67 × 19 % = 17,4173 → **17,42 EUR** ✔
