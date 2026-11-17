using Dapper;

namespace PropTax.Persistence;

public abstract class Repository(Sitzung s)
{
    protected Sitzung S { get; } = s;

    protected IEnumerable<T> Query<T>(string sql, object? p = null) => S.Verbindung.Query<T>(sql, p, S.Transaktion);
    protected T? Single<T>(string sql, object? p = null) => S.Verbindung.QuerySingleOrDefault<T>(sql, p, S.Transaktion);
    protected T Scalar<T>(string sql, object? p = null) => S.Verbindung.ExecuteScalar<T>(sql, p, S.Transaktion)!;
    protected int Exec(string sql, object? p = null) => S.Verbindung.Execute(sql, p, S.Transaktion);
    protected long Insert(string sql, object p) => S.Verbindung.ExecuteScalar<long>(sql + "; SELECT last_insert_rowid();", p, S.Transaktion);
}

public sealed class StammdatenRepository(Sitzung s) : Repository(s)
{
    public long UnternehmenId() => Scalar<long>("SELECT id FROM unternehmen ORDER BY id LIMIT 1");
    public string UnternehmenName() => Scalar<string>("SELECT name FROM unternehmen ORDER BY id LIMIT 1");
    public void SetzeUnternehmenName(string name) => Exec("UPDATE unternehmen SET name=@name WHERE id=(SELECT MIN(id) FROM unternehmen)", new { name });

    public IReadOnlyList<EuerZeilenZeile> EuerZeilen(int jahr) =>
        Query<EuerZeilenZeile>("SELECT * FROM euer_zeilen_mapping WHERE jahr=@jahr ORDER BY euer_kategorie_id", new { jahr }).ToList();

    public void SetzeEuerZeile(int jahr, long kategorieId, string? zeile, string? bezeichnung)
    {
        if (string.IsNullOrWhiteSpace(zeile))
            Exec("DELETE FROM euer_zeilen_mapping WHERE jahr=@jahr AND euer_kategorie_id=@kategorieId", new { jahr, kategorieId });
        else
            Exec("""
                INSERT INTO euer_zeilen_mapping (jahr, euer_kategorie_id, zeile, bezeichnung) VALUES (@jahr, @kategorieId, @zeile, @bezeichnung)
                ON CONFLICT(jahr, euer_kategorie_id) DO UPDATE SET zeile=@zeile, bezeichnung=@bezeichnung, status='Ungeprueft'
                """, new { jahr, kategorieId, zeile = zeile.Trim(), bezeichnung });
    }

    public IReadOnlyList<BetriebZeile> Betriebe() => Query<BetriebZeile>("SELECT * FROM betrieb ORDER BY name").ToList();
    public BetriebZeile? Betrieb(long id) => Single<BetriebZeile>("SELECT * FROM betrieb WHERE id=@id", new { id });

    public long SpeichereBetrieb(BetriebZeile b)
    {
        if (b.Id == 0)
            return Insert("INSERT INTO betrieb (unternehmen_id, name, kurzzeichen, aktiv) VALUES (@UnternehmenId, @Name, @Kurzzeichen, @Aktiv)", b);
        Exec("UPDATE betrieb SET name=@Name, kurzzeichen=@Kurzzeichen, aktiv=@Aktiv WHERE id=@Id", b);
        return b.Id;
    }

    public IReadOnlyList<PartnerZeile> Partner() => Query<PartnerZeile>("SELECT * FROM geschaeftspartner ORDER BY name").ToList();
    public PartnerZeile? Partner(long id) => Single<PartnerZeile>("SELECT * FROM geschaeftspartner WHERE id=@id", new { id });
    public PartnerZeile? PartnerNachName(string name) =>
        Single<PartnerZeile>("SELECT * FROM geschaeftspartner WHERE name = @name COLLATE NOCASE", new { name = name.Trim() });

