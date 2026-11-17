# Architektur

## Schichtenmodell

```mermaid
flowchart TB
  App["PropTax.App<br/>Avalonia-UI, MVVM, ViewModels"]
  Anw["PropTax.Application<br/>Use-Case-Services, Arbeitsbereich"]
  Rep["PropTax.Reporting<br/>PDF (QuestPDF), XLSX (ClosedXML), CSV, DATEV-EXTF"]
  Per["PropTax.Persistence<br/>SQLite, Repositories, Protokoll, Festschreibung, Belegablage"]
  Eng["PropTax.TaxEngine<br/>Steuerermittlung, USt-VA-Aggregation, §19-Prüfung"]
  Dom["PropTax.Domain<br/>Entitäten, Wertobjekte (Money, CurrencyAmount), Enums"]
  App --> Anw
  Anw --> Per & Eng & Dom & Rep
  Rep --> Dom
  Per --> Eng & Dom
  Eng --> Dom
```

| Projekt | Darf referenzieren | Darf **nicht** referenzieren |
|---|---|---|
| Domain | – | alles andere |
| TaxEngine | Domain | Persistence, Reporting, App, Avalonia, EF Core, SQLite |
| Persistence | Domain, TaxEngine | Application, App, Reporting, Avalonia |
| Application | Domain, TaxEngine, Persistence, Reporting | App, Avalonia |
| Reporting | Domain (eigene Report-DTOs: `NachweisBericht`, `Tabelle`) | Application, Persistence, TaxEngine, App |
| App | Application (transitiv Domain/Engine-Typen) | Persistence, Microsoft.Data.Sqlite, Dapper |

Die Regeln werden durch `tests/PropTax.ArchitectureTests` erzwungen. Dort ist auch das Verbot von
`double`/`float` in Domain und Engine geprüft.

## Engine-Prinzipien
- **Rein funktional:** Eingabe Beleg + Regelkontext → Ausgabe `SteuerErgebnis`. Keine I/O.
- **Datengetrieben:** Regeln, Sätze, Kurse, Kennzahlen und Grenzwerte kommen über Schnittstellen
  (`IRegelwerk`; Implementierungen `InMemoryRegelwerk`, DB über `RegelwerkRepository.Laden`). Es gibt keine fachlichen Konstanten im Code.
- **Erklärbar:** Jedes Ergebnis enthält Regel-ID, Kurs, Satz, Kennzahl und eine Begründungskette
  für den Drill-down.
- **Übersteuerbar:** Manuelle Übersteuerung ist ein eigenes Eingabeobjekt mit Pflicht-Begründung.

## Datenfluss Beleg
1. Erfassung/Import → `beleg` (Status `entwurf`)
2. Engine berechnet → `beleg_steuer` (Snapshot), optional manuelle Übersteuerung
3. Buchen → Status `gebucht`, Periodenzuordnung (USt-Periode und EÜR-Periode getrennt)
4. Festschreibung der Periode → Belege unveränderlich; Änderungen nur per Storno-/Korrekturbeleg
5. Jede Änderung → `protokoll` (Hash-Kette)

## GoBD-Umsetzung (zweistufig)
1. **Anwendung:** `Belegaenderung` (Domain) + Services prüfen Periodenstatus und Begründungspflicht und protokollieren.
2. **Datenbank:** Trigger sperren UPDATE/DELETE an Protokoll, festgeschriebenen Perioden, Belegen und Steuer-Snapshots
   in festgeschriebenen Zeiträumen sowie das Verschieben hinein. Die Hash-Kette deckt Manipulationen auch
   bei entfernten Triggern auf (`Protokoll`-Reiter zeigt den Kettenstatus).

![Belegmaske](bilder/belege.png)

## Auswertungen (Phase 4)
`AuswertungService` liest ausschließlich die gespeicherten Steuer-Snapshots (keine Neuberechnung) und liefert
USt-VA mit Drill-down, Zahllast/Fristen, §19-Monitor, EÜR, Dashboard und Nachweis. `PropTax.Reporting` rendert
PDF (QuestPDF, Community-Lizenz), XLSX (ClosedXML) und ZIP. Jeder Export wird protokolliert.

![USt-VA](bilder/ustva.png)
![Dashboard](bilder/dashboard.png)

## Sicherheit und Datensicherung (Phase 5)
- **Verschlüsselung:** SQLite3 Multiple Ciphers über `Microsoft.Data.Sqlite.Core` (`Password=` → `PRAGMA key`,
  Umschlüsseln per `PRAGMA rekey`). Startablauf: verschlüsselte DB erkannt (Datei-Header) → Passwortfenster →
  `Arbeitsbereich.Oeffne(..., passwort)`. Belegdateien bleiben unverschlüsselt (dokumentierte Grenze).
- **Sicherung:** `SicherungService` – konsistente Kopie per Online-Backup-API (behält Schlüssel), ZIP mit Manifest
  und SHA-256 je Datei; Prüfung vor Wiederherstellung; Sicherheitskopie; Protokolleintrag; Auto-Sicherung beim Schließen.

![Einstellungen](bilder/einstellungen.png)

## ERiC-Vorbereitung
Die USt-VA wird als abstraktes Modell `UstVaDatensatz` (Zeitraum, Kennzahl → Betrag) erzeugt.
Eine spätere Klasse `EricUebermittlung` in einem eigenen Projekt (`PropTax.Elster`) bindet die
ERiC-C-Bibliothek per P/Invoke an. Engine und UI bleiben dabei unverändert.

## Build & Auslieferung
- Entwicklung und Tests: Linux oder Windows (`dotnet test`)
- Windows-Executable: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` (ca. 59 MB inkl. SQLite3MC und QuestPDF; `IncludeAllContentForSelfExtract` bündelt die PDF-Schriften; CI prüft „genau eine Datei“)
- CI: `.github/workflows/build.yml` mit Tests auf Ubuntu, Publish und Smoke-Test (`--selftest`) auf `windows-latest`
