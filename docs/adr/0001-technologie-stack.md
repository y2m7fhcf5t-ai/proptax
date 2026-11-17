# ADR 0001 – Technologie-Stack

- **Status:** angenommen (Freigabe 2026-11-17)
- **Datum:** 2026-11-17

## Kontext
Offline-Desktopanwendung für Windows 10/11 (EÜR, USt inkl. §13b/§19, GoBD-Protokoll).
Anforderungen: exakte Dezimal-Arithmetik, PDF/XLSX-Erzeugung, Single-Executable,
Steuer-Engine ohne UI-Abhängigkeit mit ≥ 90 % Testabdeckung, wartbar für eine Person,
SQLite (optional verschlüsselt), später ERiC-Anbindung.

## Verglichene Optionen

| Kriterium | A) Python 3.13 + PySide6 | **B) C# / .NET 10 LTS + Avalonia 11** | C) Tauri 2 + Rust + TypeScript |
|---|---|---|---|
| Dezimal | `decimal.Decimal` gut; Float-Mischung wirft TypeError, aber `Decimal(0.1)` möglich; keine Prüfung zur Compilezeit | `decimal` nativ (128 bit); keine implizite Konvertierung aus `double` → Compiler verhindert Float-Fehler | `rust_decimal` gut; **JS-Frontend kennt kein Decimal** → Beträge nur als Strings, Risiko an der IPC-Grenze |
| PDF | reportlab (gut, imperativ) | QuestPDF (sehr gut, deklarativ; Community-Lizenz frei < 1 Mio USD Jahresumsatz) | schwach (printpdf/genpdf) oder Typst einbetten |
| XLSX | openpyxl / xlsxwriter (sehr gut) | ClosedXML (MIT, gut) | rust_xlsxwriter (gut) |
| Single-Exe Windows | PyInstaller/Nuitka; onefile 80–150 MB, Start 2–6 s (Entpacken), häufig AV-Fehlalarme; Build nur unter Windows | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` auch aus Linux; ca. 60–100 MB, Start ~1 s; NativeAOT optional | 5–15 MB, Start < 1 s; benötigt WebView2 (Win11 vorinstalliert); Cross-Build aus Linux experimentell |
| SQLite + Verschlüsselung | sqlite3; SQLCipher unter Windows umständlich | Microsoft.Data.Sqlite + SQLitePCLRaw-SQLCipher-Bundle (etabliert) | rusqlite `bundled-sqlcipher` (gut) |
| Testbarkeit | pytest + coverage exzellent | xUnit + coverlet sehr gut; Avalonia.Headless für UI-Tests | cargo test gut; zwei Testwelten (Rust + TS) |
| ERiC (später) | ctypes möglich | P/Invoke, gut dokumentiert | FFI möglich |
| Wartbarkeit (1 Person) | sehr hoch (wenig Code); dynamische Typen = Risiko bei Geldlogik | hoch: eine Sprache, starke Typen, gute Refactoring-Tools | mittel: zwei Sprachen + IPC-Schicht |
| Entwicklung im Linux-Container | ja | ja (Avalonia läuft unter Linux; **WPF nicht**) | ja, Windows-Build separat |

Größen-/Startzeitangaben sind Erfahrungswerte, nicht gemessen; werden in Phase 5 verifiziert.

## Entscheidung (Empfehlung)
**B) C# / .NET 10 LTS + Avalonia.**

Nachtrag Phase 1: Avalonia **12.1.6** ist die aktuelle stabile Version und wird statt 11 verwendet.
Gemessen: `PropTax.App.exe` (win-x64, self-contained, komprimiert) ≈ 47 MB.

Geplante Projektaufteilung (Details Phase 1):
- `PropTax.Domain` – Entitäten, Wertobjekte (`Money`, `CurrencyAmount`), keine Abhängigkeiten
- `PropTax.TaxEngine` – Steuerlogik, reine Klassenbibliothek, regelgetrieben
- `PropTax.Persistence` – SQLite/EF Core, Protokoll, Festschreibung
- `PropTax.Reporting` – PDF (QuestPDF), XLSX (ClosedXML), CSV, DATEV
- `PropTax.App` – Avalonia-UI (MVVM, CommunityToolkit.Mvvm)
- `tests/*` – xUnit, coverlet

### Begründung
1. Dezimal-Sicherheit durch den Compiler (Kernrisiko einer Buchhaltungssoftware).
2. Bestes PDF-Tooling der drei Optionen; XLSX gleichwertig.
3. Single-File-Build für Windows aus Linux/CI reproduzierbar.
4. Engine als Klassenbibliothek ohne UI-Referenz → Schichtentrennung technisch erzwungen.
5. Eine Sprache für alle Schichten.

### Nachteile / Risiken
- Executable größer als bei Tauri (~60–100 MB).
- QuestPDF-Community-Lizenz nur bis 1 Mio USD Jahresumsatz kostenfrei.
- Avalonia statt WPF: kleineres Ökosystem, dafür plattformunabhängig entwickel- und testbar.

### Korrektur gegenüber Vorgabe
Vorgeschlagen war u. a. „.NET 8“. Dessen Support endete am **10.11.2026**. Daher .NET 10 LTS
(Support bis November 2028).

### Fallback
Option A (Python + PySide6), falls die spätere Wartung durch eine Person erfolgen soll,
die ausschließlich Python beherrscht.