    public long SpeicherePartner(PartnerZeile p)
    {
        if (p.Id == 0)
            return Insert("""
                INSERT INTO geschaeftspartner (name, land_iso, kategorie, ust_idnr, tax_id, std_belegtyp, ist_prop_firma, aktiv, notiz)
                VALUES (@Name, @LandIso, @Kategorie, @UstIdnr, @TaxId, @StdBelegtyp, @IstPropFirma, @Aktiv, @Notiz)
                """, p);
        Exec("""
            UPDATE geschaeftspartner SET name=@Name, land_iso=@LandIso, kategorie=@Kategorie, ust_idnr=@UstIdnr, tax_id=@TaxId,
              std_belegtyp=@StdBelegtyp, ist_prop_firma=@IstPropFirma, aktiv=@Aktiv, notiz=@Notiz WHERE id=@Id
            """, p);
        return p.Id;
    }

    public IReadOnlyList<EuerKategorieZeile> EuerKategorien() => Query<EuerKategorieZeile>("SELECT * FROM euer_kategorie ORDER BY art, bezeichnung").ToList();

    public long SpeichereEuerKategorie(EuerKategorieZeile k) =>
        Insert("INSERT INTO euer_kategorie (code, bezeichnung, art) VALUES (@Code, @Bezeichnung, @Art)", k);

    public IReadOnlyList<VorlageZeile> Vorlagen() => Query<VorlageZeile>("SELECT * FROM beleg_vorlage ORDER BY name").ToList();

    public long SpeichereVorlage(VorlageZeile v)
    {
        if (v.Id == 0)
            return Insert("""
                INSERT INTO beleg_vorlage (name, betrieb_id, partner_id, belegtyp, richtung, betrag_fw_cent, waehrung, euer_kategorie_id)
                VALUES (@Name, @BetriebId, @PartnerId, @Belegtyp, @Richtung, @BetragFwCent, @Waehrung, @EuerKategorieId)
                """, v);
        Exec("""
            UPDATE beleg_vorlage SET name=@Name, betrieb_id=@BetriebId, partner_id=@PartnerId, belegtyp=@Belegtyp, richtung=@Richtung,
              betrag_fw_cent=@BetragFwCent, waehrung=@Waehrung, euer_kategorie_id=@EuerKategorieId WHERE id=@Id
            """, v);
        return v.Id;
    }

    public IReadOnlyList<ProfilZeile> Profile() => Query<ProfilZeile>("SELECT * FROM csv_mapping_profil ORDER BY name").ToList();
    public ProfilZeile? Profil(string name) => Single<ProfilZeile>("SELECT * FROM csv_mapping_profil WHERE name=@name", new { name });

    public long SpeichereProfil(string name, string json)
    {
        var vorhanden = Profil(name);
        if (vorhanden is null)
            return Insert("INSERT INTO csv_mapping_profil (name, profil_json) VALUES (@name, @json)", new { name, json });
        Exec("UPDATE csv_mapping_profil SET profil_json=@json WHERE id=@id", new { json, id = vorhanden.Id });
        return vorhanden.Id;
    }
}

public sealed class BelegRepository(Sitzung s) : Repository(s)
{
    public long Einfuegen(BelegZeile b) => Insert("""
        INSERT INTO beleg (belegnummer, betrieb_id, partner_id, belegtyp, richtung, rechnungsdatum, leistungsdatum, zahlungsdatum,
          betrag_fw_cent, waehrung, ausgewiesene_ust_cent, bankkurs, euer_kategorie_id, euer_periode, status, storno_von_id,
          korrektur_von_id, bezug_beleg_id, import_lauf_id, dubletten_schluessel, notiz, angelegt_am)
        VALUES (@Belegnummer, @BetriebId, @PartnerId, @Belegtyp, @Richtung, @Rechnungsdatum, @Leistungsdatum, @Zahlungsdatum,
          @BetragFwCent, @Waehrung, @AusgewieseneUstCent, @Bankkurs, @EuerKategorieId, @EuerPeriode, @Status, @StornoVonId,
          @KorrekturVonId, @BezugBelegId, @ImportLaufId, @DublettenSchluessel, @Notiz, @AngelegtAm)
        """, b);

