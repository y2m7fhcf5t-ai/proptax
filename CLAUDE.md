# CLAUDE.md – Lokale Buchhaltungs- & USt-Software für Prop-Trading

## Projektkontext
Einzelunternehmer, EÜR (§4 Abs. 3 EStG), Ist-Versteuerung, Kleinunternehmer (§19 UStG) mit
Umschaltung auf Regelbesteuerung per Stichtag. Mehrere Betriebe = ein USt-Unternehmen.
Offline-Windows-Desktop-App, lokale SQLite-DB, GoBD-orientiert. Keine Steuerberatung.

## Arbeitsweise
- Phasen 0–5; nach jeder Phase Zusammenfassung + offene Fragen, dann STOPP bis Freigabe.
- Vor jeder Phase Plan-Modus.
- Steuer-Engine test-first (Abnahmefälle 1–7), Abdeckung Engine ≥ 90 %.
- Ein Commit pro abgeschlossenem Teilschritt.

## Stack (vorläufig, siehe docs/adr/0001-technologie-stack.md)
C# / .NET 10 LTS, Avalonia 11, SQLite (Microsoft.Data.Sqlite / EF Core, optional SQLCipher),
QuestPDF, ClosedXML, xUnit + coverlet.

## Konventionen
- Geldbeträge **ausschließlich `decimal`**, niemals `double`/`float`.
- Kaufmännische Rundung je Beleg: `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
- Keine hartcodierten Steuersätze/-regeln: Regel-, Satz-, Kennzahlen- und Grenzwerttabellen in der DB.
- Fachlich Unsicheres konfigurierbar machen, im Code mit `TODO-STEUER:` markieren und in
  `docs/steuerregeln.md` als offen führen.
- Engine-Projekt referenziert weder UI noch Persistenz.

## Befehle
(folgen in Phase 1/2)
