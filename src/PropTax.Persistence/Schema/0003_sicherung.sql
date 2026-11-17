-- Schema v3: Anzahl aufzubewahrender automatischer Sicherungen.
INSERT INTO einstellung (schluessel, wert, ab)
SELECT 'backup.anzahl', '30', '1900-01-01'
WHERE NOT EXISTS (SELECT 1 FROM einstellung WHERE schluessel = 'backup.anzahl');