    public void Aktualisieren(BelegZeile b) => Exec("""
        UPDATE beleg SET belegnummer=@Belegnummer, betrieb_id=@BetriebId, partner_id=@PartnerId, belegtyp=@Belegtyp, richtung=@Richtung,
          rechnungsdatum=@Rechnungsdatum, leistungsdatum=@Leistungsdatum, zahlungsdatum=@Zahlungsdatum, betrag_fw_cent=@BetragFwCent,
          waehrung=@Waehrung, ausgewiesene_ust_cent=@AusgewieseneUstCent, bankkurs=@Bankkurs, euer_kategorie_id=@EuerKategorieId,
          euer_periode=@EuerPeriode, status=@Status, bezug_beleg_id=@BezugBelegId, dubletten_schluessel=@DublettenSchluessel, notiz=@Notiz
        WHERE id=@Id
        """, b);

    public void Loeschen(long id) => Exec("DELETE FROM beleg WHERE id=@id", new { id });

    public BelegZeile? Laden(long id) => Single<BelegZeile>("SELECT * FROM beleg WHERE id=@id", new { id });
    public BelegZeile? NachNummer(string nr) => Single<BelegZeile>("SELECT * FROM beleg WHERE belegnummer=@nr", new { nr });
    public bool NummerVergeben(string nr) => Scalar<long>("SELECT COUNT(*) FROM beleg WHERE belegnummer=@nr", new { nr }) > 0;

    public IReadOnlyList<BelegZeile> Alle() => Query<BelegZeile>("SELECT * FROM beleg ORDER BY leistungsdatum, id").ToList();
    public IReadOnlyList<BelegZeile> NachStatus(string status) =>
        Query<BelegZeile>("SELECT * FROM beleg WHERE status=@status ORDER BY leistungsdatum, id", new { status }).ToList();

    public IReadOnlyList<BelegZeile> Dubletten(string schluessel, long ausserId = 0) =>
        Query<BelegZeile>("SELECT * FROM beleg WHERE dubletten_schluessel=@schluessel AND id<>@ausserId", new { schluessel, ausserId }).ToList();

    public long? StornoZu(long id) => Single<long?>("SELECT id FROM beleg WHERE storno_von_id=@id", new { id });

    public void SpeichereSteuer(BelegSteuerZeile z)
    {
        Exec("DELETE FROM beleg_steuer WHERE beleg_id=@BelegId", z);
        Insert("""
            INSERT INTO beleg_steuer (beleg_id, regel_code, leistungsart, steuerbarkeit, leistungsort, steuerschuldner, besteuerungsart,
              kennzahl_schluessel, satz, kurs, entstehungsdatum, ust_periode, bmg_cent, steuer_cent, vorsteuer_cent, umsatz19_cent,
              kennzahlen_json, begruendung_json, ist_manuell, manuelle_begruendung, berechnet_am, engine_version)
            VALUES (@BelegId, @RegelCode, @Leistungsart, @Steuerbarkeit, @Leistungsort, @Steuerschuldner, @Besteuerungsart,
              @KennzahlSchluessel, @Satz, @Kurs, @Entstehungsdatum, @UstPeriode, @BmgCent, @SteuerCent, @VorsteuerCent, @Umsatz19Cent,
              @KennzahlenJson, @BegruendungJson, @IstManuell, @ManuelleBegruendung, @BerechnetAm, @EngineVersion)
            """, z);
    }

    public BelegSteuerZeile? Steuer(long belegId) => Single<BelegSteuerZeile>("SELECT * FROM beleg_steuer WHERE beleg_id=@belegId", new { belegId });
    public IReadOnlyList<BelegSteuerZeile> AlleSteuern() => Query<BelegSteuerZeile>("SELECT * FROM beleg_steuer ORDER BY beleg_id").ToList();
}

public sealed class PeriodenRepository(Sitzung s) : Repository(s)
{
    public const string Ust = "UST";
    public const string Euer = "EUER";

