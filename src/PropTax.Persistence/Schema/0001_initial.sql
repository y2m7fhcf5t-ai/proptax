-- PropTax Schema v1. Geld: INTEGER in Cent. Kurse/Sätze: TEXT-Dezimalstring. Datum: TEXT ISO-8601.
-- Quelle der Wahrheit für das Datenmodell (siehe docs/datenmodell.md).

CREATE TABLE unternehmen (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL,
  steuernummer TEXT,
  ust_idnr TEXT
);

CREATE TABLE betrieb (
  id INTEGER PRIMARY KEY,
  unternehmen_id INTEGER NOT NULL REFERENCES unternehmen(id),
  name TEXT NOT NULL UNIQUE,
  kurzzeichen TEXT,
  aktiv INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE geschaeftspartner (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL UNIQUE COLLATE NOCASE,
  land_iso TEXT,
  kategorie TEXT CHECK (kategorie IN ('Inland','Eu','Drittland')),  -- NULL = ungeklärt
  ust_idnr TEXT,
  tax_id TEXT,
  std_belegtyp TEXT,
  ist_prop_firma INTEGER NOT NULL DEFAULT 0,
  aktiv INTEGER NOT NULL DEFAULT 1,
  notiz TEXT
);

CREATE TABLE euer_kategorie (
  id INTEGER PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  bezeichnung TEXT NOT NULL,
  art TEXT NOT NULL CHECK (art IN ('Einnahme','Ausgabe'))
);

CREATE TABLE euer_zeilen_mapping (
  id INTEGER PRIMARY KEY,
  jahr INTEGER NOT NULL,
  euer_kategorie_id INTEGER NOT NULL REFERENCES euer_kategorie(id),
  zeile TEXT NOT NULL,
  bezeichnung TEXT,
  status TEXT NOT NULL DEFAULT 'Ungeprueft',
  UNIQUE (jahr, euer_kategorie_id)
);

CREATE TABLE steuerregel (
  id INTEGER PRIMARY KEY,
  code TEXT NOT NULL UNIQUE,
  prioritaet INTEGER NOT NULL DEFAULT 0,
  belegtypen TEXT NOT NULL DEFAULT '[]',
  richtung TEXT,
  partner_kategorie TEXT,
  bedingung TEXT,
  gueltig_ab TEXT NOT NULL,
  gueltig_bis TEXT,
  leistungsart TEXT NOT NULL,
  steuerbarkeit TEXT NOT NULL,
  leistungsort TEXT NOT NULL,
  steuerschuldner TEXT NOT NULL,
  satz_schluessel TEXT,
  kennzahl_schluessel TEXT,
  entstehung TEXT NOT NULL,
  zaehlt_zu_19_umsatz INTEGER NOT NULL DEFAULT 0,
  rechtsgrundlage TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'Ungeprueft'
);

CREATE TABLE steuersatz (
  id INTEGER PRIMARY KEY,
  schluessel TEXT NOT NULL,
  satz TEXT NOT NULL,
  gueltig_ab TEXT NOT NULL,
  gueltig_bis TEXT,
  status TEXT NOT NULL DEFAULT 'Ungeprueft'
);

CREATE TABLE kennzahl_mapping (
  id INTEGER PRIMARY KEY,
  jahr INTEGER NOT NULL,
  schluessel TEXT NOT NULL,
  kz_bmg TEXT,
  kz_steuer TEXT,
  kz_vorsteuer TEXT,
  bezeichnung TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'Ungeprueft',
  UNIQUE (jahr, schluessel)
);

CREATE TABLE import_lauf (
  id INTEGER PRIMARY KEY,
  art TEXT NOT NULL,                      -- BELEGE | KURSE | DOKUMENTE
  profil_id INTEGER REFERENCES csv_mapping_profil(id),
  datei TEXT NOT NULL,
  datei_sha256 TEXT NOT NULL,
  zeitpunkt TEXT NOT NULL,
  anzahl_ok INTEGER NOT NULL DEFAULT 0,
  anzahl_dubletten INTEGER NOT NULL DEFAULT 0,
  anzahl_fehler INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE wechselkurs (
  id INTEGER PRIMARY KEY,
  waehrung TEXT NOT NULL,
  jahr INTEGER NOT NULL,
  monat INTEGER NOT NULL CHECK (monat BETWEEN 1 AND 12),
  kurs TEXT NOT NULL,
  quelle TEXT NOT NULL CHECK (quelle IN ('Bmf','Bank','Manuell')),
  import_lauf_id INTEGER REFERENCES import_lauf(id),
  UNIQUE (waehrung, jahr, monat, quelle)
);

CREATE TABLE grenzwert_19 (
  id INTEGER PRIMARY KEY,
  jahr INTEGER NOT NULL UNIQUE,
  grenze_vorjahr_cent INTEGER NOT NULL,
  grenze_lfd_jahr_cent INTEGER NOT NULL,
  rechtsgrundlage TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'Ungeprueft'
);

CREATE TABLE besteuerungsart_historie (
  id INTEGER PRIMARY KEY,
  gueltig_ab TEXT NOT NULL,
  art TEXT NOT NULL CHECK (art IN ('Kleinunternehmer','Regelbesteuerung')),
  begruendung TEXT NOT NULL DEFAULT ''
);

CREATE TABLE einstellung (
  id INTEGER PRIMARY KEY,
  schluessel TEXT NOT NULL,
  wert TEXT NOT NULL,
  ab TEXT NOT NULL
);

CREATE TABLE periode (
  id INTEGER PRIMARY KEY,
  art TEXT NOT NULL CHECK (art IN ('UST','EUER')),
  schluessel TEXT NOT NULL,               -- USt: '2026-Q1' / '2026-03' / '2026'; EÜR: '2026'
  status TEXT NOT NULL CHECK (status IN ('OFFEN','FESTGESCHRIEBEN')),
  festgeschrieben_am TEXT,
  festgeschrieben_von TEXT,
  UNIQUE (art, schluessel)
);

CREATE TABLE csv_mapping_profil (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  profil_json TEXT NOT NULL
);

CREATE TABLE beleg_vorlage (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  betrieb_id INTEGER REFERENCES betrieb(id),
  partner_id INTEGER REFERENCES geschaeftspartner(id),
  belegtyp TEXT NOT NULL,
  richtung TEXT NOT NULL,
  betrag_fw_cent INTEGER,
  waehrung TEXT,
  euer_kategorie_id INTEGER REFERENCES euer_kategorie(id)
);

CREATE TABLE beleg (
  id INTEGER PRIMARY KEY,
  belegnummer TEXT NOT NULL UNIQUE,
  betrieb_id INTEGER NOT NULL REFERENCES betrieb(id),
  partner_id INTEGER NOT NULL REFERENCES geschaeftspartner(id),
  belegtyp TEXT NOT NULL,
  richtung TEXT NOT NULL CHECK (richtung IN ('Eingang','Ausgang')),
  rechnungsdatum TEXT,
  leistungsdatum TEXT NOT NULL,
  zahlungsdatum TEXT,
  betrag_fw_cent INTEGER NOT NULL,
  waehrung TEXT NOT NULL,
  ausgewiesene_ust_cent INTEGER,
  bankkurs TEXT,
  euer_kategorie_id INTEGER REFERENCES euer_kategorie(id),
  euer_periode TEXT,                       -- Jahr des Zahlungsdatums (§11 EStG), NULL = unbezahlt
  status TEXT NOT NULL CHECK (status IN ('Entwurf','Gebucht')),
  storno_von_id INTEGER REFERENCES beleg(id),
  korrektur_von_id INTEGER REFERENCES beleg(id),
  bezug_beleg_id INTEGER REFERENCES beleg(id),
  import_lauf_id INTEGER REFERENCES import_lauf(id),
  dubletten_schluessel TEXT NOT NULL,
  notiz TEXT,
  angelegt_am TEXT NOT NULL
);
CREATE INDEX ix_beleg_dublette ON beleg(dubletten_schluessel);
CREATE INDEX ix_beleg_status ON beleg(status);

CREATE TABLE beleg_steuer (
  id INTEGER PRIMARY KEY,
  beleg_id INTEGER NOT NULL UNIQUE REFERENCES beleg(id),
  regel_code TEXT NOT NULL,
  leistungsart TEXT NOT NULL,
  steuerbarkeit TEXT NOT NULL,
  leistungsort TEXT NOT NULL,
  steuerschuldner TEXT NOT NULL,
  besteuerungsart TEXT NOT NULL,
  kennzahl_schluessel TEXT,
  satz TEXT,
  kurs TEXT,
  entstehungsdatum TEXT NOT NULL,
  ust_periode TEXT NOT NULL,
  bmg_cent INTEGER NOT NULL,
  steuer_cent INTEGER NOT NULL,
  vorsteuer_cent INTEGER NOT NULL,
  umsatz19_cent INTEGER NOT NULL,
  kennzahlen_json TEXT NOT NULL,
  begruendung_json TEXT NOT NULL,
  ist_manuell INTEGER NOT NULL DEFAULT 0,
  manuelle_begruendung TEXT,
  berechnet_am TEXT NOT NULL,
  engine_version TEXT NOT NULL,
  CHECK (ist_manuell = 0 OR length(trim(coalesce(manuelle_begruendung, ''))) > 0)
);
CREATE INDEX ix_beleg_steuer_periode ON beleg_steuer(ust_periode);

CREATE TABLE dokument (
  id INTEGER PRIMARY KEY,
  sha256 TEXT NOT NULL UNIQUE,
  dateiname TEXT NOT NULL,
  relativer_pfad TEXT NOT NULL,
  groesse INTEGER NOT NULL,
  mime TEXT NOT NULL,
  importiert_am TEXT NOT NULL
);

CREATE TABLE beleg_dokument (
  beleg_id INTEGER NOT NULL REFERENCES beleg(id),
  dokument_id INTEGER NOT NULL REFERENCES dokument(id),
  PRIMARY KEY (beleg_id, dokument_id)
);

CREATE TABLE protokoll (
  id INTEGER PRIMARY KEY,
  nr INTEGER NOT NULL UNIQUE,
  zeitpunkt TEXT NOT NULL,
  benutzer TEXT NOT NULL,
  entitaet TEXT NOT NULL,
  entitaet_id TEXT NOT NULL,
  aktion TEXT NOT NULL,
  feld TEXT,
  alt TEXT,
  neu TEXT,
  begruendung TEXT NOT NULL,
  prev_hash TEXT NOT NULL,
  hash TEXT NOT NULL
);
CREATE INDEX ix_protokoll_entitaet ON protokoll(entitaet, entitaet_id);

-- ===== GoBD-Sperren =====
CREATE TRIGGER protokoll_kein_update BEFORE UPDATE ON protokoll
BEGIN SELECT RAISE(ABORT, 'GoBD: Protokolleinträge sind unveränderlich.'); END;
CREATE TRIGGER protokoll_kein_delete BEFORE DELETE ON protokoll
BEGIN SELECT RAISE(ABORT, 'GoBD: Protokolleinträge sind unveränderlich.'); END;

CREATE TRIGGER periode_festschreibung_endgueltig BEFORE UPDATE ON periode
WHEN OLD.status = 'FESTGESCHRIEBEN'
BEGIN SELECT RAISE(ABORT, 'GoBD: Festschreibung kann nicht aufgehoben werden.'); END;
CREATE TRIGGER periode_kein_delete BEFORE DELETE ON periode
WHEN OLD.status = 'FESTGESCHRIEBEN'
BEGIN SELECT RAISE(ABORT, 'GoBD: Festgeschriebene Perioden können nicht gelöscht werden.'); END;

CREATE TRIGGER beleg_gebucht_kein_delete BEFORE DELETE ON beleg
WHEN OLD.status = 'Gebucht'
BEGIN SELECT RAISE(ABORT, 'GoBD: Gebuchte Belege können nicht gelöscht werden (Storno verwenden).'); END;

CREATE TRIGGER beleg_festgeschrieben_kein_update BEFORE UPDATE ON beleg
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND (
       (p.art = 'EUER' AND p.schluessel = OLD.euer_periode)
    OR (p.art = 'UST' AND p.schluessel = (SELECT s.ust_periode FROM beleg_steuer s WHERE s.beleg_id = OLD.id))))
BEGIN SELECT RAISE(ABORT, 'GoBD: Beleg liegt in festgeschriebener Periode (nur Storno/Korrektur).'); END;

CREATE TRIGGER beleg_steuer_festgeschrieben_kein_update BEFORE UPDATE ON beleg_steuer
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND p.art = 'UST'
             AND p.schluessel IN (OLD.ust_periode, NEW.ust_periode))
