-- Schema v2: Startwerte für Auswertungen (nur ergänzen, falls nicht vorhanden).
INSERT INTO einstellung (schluessel, wert, ab)
SELECT 'ust.va_frist_tag', '10', '1900-01-01'
WHERE NOT EXISTS (SELECT 1 FROM einstellung WHERE schluessel = 'ust.va_frist_tag');

INSERT INTO einstellung (schluessel, wert, ab)
SELECT 'ust19.warnschwelle', '0.8', '1900-01-01'
WHERE NOT EXISTS (SELECT 1 FROM einstellung WHERE schluessel = 'ust19.warnschwelle');

-- TODO-STEUER O-12: Zahlungszeitpunkt der USt im EÜR (FAELLIGKEIT | FAELLIGKEIT_10_TAGE).
INSERT INTO einstellung (schluessel, wert, ab)
SELECT 'euer.ust_zahlung', 'FAELLIGKEIT_10_TAGE', '1900-01-01'
WHERE NOT EXISTS (SELECT 1 FROM einstellung WHERE schluessel = 'euer.ust_zahlung');