    public bool IstFestgeschrieben(string art, string schluessel) =>
        Scalar<long>("SELECT COUNT(*) FROM periode WHERE art=@art AND schluessel=@schluessel AND status='FESTGESCHRIEBEN'",
            new { art, schluessel }) > 0;

    public IReadOnlyList<PeriodeZeile> Alle() => Query<PeriodeZeile>("SELECT * FROM periode ORDER BY art, schluessel").ToList();

    public void Festschreiben(string art, string schluessel, string am, string von) => Exec("""
        INSERT INTO periode (art, schluessel, status, festgeschrieben_am, festgeschrieben_von)
        VALUES (@art, @schluessel, 'FESTGESCHRIEBEN', @am, @von)
        ON CONFLICT(art, schluessel) DO UPDATE SET status='FESTGESCHRIEBEN', festgeschrieben_am=@am, festgeschrieben_von=@von
        """, new { art, schluessel, am, von });
}

public sealed class DokumentRepository(Sitzung s) : Repository(s)
{
    public DokumentZeile? NachSha(string sha) => Single<DokumentZeile>("SELECT * FROM dokument WHERE sha256=@sha", new { sha });
    public DokumentZeile? Laden(long id) => Single<DokumentZeile>("SELECT * FROM dokument WHERE id=@id", new { id });
    public IReadOnlyList<DokumentZeile> Alle() => Query<DokumentZeile>("SELECT * FROM dokument ORDER BY importiert_am, id").ToList();

    public IReadOnlyList<DokumentZeile> Unzugeordnet() => Query<DokumentZeile>(
        "SELECT d.* FROM dokument d WHERE NOT EXISTS (SELECT 1 FROM beleg_dokument bd WHERE bd.dokument_id = d.id) ORDER BY d.id").ToList();

    public IReadOnlyList<DokumentZeile> ZuBeleg(long belegId) => Query<DokumentZeile>(
        "SELECT d.* FROM dokument d JOIN beleg_dokument bd ON bd.dokument_id = d.id WHERE bd.beleg_id=@belegId ORDER BY d.id", new { belegId }).ToList();

    public long Einfuegen(DokumentZeile d) => Insert("""
        INSERT INTO dokument (sha256, dateiname, relativer_pfad, groesse, mime, importiert_am)
        VALUES (@Sha256, @Dateiname, @RelativerPfad, @Groesse, @Mime, @ImportiertAm)
        """, d);

    public bool Verknuepfen(long belegId, long dokumentId) =>
        Exec("INSERT OR IGNORE INTO beleg_dokument (beleg_id, dokument_id) VALUES (@belegId, @dokumentId)", new { belegId, dokumentId }) > 0;
}

public sealed class ImportRepository(Sitzung s) : Repository(s)
{
    public long Einfuegen(ImportLaufZeile z) => Insert("""
        INSERT INTO import_lauf (art, profil_id, datei, datei_sha256, zeitpunkt, anzahl_ok, anzahl_dubletten, anzahl_fehler)
        VALUES (@Art, @ProfilId, @Datei, @DateiSha256, @Zeitpunkt, @AnzahlOk, @AnzahlDubletten, @AnzahlFehler)
        """, z);

    public void Abschliessen(long id, long ok, long dubletten, long fehler) =>
        Exec("UPDATE import_lauf SET anzahl_ok=@ok, anzahl_dubletten=@dubletten, anzahl_fehler=@fehler WHERE id=@id",
            new { id, ok, dubletten, fehler });

    public IReadOnlyList<ImportLaufZeile> NachDateiHash(string art, string sha) =>
        Query<ImportLaufZeile>("SELECT * FROM import_lauf WHERE art=@art AND datei_sha256=@sha", new { art, sha }).ToList();

    public IReadOnlyList<ImportLaufZeile> Alle() => Query<ImportLaufZeile>("SELECT * FROM import_lauf ORDER BY id DESC").ToList();
}
