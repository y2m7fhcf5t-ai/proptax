# Architektur

## Schichtenmodell

```mermaid
flowchart TB
  App["PropTax.App<br/>Avalonia-UI, MVVM, ViewModels"]
  Rep["PropTax.Reporting<br/>PDF (QuestPDF), XLSX (ClosedXML), CSV, DATEV-EXTF"]
  Per["PropTax.Persistence<br/>SQLite, Repositories, Protokoll, Festschreibung, Belegablage"]
  Eng["PropTax.TaxEngine<br/>Steuerermittlung, USt-VA-Aggregation, §19-Prüfung"]
  Dom["PropTax.Domain<br/>Entitäten, Wertobjekte (Money, CurrencyAmount), Enums"]
  App --> Rep & Per & Eng & Dom
  Rep --> Dom
  Per --> Eng & Dom
  Eng --> Dom
```

| Projekt | Darf referenzieren | Darf **nicht** referenzieren |
|---|---|---|
| Domain | – | alles andere |
| TaxEngine | Domain | Persistence, Reporting, App, Avalonia, EF Core, SQLite |
| Persistence | Domain, TaxEngine (implementiert deren Schnittstellen) | App, Reporting |
| Reporting | Domain (bekommt fertige Auswertungsmodelle) | App, Persistence |
| App | alle | – |

Die Regeln werden durch `tests/PropTax.ArchitectureTests` erzwungen. Dort ist auch das Verbot von
`double`/`float` in Domain und Engine geprüft.

## Engine-Prinzipien
- **Rein funktional:** Eingabe Beleg + Regelkontext → Ausgabe `SteuerErgebnis`. Keine I/O.
- **Datengetrieben:** Regeln, Sätze, Kurse, Kennzahlen und Grenzwerte kommen über Schnittstellen
  (`IRegelQuelle` u. a., Phase 2). Es gibt keine fachlichen Konstanten im Code.
- **Erklärbar:** Jedes Ergebnis enthält Regel-ID, Kurs, Satz, Kennzahl und eine Begründungskette
  für den Drill-down.
- **Übersteuerbar:** Manuelle Übersteuerung ist ein eigenes Eingabeobjekt mit Pflicht-Begründung.

## Datenfluss Beleg
1. Erfassung/Import → `beleg` (Status `entwurf`)
2. Engine berechnet → `beleg_steuer` (Snapshot), optional manuelle Übersteuerung
3. Buchen → Status `gebucht`, Periodenzuordnung (USt-Periode und EÜR-Periode getrennt)
4. Festschreibung der Periode → Belege unveränderlich; Änderungen nur per Storno-/Korrekturbeleg
5. Jede Änderung → `protokoll` (Hash-Kette)

## ERiC-Vorbereitung
Die USt-VA wird als abstraktes Modell `UstVaDatensatz` (Zeitraum, Kennzahl → Betrag) erzeugt.
Eine spätere Klasse `EricUebermittlung` in einem eigenen Projekt (`PropTax.Elster`) bindet die
ERiC-C-Bibliothek per P/Invoke an. Engine und UI bleiben dabei unverändert.

## Build & Auslieferung
- Entwicklung und Tests: Linux oder Windows (`dotnet test`)
- Windows-Executable: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` (ca. 47 MB)
- CI: `.github/workflows/build.yml` mit Tests auf Ubuntu, Publish und Smoke-Test (`--selftest`) auf `windows-latest`
