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
C# / .NET 10 LTS, Avalonia 12, CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite / EF Core, optional SQLCipher),
QuestPDF, ClosedXML, xUnit + coverlet.

## Konventionen
- Geldbeträge **ausschließlich `decimal`**, niemals `double`/`float`.
- Kaufmännische Rundung je Beleg: `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
- Keine hartcodierten Steuersätze/-regeln: Regel-, Satz-, Kennzahlen- und Grenzwerttabellen in der DB.
- Fachlich Unsicheres konfigurierbar machen, im Code mit `TODO-STEUER:` markieren und in
  `docs/steuerregeln.md` als offen führen.
- Engine-Projekt referenziert weder UI noch Persistenz.

## Struktur
- `src/PropTax.Domain` – Entitäten, Wertobjekte (`Money`, `CurrencyAmount`), keine Abhängigkeiten
- `src/PropTax.TaxEngine` – Steuerlogik, nur Domain
- `src/PropTax.Persistence` – SQLite, Protokoll, Festschreibung
- `src/PropTax.Reporting` – PDF/XLSX/CSV/DATEV
- `src/PropTax.App` – Avalonia-UI
- `tests/PropTax.TaxEngine.Tests`, `tests/PropTax.ArchitectureTests` (Schichten + Gleitkomma-Verbot)
- Doku: `docs/architektur.md`, `docs/datenmodell.md`, `docs/steuerregeln.md`, `docs/adr/`
- NuGet-Versionen zentral in `Directory.Packages.props`

## Befehle
```bash
dotnet build
dotnet test
dotnet test --collect:"XPlat Code Coverage"
dotnet run --project src/PropTax.App
dotnet run --project src/PropTax.App -- --selftest
dotnet publish src/PropTax.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```
CI: `.github/workflows/build.yml` (Ubuntu-Tests, Windows-Publish + Smoke-Test).

## Umgebung (Cloud-Container)
.NET-SDK via `apt-get install dotnet-sdk-10.0` (builds.dotnet.microsoft.com ist gesperrt, nuget.org erreichbar).

## Persistenz-Konventionen
- Geld in SQLite als INTEGER-Cent, Kurse/Sätze als TEXT-Dezimalstring (Details: docs/datenmodell.md).
- Engine-Ergebnis als Snapshot in `beleg_steuer` (Regel-ID, Satz, Kurs, Kennzahl, Engine-Version).
- Protokoll append-only mit SHA-256-Hash-Kette.
