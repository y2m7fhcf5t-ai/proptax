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

## Stack (angenommen, siehe docs/adr/0001-technologie-stack.md)
C# / .NET 10 LTS, Avalonia 12 (+ DataGrid), CommunityToolkit.Mvvm, Microsoft.Data.Sqlite.Core 11 + SQLite3 Multiple Ciphers + Dapper,
QuestPDF, ClosedXML, xUnit + coverlet.

## Konventionen
- Geldbeträge **ausschließlich `decimal`**, niemals `double`/`float`.
- Kaufmännische Rundung je Beleg: `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
- Keine hartcodierten Steuersätze/-regeln: Regel-, Satz-, Kennzahlen- und Grenzwerttabellen in der DB.
- Fachlich Unsicheres konfigurierbar machen, im Code mit `TODO-STEUER:` markieren und in
  `docs/steuerregeln.md` als offen führen.
- Engine-Projekt referenziert weder UI noch Persistenz.

## Struktur
- `src/PropTax.Domain` – Wertobjekte (`Money`, `CurrencyAmount`), Enums, `UstZeitraum`, `Laender`, GoBD (`Protokoll`, `Belegaenderung`)
- `src/PropTax.TaxEngine` – Steuerlogik, Regelwerk-Startwerte (JSON), nur Domain
- `src/PropTax.Persistence` – SQLite-Schema (`Schema/NNNN_*.sql`, eingebettet), Repositories, `Sitzung` (Unit of Work), DB-Protokoll
- `src/PropTax.Application` – Use-Case-Services; Einstieg `Arbeitsbereich.Oeffne(datenordner)`
- `src/PropTax.Reporting` – PDF (QuestPDF), XLSX (ClosedXML), ZIP, CSV; eigene DTOs, kennt nur Domain
- `src/PropTax.App` – Avalonia-UI (Views + ViewModels), kennt nur Application
- Tests: `TaxEngine.Tests` (Abnahme/ + Detail/, Gate ≥ 90 %), `Persistence.Tests` (Trigger, Schema),
  `Application.Tests` (Gate ≥ 80 % für Application+Persistence), `Reporting.Tests` (Gate ≥ 80 %),
  `App.Tests` (Headless-UI, xunit v3), `ArchitectureTests`
- Doku: `docs/architektur.md`, `docs/datenmodell.md`, `docs/steuerregeln.md`, `docs/adr/`, `docs/bilder/`
- NuGet-Versionen zentral in `Directory.Packages.props`

## Befehle
```bash
dotnet build
dotnet test
dotnet test tests/PropTax.TaxEngine.Tests        # inkl. Coverage-Report + Gate
dotnet run --project src/PropTax.App
dotnet run --project src/PropTax.App -- --selftest
PROPTAX_SCREENSHOT=/pfad/belege.png PROPTAX_SCREENSHOT_DIR=/pfad dotnet test tests/PropTax.App.Tests   # Screenshots
PROPTAX_PDF=/pfad/nachweis.pdf dotnet test tests/PropTax.Reporting.Tests   # Beispiel-PDF
dotnet publish src/PropTax.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
build/publish-windows.sh [Version]      # bzw. build/publish-windows.ps1 (inkl. Selbsttest)
```
Release: Tag `v1.2.3` pushen → CI baut die Exe, prüft Selbsttest/Einzeldatei und hängt sie an ein GitHub-Release.
CI: `.github/workflows/build.yml` (Ubuntu-Tests, Windows-Publish + Smoke-Test).

## Umgebung (Cloud-Container)
.NET-SDK via `apt-get install dotnet-sdk-10.0` (builds.dotnet.microsoft.com ist gesperrt, nuget.org erreichbar).

## Engine-Konventionen
- Einstiegspunkte: `Steuerermittlung.Ermittle`, `UstVaAggregation.Berechne`, `Kleinunternehmergrenze.Pruefe`,
  GoBD: `Belegaenderung`, `Protokoll` (Domain).
- Neue fachliche Regel = neuer Eintrag in Regeltabelle/JSON + Test + Zeile in `docs/steuerregeln.md`; kein `if` auf Länder/Firmen im Code.
- Fehlende Stammdaten (Kurs, Kennzahl, Grenzwert, Einstellung) → `RegelwerkException`, niemals stiller Default.
- Tests: Helfer `T.NeuesRegelwerk()` (Startwerte + Test-Kurse USD 03–05/2026).

## Repository
GitHub `y2m7fhcf5t-ai/proptax` (öffentlich), Branch `main`.

## Anwendungs-/UI-Konventionen
- Datenordner = eine Buchhaltung: `proptax.db` + `belege/<sha[0..2]>/<sha>.<ext>`; Standard `%LOCALAPPDATA%\PropTax`,
  gemerkt in `%APPDATA%\PropTax\datenordner.txt`.
- Schema-Änderungen nur als neues Skript `000N_*.sql` (nie ausgelieferte Skripte ändern).
- Protokoll-Schlüssel für Belege ist die DB-ID (`entitaet='beleg'`, `entitaet_id=<id>`).
- Fachliche Fehler als `FachlicherFehler` (UI zeigt sie in der Statusleiste); UI-Code ruft nie SQL.
- Storno = eigener Beleg mit `storno_von_id`, negiertem Betrag und negiertem Steuer-Snapshot im Zeitraum des Stornodatums;
  das Original bleibt unverändert (Status „storniert“ wird abgeleitet).

## Auswertungs-Konventionen
- Auswertungen nur aus gespeicherten Snapshots (`AuswertungService`), nie Neuberechnung.
- Fristen: `Fristen.VaFaelligkeit` + bundeseinheitliche `Feiertage`.
- PDF: keine Sonderzeichen außerhalb der Standardschriften verwenden (z. B. kein ⚠), sonst wirft QuestPDF.
- Dateinamen in Exporten nach Windows-Regeln bereinigen.

## Sicherheit & Sicherung
- Verschlüsselung per `PRAGMA rekey` (SQLite3MC); Passwort nur im Speicher (`Datenbank`), nie in DB/Protokoll/Dateien.
- Verbindungs-Pooling ist aktiv; vor Datei-Operationen `Datenbank.Freigeben()` / `Arbeitsbereich.VerbindungenSchliessen()`.
- Sicherung = ZIP (DB via Online-Backup-API, `belege/`, `manifest.json` mit SHA-256); Restore prüft vorher und legt
  eine Sicherheitskopie an. Auto-Sicherung beim Schließen (`backup.anzahl`).
- Jede `TODO-STEUER O-xx`-Markierung muss in `docs/steuerregeln.md` stehen (Test `OffeneSteuerpunkteTests`).

## Persistenz-Konventionen
- Geld in SQLite als INTEGER-Cent, Kurse/Sätze als TEXT-Dezimalstring (Details: docs/datenmodell.md).
- Engine-Ergebnis als Snapshot in `beleg_steuer` (Regel-ID, Satz, Kurs, Kennzahl, Engine-Version).
- Protokoll append-only mit SHA-256-Hash-Kette.