BEGIN SELECT RAISE(ABORT, 'GoBD: Steuerergebnis liegt in festgeschriebener Periode.'); END;

CREATE TRIGGER beleg_steuer_festgeschrieben_kein_delete BEFORE DELETE ON beleg_steuer
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND p.art = 'UST' AND p.schluessel = OLD.ust_periode)
BEGIN SELECT RAISE(ABORT, 'GoBD: Steuerergebnis liegt in festgeschriebener Periode.'); END;

CREATE TRIGGER beleg_steuer_kein_insert_festgeschrieben BEFORE INSERT ON beleg_steuer
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND p.art = 'UST' AND p.schluessel = NEW.ust_periode)
BEGIN SELECT RAISE(ABORT, 'GoBD: Buchung in festgeschriebene Periode nicht zulässig.'); END;

CREATE TRIGGER beleg_kein_insert_festgeschrieben BEFORE INSERT ON beleg
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND p.art = 'EUER' AND p.schluessel = NEW.euer_periode)
BEGIN SELECT RAISE(ABORT, 'GoBD: Zahlungsjahr ist festgeschrieben.'); END;

CREATE TRIGGER beleg_kein_verschieben_in_festgeschrieben BEFORE UPDATE OF euer_periode ON beleg
WHEN EXISTS (SELECT 1 FROM periode p WHERE p.status = 'FESTGESCHRIEBEN' AND p.art = 'EUER' AND p.schluessel = NEW.euer_periode)
BEGIN SELECT RAISE(ABORT, 'GoBD: Zahlungsjahr ist festgeschrieben.'); END;
