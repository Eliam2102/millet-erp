-- Generado por tools/aw-sandbox/extraer-esquema-completo.sh (solo estructura, sin datos). BD AW_FULL (CS_AS por defecto).
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'SYSADM') EXEC('CREATE SCHEMA SYSADM');
GO

CREATE TABLE SYSADM.[AD_ATTACH] (
    [VORGANG] int NOT NULL,
    [PATH] nvarchar(200) NOT NULL,
    [NAME] nvarchar(200) NOT NULL,
    [KUNDE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_CACHE] (
    [TABLENAME] nvarchar(80) NOT NULL,
    [LASTCHANGED] datetime NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_OP] (
    [ID] int NOT NULL,
    [THEMA] nvarchar(80) NULL,
    [BEARBEITER] nvarchar(40) NULL,
    [ERF_DATE] datetime NOT NULL,
    [ERL_DATE] datetime NULL,
    [ERFASSER] nvarchar(40) NOT NULL,
    [VERSION] nvarchar(40) NULL,
    [PRIO] int NULL,
    [DETAILS] nvarchar(max) NULL,
    [SW] nvarchar(20) NOT NULL,
    [PROJEKT] int NOT NULL,
    [BIS] nvarchar(40) NULL,
    [BEISPIEL] nvarchar(max) NULL,
    [AUFWAND] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_PROJEKT] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(80) NULL,
    [KUNDE] int NOT NULL,
    [SOLL_ZEIT] int NOT NULL,
    [IST_ZEIT] int NOT NULL,
    [DATUM] datetime NULL,
    [CLOSED] int NOT NULL,
    [ERFASSER] nvarchar(80) NULL,
    [SW] nvarchar(20) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_SETTINGS] (
    [NUMMER] int NOT NULL,
    [SCRIPT_PATH] nvarchar(254) NOT NULL,
    [UPDATE_START_DATE] date NOT NULL,
    [SPRACH_VALUE] int NOT NULL,
    [ARCHIV] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_TERMIN] (
    [BEARBEITER] nvarchar(40) NOT NULL,
    [ZEIT] datetime NOT NULL,
    [TERMIN] nvarchar(254) NULL,
    [REFERENZ_TYPE] int NULL,
    [TASK_TYPE] int NULL,
    [REFERENZ] nvarchar(80) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[AD_UPDATE] (
    [SCRIPT] nvarchar(20) NOT NULL,
    [DATUM] date NOT NULL,
    [BENUTZER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [EXECUTED] datetime NULL,
    [EXECUTED_PATH] nvarchar(254) NULL,
    [BENUTZER_PC] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[AD_VORGANG] (
    [ID] int NOT NULL,
    [KUNDE] int NOT NULL,
    [TYP] nvarchar(40) NULL,
    [PROBLEM] nvarchar(max) NULL,
    [ERF_DATE] datetime NOT NULL,
    [ERL_DATE] datetime NULL,
    [ERFASSER] nvarchar(40) NOT NULL,
    [BEARBEITER] nvarchar(40) NULL,
    [PROJEKT] int NOT NULL,
    [SW] nvarchar(20) NOT NULL,
    [OP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_BEARB_WERTE] (
    [PRODUKT] int NOT NULL,
    [PARAM_NAME] nvarchar(40) NOT NULL,
    [WERT] decimal(28,8) NOT NULL,
    [TEXT] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_CEKAL_PRODUKT] (
    [PRODUKT] int NOT NULL,
    [EBENE] int NOT NULL,
    [TEXT_ALLG] int NOT NULL,
    [TEXT_ETIK] int NOT NULL,
    [TEXT_PROD] int NOT NULL,
    [TEXT_BIEGER] int NOT NULL,
    [BIEGER_NR] int NOT NULL,
    [BEM] nvarchar(40) NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [FARB_ID] int NOT NULL,
    [FARB_SPRACH_ID] int NOT NULL
);

CREATE TABLE SYSADM.[BA_CEKAL_RESTRICT] (
    [ID] nvarchar(10) NOT NULL,
    [BEM] nvarchar(200) NULL,
    [SPRACH_ID] int NOT NULL,
    [TEXT_ALLG] int NOT NULL,
    [TEXT_ETIK] int NOT NULL,
    [TEXT_PROD] int NOT NULL,
    [TEXT_BIEGER] int NOT NULL,
    [BIEGER_NR] int NOT NULL,
    [PARAM1] decimal(28,8) NULL,
    [PARAM2] decimal(28,8) NULL,
    [PARAM3] decimal(28,8) NULL,
    [PARAM4] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_CEKAL_RESULT] (
    [CLASS] nvarchar(40) NOT NULL,
    [S1] nvarchar(10) NOT NULL,
    [S2] nvarchar(10) NOT NULL,
    [S3] nvarchar(10) NOT NULL,
    [S4] nvarchar(10) NOT NULL,
    [S5] nvarchar(10) NOT NULL,
    [S6] nvarchar(10) NOT NULL,
    [S7] nvarchar(10) NOT NULL,
    [S8] nvarchar(10) NOT NULL,
    [S9] nvarchar(10) NOT NULL,
    [S10] nvarchar(10) NOT NULL,
    [RESULT] nvarchar(20) NOT NULL,
    [BEM] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [DIM_TABLE] int NOT NULL,
    [DIM_VALUE] int NOT NULL
);

CREATE TABLE SYSADM.[BA_CEKAL_TEXT] (
    [RESULT1] nvarchar(10) NOT NULL,
    [RESULT2] nvarchar(10) NOT NULL,
    [RESULT3] nvarchar(10) NOT NULL,
    [RESULT4] nvarchar(10) NOT NULL,
    [RESULT5] nvarchar(10) NOT NULL,
    [RESULT6] nvarchar(10) NOT NULL,
    [RESULT7] nvarchar(10) NOT NULL,
    [RESULT8] nvarchar(10) NOT NULL,
    [RESULT9] nvarchar(10) NOT NULL,
    [RESULT10] nvarchar(10) NOT NULL,
    [TEXT_ALLG] int NOT NULL,
    [TEXT_ETIK] int NOT NULL,
    [TEXT_PROD] int NOT NULL,
    [TEXT_BIEGER] int NOT NULL,
    [BIEGER_NR] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_CEKAL_ZUORD] (
    [PRODUKT] int NOT NULL,
    [CLASS] nvarchar(40) NOT NULL,
    [EBENE] int NOT NULL,
    [WERT] nvarchar(10) NOT NULL,
    [BEM] nvarchar(40) NULL,
    [SPRACH_ID] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL,
    [FARB_ID] int NOT NULL,
    [FARB_SPRACH_ID] int NOT NULL
);

CREATE TABLE SYSADM.[BA_DOORART_MASTXT] (
    [SPRACHE] nvarchar(50) NOT NULL,
    [TXT_ID] nvarchar(100) NOT NULL,
    [UEBER_BEZ] nvarchar(100) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_DOORART_MAT] (
    [BA_PRODUKT] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [ID] nvarchar(100) NOT NULL,
    [CODE] nvarchar(50) NOT NULL,
    [TYP] nvarchar(1) NOT NULL,
    [GROESSE_X] decimal(28,8) NOT NULL,
    [GROESSE_Y] decimal(28,8) NOT NULL,
    [GLAS_REDUKTION] decimal(28,8) NOT NULL,
    [TI_ID] nvarchar(50) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_DOORART_MODELLE] (
    [MODELL_ID] nvarchar(50) NOT NULL,
    [BESCHREIBUNG] nvarchar(100) NULL,
    [TEXT] nvarchar(100) NULL,
    [ENTWURF] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_DOORART_TI] (
    [TI_ID] nvarchar(50) NOT NULL,
    [BESCHREIBUNG] nvarchar(100) NULL,
    [TYP] nvarchar(50) NOT NULL,
    [GROESSE_X] decimal(28,8) NOT NULL,
    [GROESSE_Y] decimal(28,8) NOT NULL,
    [BEZ_P_X] decimal(28,8) NOT NULL,
    [BEZ_P_Y] decimal(28,8) NOT NULL,
    [MESS_P_X] decimal(28,8) NOT NULL,
    [MESS_P_Y] decimal(28,8) NOT NULL,
    [MODELL] varbinary(max) NULL,
    [AUSSCHNITTE] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_EDGEQUALITY] (
    [PRODUKT] int NOT NULL,
    [MOD_NR] int NOT NULL,
    [EDGE_QUALITY1] decimal(28,8) NOT NULL,
    [EDGE_QUALITY2] decimal(28,8) NOT NULL,
    [EDGE_QUALITY3] decimal(28,8) NOT NULL,
    [EDGE_QUALITY4] decimal(28,8) NOT NULL,
    [EDGE_QUALITY5] decimal(28,8) NOT NULL,
    [EDGE_QUALITY6] decimal(28,8) NOT NULL,
    [EDGE_QUALITY7] decimal(28,8) NOT NULL,
    [EDGE_QUALITY8] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_EKKAL] (
    [BA_PRODUKT] int NOT NULL,
    [BA_VERBR3] decimal(28,8) NULL,
    [BA_VERBR4] decimal(28,8) NULL,
    [SPRACH_BASIS] int NOT NULL,
    [BA_PROD1] int NULL,
    [BA_VERBR5] decimal(28,8) NULL,
    [BA_ME1] nvarchar(20) NOT NULL,
    [BA_PROD2] int NULL,
    [BA_VERBR6] decimal(28,8) NULL,
    [BA_ME2] nvarchar(20) NOT NULL,
    [BA_PROD3] int NULL,
    [BA_VERBR7] decimal(28,8) NULL,
    [BA_ME3] nvarchar(20) NOT NULL,
    [BA_PROD4] int NULL,
    [BA_VERBR8] decimal(28,8) NULL,
    [BA_ME4] nvarchar(20) NOT NULL,
    [BA_PROD5] int NULL,
    [BA_VERBR9] decimal(28,8) NULL,
    [BA_ME5] nvarchar(20) NOT NULL,
    [BA_PROD6] int NULL,
    [BA_VERBR10] decimal(28,8) NULL,
    [BA_ME6] nvarchar(20) NOT NULL,
    [BA_PROD7] int NULL,
    [BA_VERBR11] decimal(28,8) NULL,
    [BA_ME7] nvarchar(20) NOT NULL,
    [BA_PROD8] int NULL,
    [BA_VERBR12] decimal(28,8) NULL,
    [BA_ME8] nvarchar(20) NOT NULL,
    [BA_PROD9] int NULL,
    [BA_ME9] nvarchar(20) NOT NULL,
    [BA_PROD10] int NULL,
    [BA_ME10] nvarchar(20) NOT NULL,
    [BA_PROD11] int NULL,
    [BA_ME11] nvarchar(20) NOT NULL,
    [BA_PROD12] int NULL,
    [BA_ME12] nvarchar(20) NOT NULL,
    [BA_REDUKTION] decimal(28,8) NOT NULL,
    [BA_VERBR_GAS] decimal(28,8) NULL,
    [BA_VERBR1] decimal(28,8) NULL,
    [BA_VERBR2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_EXCHANGE] (
    [ID] int NOT NULL,
    [DESCRIPTION] nvarchar(200) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_EXCHANGE_DETAIL] (
    [ID] int NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_FITTING_MACRO] (
    [ARTICLE] int NOT NULL,
    [AREA] nvarchar(40) NOT NULL,
    [MACRO] int NOT NULL,
    [DIN_LR] int NOT NULL,
    [NUMBER] int NOT NULL,
    [RANK] int NOT NULL,
    [BEA_PARAM1] int NULL,
    [BEA_PARAM2] int NULL,
    [BEA_PARAM3] int NULL,
    [BEA_PARAM4] int NULL,
    [BEA_PARAM5] int NULL,
    [BEA_PARAM6] int NULL,
    [BEA_PARAM7] int NULL,
    [BEA_PARAM8] int NULL,
    [BEA_PARAM9] int NULL,
    [BEA_PARAM10] int NULL,
    [BEA_PARAM11] int NULL,
    [BEA_PARAM12] int NULL,
    [BEA_PARAM13] int NULL,
    [BEA_PARAM14] int NULL,
    [BEA_PARAM15] int NULL,
    [BEA_PARAM16] int NULL,
    [BEA_PARAM17] int NULL,
    [BEA_PARAM18] int NULL,
    [BEA_PARAM19] int NULL,
    [BEA_PARAM20] int NULL,
    [BEA_PARAM21] int NULL,
    [BEA_PARAM22] int NULL,
    [BEA_PARAM23] int NULL,
    [BEA_PARAM24] int NULL,
    [BEA_PARAM25] int NULL,
    [BEA_PARAM26] int NULL,
    [BEA_PARAM27] int NULL,
    [BEA_PARAM28] int NULL,
    [BEA_PARAM29] int NULL,
    [BEA_PARAM30] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [REPLACMENT] int NULL
);

CREATE TABLE SYSADM.[BA_GESTELLARTEN] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [BEM] nvarchar(40) NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [LEER_GEWICHT] decimal(28,8) NOT NULL,
    [ANZ_SPANNLATTEN] int NOT NULL,
    [ANZ_RAEDER] int NOT NULL,
    [ANZ_WAGEN] int NOT NULL,
    [STATIONAER] int NOT NULL,
    [MAX_BREITE] decimal(28,8) NOT NULL,
    [MAX_HOEHE] decimal(28,8) NOT NULL,
    [MAX_GEWICHT] decimal(28,8) NOT NULL,
    [GESTELLWERT] decimal(28,8) NOT NULL,
    [OT_RACK_TYPE] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_GESTELLE] (
    [GESTELL_NR] nvarchar(20) NOT NULL,
    [GESTELLSCHEIN] int NULL,
    [GESTELLART] int NOT NULL,
    [LIEFERSCHEIN_NR] int NULL,
    [BEZ] nvarchar(40) NULL,
    [BEM] nvarchar(40) NULL,
    [DRUCKSTATUS] nvarchar(1) NULL,
    [KUNDEN_NR] int NULL,
    [FAHRER_NR] int NULL,
    [TOUR_NR] nvarchar(20) NULL,
    [AUSGABEDATUM] date NULL,
    [RUECKGABEDATUM] date NULL,
    [SPERRKZ] int NULL,
    [ANZ_SPANNLATTEN] int NOT NULL,
    [ANZ_RAEDER] int NOT NULL,
    [ANZ_WAGEN] int NOT NULL,
    [STATIONAER] int NOT NULL,
    [FREMDGESTELL] int NOT NULL,
    [VERLOREN] int NOT NULL,
    [MIETSATZ] decimal(28,8) NOT NULL,
    [TAGE_FREI] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LIEF_KZ] int NOT NULL,
    [BUCHTYP] int NOT NULL,
    [MITARB_ID] nvarchar(40) NULL,
    [AENDERUNG] date NULL,
    [AUTO_CREATE] int NOT NULL,
    [NICHT_MAHNBAR] int NOT NULL,
    [ABW_NAME] nvarchar(40) NULL,
    [ABW_STRASSE] nvarchar(40) NULL,
    [ABW_ORT] nvarchar(40) NULL,
    [VERSAND_INFO] nvarchar(40) NULL,
    [ABW_PLZ] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL,
    [WV_DATUM] date NULL,
    [WV_BEMERKUNG] nvarchar(100) NULL,
    [WV_MITARBEITER] nvarchar(40) NOT NULL,
    [ABHOL_DATUM] date NULL,
    [RACK_KIND] int NOT NULL
);

CREATE TABLE SYSADM.[BA_GESTELLE_AUFTR] (
    [ID] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GESTELL_NR] nvarchar(20) NULL,
    [GESTELLART] int NULL,
    [AUSGABE] date NULL,
    [KUNDEN_ID] int NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [AWRACK_OWNER] int NULL,
    [AWRACK_RACKTYPE] int NULL,
    [AWRACK_NUMBER] int NULL,
    [ROWID] char(36) NOT NULL,
    [RACK_KIND] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BARCODE] nvarchar(20) NOT NULL,
    [REG_POINT] int NOT NULL,
    [SEQUENCE] int NOT NULL,
    [RACK_STATUS] int NOT NULL
);

CREATE TABLE SYSADM.[BA_GESTELLE_BEST] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEST_ID] int NOT NULL,
    [AUSGABE] date NULL,
    [GESTELLART] int NULL,
    [GESTELL_NR] nvarchar(20) NULL,
    [LIEFERANTEN_ID] int NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [AWRACK_OWNER] int NULL,
    [AWRACK_RACKTYPE] int NULL,
    [AWRACK_NUMBER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_GESTELLE_EA] (
    [LFD_NR] int NOT NULL,
    [ID] int NULL,
    [GESTELL_NR] nvarchar(20) NULL,
    [GESTELLART] int NULL,
    [AUSGABE] date NULL,
    [RUECKGABE] date NULL,
    [ANZ_SPANNLATTEN] int NULL,
    [ANZ_RAEDER] int NULL,
    [ANZ_WAGEN] int NULL,
    [MAHNDATUM] date NULL,
    [BUCHTYP] int NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [BUCHDATUM] date NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_GESTELLE_EA_DETAIL] (
    [LFD_NR] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [KUNDEN_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_GUETE_RESTRICT] (
    [GLAS] int NOT NULL,
    [GAS] int NOT NULL,
    [PRIO] int NOT NULL,
    [LAND] nvarchar(6) NOT NULL,
    [ID] int NOT NULL,
    [REGEL_ID] int NOT NULL,
    [PARAM1] decimal(28,8) NULL,
    [PARAM2] decimal(28,8) NULL,
    [PARAM3] decimal(28,8) NULL,
    [PARAM4] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_GUETE_TEXT] (
    [GLAS] int NOT NULL,
    [GAS] int NOT NULL,
    [PRIO] int NOT NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TEXT] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_ISO_AUFBAU] (
    [KEYNR] int NOT NULL,
    [GLAS1] int NOT NULL,
    [GLAS2] int NOT NULL,
    [GLAS3] int NOT NULL,
    [SZR1] decimal(28,8) NOT NULL,
    [SZR2] decimal(28,8) NOT NULL,
    [GAS1] int NOT NULL,
    [GAS2] int NOT NULL,
    [KWERT] decimal(28,8) NULL,
    [GWERT] decimal(28,8) NULL,
    [RWERT] decimal(28,8) NULL,
    [ERFDATUM] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_KU_PRODUKTE] (
    [KUNDE] int NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [ID] nvarchar(40) NOT NULL,
    [BA_BEZ1] nvarchar(40) NULL,
    [BA_BEZ2] nvarchar(40) NULL,
    [BA_BEZ3] nvarchar(40) NULL,
    [BA_EAN] nvarchar(20) NULL,
    [BA_STD_TXT] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_LAGMA] (
    [BA_PRODUKT] int NOT NULL,
    [BA_INHALT] int NOT NULL,
    [BA_BEZ] nvarchar(40) NULL,
    [BA_KURZ_BEZ] nvarchar(20) NULL,
    [BA_ABW_WGR] nvarchar(3) NOT NULL,
    [BA_WGR_STAT] nvarchar(3) NOT NULL,
    [PRZ_SCHLS] int NOT NULL,
    [PRZ_SCHLS_EK] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [EK_KZ_BESTELL] int NULL,
    [LAG_KZ_LAGER] int NULL,
    [EXT_STAT] nvarchar(8) NULL,
    [BA_BREITE] decimal(28,8) NOT NULL,
    [BA_HOEHE] decimal(28,8) NOT NULL,
    [KZ_EK] int NULL,
    [ROWID] char(36) NOT NULL,
    [IQUOTE_RELEASE] int NOT NULL,
    [IQUOTE_IMAGE] nvarchar(254) NULL,
    [EAN] nvarchar(20) NULL,
    [TRANSFER_SAP] int NOT NULL,
    [SAP_PRODUCT_CODE] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BA_LENR] (
    [LENR] nvarchar(40) NOT NULL,
    [BEZ] nvarchar(200) NULL,
    [DATUM] datetime NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [AUTOCREATE] int NOT NULL
);

CREATE TABLE SYSADM.[BA_LENR_DETAIL] (
    [LENR] nvarchar(40) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [LINK] nvarchar(254) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_LENR_TI] (
    [LENR] nvarchar(40) NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_POS] (
    [ID] int NOT NULL,
    [ID_KUNDE] int NOT NULL,
    [NAME] nvarchar(60) NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] int NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] nvarchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_ZEIT_STK] int NOT NULL,
    [PROVISIONSSATZ2] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [PMGRP] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VERS_BIT] int NULL,
    [GRUPPE] int NOT NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(8) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL,
    [CHECKED] int NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(8) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MAKRO_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MASSZU] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [PREIS_RELEVANT] int NOT NULL,
    [REDUKTION] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_MASSZU_WERT] (
    [ID] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUGABE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_NUMKREISE] (
    [ID] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [ANGEBOT] int NULL,
    [AUFTRAG] int NULL,
    [LIEFERSCHEIN] int NULL,
    [RECHNUNG] int NULL,
    [GUTSCHRIFT] int NULL,
    [BESTELLUNG] int NULL,
    [BEST_ANFRAGE] int NULL,
    [LAUF_AWPOOL] int NULL,
    [ANGEBOT_BIS] int NULL,
    [AUFTRAG_BIS] int NULL,
    [LIEFERSCHEIN_BIS] int NULL,
    [RECHNUNG_BIS] int NULL,
    [GUTSCHRIFT_BIS] int NULL,
    [BESTELLUNG_BIS] int NULL,
    [BEST_ANFRAGE_BIS] int NULL,
    [LAUF_AWPOOL_BIS] int NULL,
    [TEILLIEF] int NULL,
    [TEILLIEF_BIS] int NULL,
    [REKLA] int NULL,
    [REKLA_BIS] int NULL,
    [LAUF_FIBU] int NULL,
    [PACKMITTEL] int NULL,
    [PACKMITTEL_BIS] int NULL,
    [ANGEBOT_VON] int NULL,
    [AUFTRAG_VON] int NULL,
    [LIEFERSCHEIN_VON] int NULL,
    [RECHNUNG_VON] int NULL,
    [GUTSCHRIFT_VON] int NULL,
    [BEST_ANFRAGE_VON] int NULL,
    [BESTELLUNG_VON] int NULL,
    [LAUF_AWPOOL_VON] int NULL,
    [TEILLIEF_VON] int NULL,
    [REKLA_VON] int NULL,
    [PACKMITTEL_VON] int NULL,
    [LAUF_FIBU_VON] int NULL,
    [LAUF_FIBU_BIS] int NULL,
    [AUFTRAG_ZEIT] int NULL,
    [ANGEBOT_ZEIT] int NULL,
    [BESTELLUNG_ZEIT] int NULL,
    [GUTSCHRIFT_ZEIT] int NULL,
    [BEST_ANF_ZEIT] int NULL,
    [SN_DATEI] int NULL,
    [SN_DATEI_VON] int NULL,
    [SN_DATEI_BIS] int NULL,
    [MITARBEITER] nvarchar(40) NOT NULL,
    [BRUCHREKLA] int NULL,
    [BRUCHREKLA_VON] int NULL,
    [BRUCHREKLA_BIS] int NULL,
    [ALC_ARCHIV] int NULL,
    [ALC_ARCHIV_VON] int NULL,
    [ALC_ARCHIV_BIS] int NULL,
    [REKLA_ZEIT] int NULL,
    [ROWID] char(36) NOT NULL,
    [ANGEB_PRAEFIX] nvarchar(8) NULL,
    [AUFTR_PRAEFIX] nvarchar(8) NULL,
    [BEST_PRAEFIX] nvarchar(8) NULL,
    [ANFR_PRAEFIX] nvarchar(8) NULL,
    [RECH_PRAEFIX] nvarchar(8) NULL,
    [GUTSCH_PRAEFIX] nvarchar(8) NULL,
    [LS_PRAEFIX] nvarchar(8) NULL,
    [LENR_VON] int NULL,
    [LENR_BIS] int NULL,
    [LENR] int NULL,
    [LENR_PRAEFIX] nvarchar(8) NULL
);

CREATE TABLE SYSADM.[BA_PLANSTK] (
    [BA_TYP] int NOT NULL,
    [BA_BEZ] nvarchar(40) NULL,
    [BA_MIN_WERT] decimal(28,8) NULL,
    [BA_SV1_FAKTOR] decimal(28,8) NULL,
    [BA_SV2_FAKTOR] decimal(28,8) NULL,
    [BA_SV3_FAKTOR] decimal(28,8) NULL,
    [BA_SV4_FAKTOR] decimal(28,8) NULL,
    [BA_SV1] int NULL,
    [BA_SV2] int NULL,
    [BA_SV3] int NULL,
    [BA_SV4] int NULL,
    [BA_SV5] int NULL,
    [BA_SV6] int NULL,
    [BA_SV7] int NULL,
    [BA_SV8] int NULL,
    [BA_SV9] int NULL,
    [BA_SV10] int NULL,
    [BA_SV5_FAKTOR] decimal(28,8) NULL,
    [BA_SV6_FAKTOR] decimal(28,8) NULL,
    [BA_SV7_FAKTOR] decimal(28,8) NULL,
    [BA_SV8_FAKTOR] decimal(28,8) NULL,
    [BA_SV9_FAKTOR] decimal(28,8) NULL,
    [BA_SV10_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE1_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE2_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE3_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE4_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE1] int NULL,
    [BA_SERIE2] int NULL,
    [BA_SERIE3] int NULL,
    [BA_SERIE4] int NULL,
    [BA_SERIE5] int NULL,
    [BA_SERIE6] int NULL,
    [BA_SERIE7] int NULL,
    [BA_SERIE8] int NULL,
    [BA_SERIE9] int NULL,
    [BA_SERIE10] int NULL,
    [BA_SERIE5_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE6_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE7_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE8_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE9_FAKTOR] decimal(28,8) NULL,
    [BA_SERIE10_FAKTOR] decimal(28,8) NULL,
    [BA_LIMIT_1] decimal(28,8) NULL,
    [BA_LIMIT_2] decimal(28,8) NULL,
    [BA_PL_MINUTEN] decimal(28,8) NULL,
    [BA_MIN_QM] decimal(28,8) NULL,
    [BA_MIN_STK] decimal(28,8) NULL,
    [BA_MIN_LFM] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_POOL_GLASART] (
    [GLASART] int NOT NULL,
    [TEXT] nvarchar(40) NOT NULL,
    [GLASARTGRP] int NOT NULL,
    [BESCHICH] int NOT NULL,
    [STRUKTUR] int NOT NULL,
    [ARTIKEL_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PROD_VERPEINH] (
    [BA_PRODUKT] int NOT NULL,
    [BA_VERP_EINH] nvarchar(40) NOT NULL,
    [BA_ABW_WGR] nvarchar(3) NOT NULL,
    [BA_LAGERORT] nvarchar(40) NOT NULL,
    [BA_MENGENEINH] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PRZ_SCHLS] int NOT NULL,
    [PRZ_SCHLS_EK] int NOT NULL,
    [LAG_MENGE] decimal(28,8) NULL,
    [LAG_MIND_BEST] decimal(28,8) NULL,
    [LAG_BEST_MENGE] decimal(28,8) NULL,
    [LAG_STAND_MENGE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODGRP_REST] (
    [PRDKTART_ID] nvarchar(40) NOT NULL,
    [PRDKTGRP_ID] nvarchar(40) NOT NULL,
    [GLASDICKE_VON] decimal(28,8) NOT NULL,
    [GLASDICKE_BIS] decimal(28,8) NOT NULL,
    [REST_ID] int NOT NULL,
    [PARAM1] decimal(28,8) NULL,
    [PARAM2] decimal(28,8) NULL,
    [PARAM3] decimal(28,8) NULL,
    [PARAM4] decimal(28,8) NULL,
    [PARAM5] decimal(28,8) NULL,
    [LEVEL] int NULL,
    [IST_MASS1] int NULL,
    [IST_MASS2] int NULL,
    [IST_MASS3] int NULL,
    [IST_MASS4] int NULL,
    [IST_MASS5] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKT_AUFBAU] (
    [ID] int NOT NULL,
    [AUFBAU_PROD] nvarchar(240) NULL,
    [AUFBAU_BOM] nvarchar(240) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKT_KZ] (
    [PRODUKT] int NOT NULL,
    [MANDANT] int NOT NULL,
    [AVBEREICH] nvarchar(40) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELL] int NOT NULL,
    [LAG_PRIME] int NOT NULL,
    [GUELTIG_IN] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE] (
    [BA_PRODUKT] int NOT NULL,
    [BA_MCODE] nvarchar(80) NULL,
    [BA_FREMD_KEY] nvarchar(20) NULL,
    [BA_SN_KEY] nvarchar(80) NULL,
    [BA_WGR] nvarchar(3) NOT NULL,
    [BA_WGR_STAT] nvarchar(3) NOT NULL,
    [BA_PRODUKTART] nvarchar(40) NOT NULL,
    [BA_PRODUKTGRP] nvarchar(40) NOT NULL,
    [BA_MASS_RUNDUNGBR] int NOT NULL,
    [BA_MASS_RUNDUNGHOE] int NOT NULL,
    [BA_REST_MAXFL] decimal(28,8) NOT NULL,
    [BA_STD_HOEHE] decimal(28,8) NOT NULL,
    [BA_REST_MINBREITE] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [BA_PLANSTK] decimal(28,8) NOT NULL,
    [EK_MAX_HOEHE] decimal(28,8) NOT NULL,
    [BA_REST_ISOFAEHIG] int NOT NULL,
    [BA_REST_ISOAUST] int NOT NULL,
    [BA_PRODUKT_AUSW] int NOT NULL,
    [BA_BEARBINDEX] int NOT NULL,
    [BA_MODELLNR] int NOT NULL,
    [BA_SPROSSENNR] int NOT NULL,
    [BA_DATUM] datetime NULL,
    [BA_MITARB_ID] nvarchar(40) NOT NULL,
    [BA_STD_BREITE] decimal(28,8) NOT NULL,
    [LAG_MAX_HOEHE] decimal(28,8) NOT NULL,
    [EK_MAX_BREITE] decimal(28,8) NOT NULL,
    [FER_VERSCHNITT] decimal(28,8) NOT NULL,
    [PRZ_GRUND_ZUSCHL] decimal(28,8) NOT NULL,
    [EK_KZ_BESTELL] int NOT NULL,
    [LAG_MAX_BREITE] decimal(28,8) NOT NULL,
    [BEST_FAKTOR] decimal(28,8) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [LAG_KZ_LAGER] int NOT NULL,
    [FER_OPTIKEY] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [FER_MASSZUSCHL_BR] int NOT NULL,
    [FER_MASSZUSCHL_HO] int NOT NULL,
    [FER_KZ_BESCHICHTET] int NOT NULL,
    [FER_KZ_DREH] int NOT NULL,
    [FER_KZ_BIEGER] int NOT NULL,
    [FER_KZ_AUTOZUSCHN] int NOT NULL,
    [FER_KZ_STUECKLISTE] int NOT NULL,
    [PRZ_KZ_RABATT] int NOT NULL,
    [PRZ_KZ_SKONTO] int NOT NULL,
    [PRZ_KZ_RABATTUEB] int NOT NULL,
    [PRZ_PREIS_OFFEN] int NOT NULL,
    [PRZ_ZUSCHLAG_ART] int NOT NULL,
    [PRZ_PREISTAB] int NOT NULL,
    [PRZ_GRUNDTAB] int NOT NULL,
    [SENK_WINKEL] decimal(28,8) NULL,
    [PRZ_SONST_ZUSCHL] int NOT NULL,
    [PRZ_KZ_STEUER] int NOT NULL,
    [PREISRELEVANT] int NOT NULL,
    [ALC_BEARB_TYP] int NOT NULL,
    [LAG_PRIME] int NOT NULL,
    [ALC_PLAN_TEXT] nvarchar(12) NULL,
    [PRZ_PREISTAB_EK] int NULL,
    [PRZ_SON_TAB] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [EAN] nvarchar(20) NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [FER_MAX_BREITE] decimal(28,8) NOT NULL,
    [FER_MAX_HOEHE] decimal(28,8) NOT NULL,
    [KZ_CONNECT] int NOT NULL,
    [ALC_PLAN_PRIO] int NOT NULL,
    [DRUCK] int NOT NULL,
    [EXT_STAT] nvarchar(8) NULL,
    [BA_SUR_REST_SVERH] decimal(28,8) NOT NULL,
    [BA_SUR_REST_MAXFL] decimal(28,8) NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [BA_SKIZZE_NAME] nvarchar(254) NULL,
    [BA_SKIZZE_DRUCK] int NOT NULL,
    [EDGE_QUALITY] int NOT NULL,
    [BA_MASS_DICKE] decimal(28,8) NOT NULL,
    [BA_MASS_GEWICHT] decimal(28,8) NOT NULL,
    [BA_REST_MINHOEHE] decimal(28,8) NOT NULL,
    [BA_REST_SVERH] decimal(28,8) NOT NULL,
    [ALC_DAUER] int NOT NULL,
    [ALC_SCHICHT_LAGE] int NOT NULL,
    [ALC_MASCHIN_NR] int NOT NULL,
    [ALC_MASCHIN_NR_LOG] int NOT NULL,
    [ALC_UEBERGANGSZEIT] int NOT NULL,
    [ALC_SEQUENZ] int NOT NULL,
    [ALC_ZUSCHLAGGRUPPE] int NOT NULL,
    [SENK_TYP] int NULL,
    [ALC_GLASART] int NOT NULL,
    [ALC_SCHLEIFGRUPPE] int NOT NULL,
    [SENK_PARAM] int NULL,
    [ALC_K_WERT] int NOT NULL,
    [ALC_RAND_L] int NOT NULL,
    [ALC_RAND_R] int NOT NULL,
    [ALC_RAND_O] int NOT NULL,
    [ALC_RAND_U] int NOT NULL,
    [ALC_MAX_TRAVERE] int NOT NULL,
    [ALC_MIN_X_Z] int NOT NULL,
    [FER_SZR_KZ] nvarchar(4) NULL,
    [ALC_RANG] int NOT NULL,
    [ALC_PLANSTATUS] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [ALC_TECHART] nvarchar(8) NULL,
    [ALC_MIN_FLAECHE] decimal(28,8) NOT NULL,
    [PRZ_ZUSCHLAG_BIT] int NOT NULL,
    [PRZ_MINDERMENGE] int NOT NULL,
    [KZ_AINFO] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [ALC_BEARB_FLAGS] nvarchar(20) NULL,
    [BA_SN_MAKRO_NAME] nvarchar(254) NULL,
    [CE_TYP] nvarchar(1) NULL,
    [CE_REFERENZ] nvarchar(30) NULL,
    [CE_LEVEL] int NOT NULL,
    [CE_INFLUENCING] int NOT NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [FX_PRODUCT] int NOT NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [STD_UEBERMENGE] decimal(28,8) NULL,
    [STD_UNTERMENGE] decimal(28,8) NULL,
    [NO_STOCK_FORECAST] int NULL,
    [TAX_PRODUCT_TYPE] int NOT NULL,
    [SAP_PRODUCT_CODE] nvarchar(10) NULL,
    [SAP_MATERIAL_TYPE] nvarchar(10) NULL,
    [SAP_STATISTIC_CODE] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [LE_FLAG] int NOT NULL,
    [PM_SHAPEOPTI] int NOT NULL,
    [PM_STRUKTVERL] int NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [PM_RESIDUALWIDTH] decimal(28,8) NULL,
    [PM_RESIDUALHEIGHT] decimal(28,8) NULL,
    [CLASSIFIERS] xml NULL,
    [BEA_MIN1] decimal(28,8) NULL,
    [BEA_MIN2] decimal(28,8) NULL,
    [BEA_MIN3] decimal(28,8) NULL,
    [BEA_MIN4] decimal(28,8) NULL,
    [BEA_MIN5] decimal(28,8) NULL,
    [BEA_MIN6] decimal(28,8) NULL,
    [BEA_MIN7] decimal(28,8) NULL,
    [BEA_MIN8] decimal(28,8) NULL,
    [BEA_MIN9] decimal(28,8) NULL,
    [BEA_MIN10] decimal(28,8) NULL,
    [BEA_MIN11] decimal(28,8) NULL,
    [BEA_MIN12] decimal(28,8) NULL,
    [BEA_MIN13] decimal(28,8) NULL,
    [BEA_MIN14] decimal(28,8) NULL,
    [BEA_MIN15] decimal(28,8) NULL,
    [BEA_MIN16] decimal(28,8) NULL,
    [BEA_MIN17] decimal(28,8) NULL,
    [BEA_MIN18] decimal(28,8) NULL,
    [BEA_MIN19] decimal(28,8) NULL,
    [BEA_MIN20] decimal(28,8) NULL,
    [BEA_MIN21] decimal(28,8) NULL,
    [BEA_MIN22] decimal(28,8) NULL,
    [BEA_MIN23] decimal(28,8) NULL,
    [BEA_MIN24] decimal(28,8) NULL,
    [BEA_MIN25] decimal(28,8) NULL,
    [BEA_MIN26] decimal(28,8) NULL,
    [BEA_MIN27] decimal(28,8) NULL,
    [BEA_MIN28] decimal(28,8) NULL,
    [BEA_MIN29] decimal(28,8) NULL,
    [BEA_MIN30] decimal(28,8) NULL,
    [BEA_MAX1] decimal(28,8) NULL,
    [BEA_MAX2] decimal(28,8) NULL,
    [BEA_MAX3] decimal(28,8) NULL,
    [BEA_MAX4] decimal(28,8) NULL,
    [BEA_MAX5] decimal(28,8) NULL,
    [BEA_MAX6] decimal(28,8) NULL,
    [BEA_MAX7] decimal(28,8) NULL,
    [BEA_MAX8] decimal(28,8) NULL,
    [BEA_MAX9] decimal(28,8) NULL,
    [BEA_MAX10] decimal(28,8) NULL,
    [BEA_MAX11] decimal(28,8) NULL,
    [BEA_MAX12] decimal(28,8) NULL,
    [BEA_MAX13] decimal(28,8) NULL,
    [BEA_MAX14] decimal(28,8) NULL,
    [BEA_MAX15] decimal(28,8) NULL,
    [BEA_MAX16] decimal(28,8) NULL,
    [BEA_MAX17] decimal(28,8) NULL,
    [BEA_MAX18] decimal(28,8) NULL,
    [BEA_MAX19] decimal(28,8) NULL,
    [BEA_MAX20] decimal(28,8) NULL,
    [BEA_MAX21] decimal(28,8) NULL,
    [BEA_MAX22] decimal(28,8) NULL,
    [BEA_MAX23] decimal(28,8) NULL,
    [BEA_MAX24] decimal(28,8) NULL,
    [BEA_MAX25] decimal(28,8) NULL,
    [BEA_MAX26] decimal(28,8) NULL,
    [BEA_MAX27] decimal(28,8) NULL,
    [BEA_MAX28] decimal(28,8) NULL,
    [BEA_MAX29] decimal(28,8) NULL,
    [BEA_MAX30] decimal(28,8) NULL,
    [PRZ_ZUSCHLAG2_BIT] int NOT NULL,
    [PRZ_ISO_AUFBAU] int NOT NULL,
    [KZ_XAVANNAH] int NOT NULL,
    [XAVANNAH_DATUM] datetime NULL,
    [IQUOTE_CHARTER] int NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE_ATTACH] (
    [BA_PRODUKT] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE_BEZ] (
    [SPRACH_ID] int NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [BA_BEZ1] nvarchar(40) NULL,
    [BA_BEZ2] nvarchar(40) NULL,
    [BA_BEZ3] nvarchar(65) NULL,
    [ALC_BESCHICH_LAGE] int NOT NULL,
    [BA_PRODUKT_INFO] nvarchar(max) NULL,
    [BA_MENGENEINH] nvarchar(20) NOT NULL,
    [BA_STTXT_NR] int NOT NULL,
    [FER_STRUKTSEITE] int NOT NULL,
    [FER_STRUKTVERLAUF] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [BA_KURZ_BEZ] nvarchar(32) NULL,
    [ALC_STRUKT_LAGE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BA_ABW_MENGENEINH] nvarchar(20) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE_FAR] (
    [BA_PRODUKT] int NOT NULL,
    [BA_FARB_ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [BA_PREISTAB] int NULL,
    [BA_WGR] nvarchar(3) NOT NULL,
    [BA_WGR_STAT] nvarchar(3) NOT NULL,
    [EK_KZ_BESTELL] int NOT NULL,
    [LAG_KZ_LAGER] int NOT NULL,
    [VORGABE] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [FER_SZR_KZ] nvarchar(4) NULL,
    [BA_MASS_GEWICHT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [FREMD_KEY] nvarchar(10) NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE_MOTIV] (
    [ID] int NOT NULL,
    [NAME] nvarchar(254) NOT NULL,
    [DATEI] nvarchar(254) NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_PRODUKTE_TI] (
    [BA_PRODUKT] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [KEEP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_SN_RULES] (
    [BA_PRODUKTART] nvarchar(40) NOT NULL,
    [BA_PRODUKTGRP] nvarchar(40) NOT NULL,
    [OPTIONS] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_STUKL] (
    [PRODUKT] int NOT NULL,
    [STRUKTV] int NOT NULL,
    [STRUKTS] int NOT NULL,
    [DRUCK] int NULL,
    [LIEFERANT] int NOT NULL,
    [BESTKZ] int NOT NULL,
    [BESCHICHS] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [REDUKTION] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PREISRELEVANT] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [ABW_WGR] nvarchar(3) NOT NULL,
    [BEARB_INS] int NOT NULL,
    [ABW_WGR_STAT] nvarchar(3) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VARIANTE] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [MENGE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NOT NULL,
    [BREITE] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [VARIANTE_BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [EXCHANGE_RULE] int NOT NULL,
    [BEARB_INS2] int NULL,
    [TRANSACTION_TIME] datetime NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [CLASSIFIERS] xml NULL
);

CREATE TABLE SYSADM.[BA_STUKL_BEARB] (
    [PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL,
    [BEA_MIN1] decimal(28,8) NULL,
    [BEA_MIN2] decimal(28,8) NULL,
    [BEA_MIN3] decimal(28,8) NULL,
    [BEA_MIN4] decimal(28,8) NULL,
    [BEA_MIN5] decimal(28,8) NULL,
    [BEA_MIN6] decimal(28,8) NULL,
    [BEA_MIN7] decimal(28,8) NULL,
    [BEA_MIN8] decimal(28,8) NULL,
    [BEA_MIN9] decimal(28,8) NULL,
    [BEA_MIN10] decimal(28,8) NULL,
    [BEA_MIN11] decimal(28,8) NULL,
    [BEA_MIN12] decimal(28,8) NULL,
    [BEA_MIN13] decimal(28,8) NULL,
    [BEA_MIN14] decimal(28,8) NULL,
    [BEA_MIN15] decimal(28,8) NULL,
    [BEA_MIN16] decimal(28,8) NULL,
    [BEA_MIN17] decimal(28,8) NULL,
    [BEA_MIN18] decimal(28,8) NULL,
    [BEA_MIN19] decimal(28,8) NULL,
    [BEA_MIN20] decimal(28,8) NULL,
    [BEA_MIN21] decimal(28,8) NULL,
    [BEA_MIN22] decimal(28,8) NULL,
    [BEA_MIN23] decimal(28,8) NULL,
    [BEA_MIN24] decimal(28,8) NULL,
    [BEA_MIN25] decimal(28,8) NULL,
    [BEA_MIN26] decimal(28,8) NULL,
    [BEA_MIN27] decimal(28,8) NULL,
    [BEA_MIN28] decimal(28,8) NULL,
    [BEA_MIN29] decimal(28,8) NULL,
    [BEA_MIN30] decimal(28,8) NULL,
    [BEA_MAX1] decimal(28,8) NULL,
    [BEA_MAX2] decimal(28,8) NULL,
    [BEA_MAX3] decimal(28,8) NULL,
    [BEA_MAX4] decimal(28,8) NULL,
    [BEA_MAX5] decimal(28,8) NULL,
    [BEA_MAX6] decimal(28,8) NULL,
    [BEA_MAX7] decimal(28,8) NULL,
    [BEA_MAX8] decimal(28,8) NULL,
    [BEA_MAX9] decimal(28,8) NULL,
    [BEA_MAX10] decimal(28,8) NULL,
    [BEA_MAX11] decimal(28,8) NULL,
    [BEA_MAX12] decimal(28,8) NULL,
    [BEA_MAX13] decimal(28,8) NULL,
    [BEA_MAX14] decimal(28,8) NULL,
    [BEA_MAX15] decimal(28,8) NULL,
    [BEA_MAX16] decimal(28,8) NULL,
    [BEA_MAX17] decimal(28,8) NULL,
    [BEA_MAX18] decimal(28,8) NULL,
    [BEA_MAX19] decimal(28,8) NULL,
    [BEA_MAX20] decimal(28,8) NULL,
    [BEA_MAX21] decimal(28,8) NULL,
    [BEA_MAX22] decimal(28,8) NULL,
    [BEA_MAX23] decimal(28,8) NULL,
    [BEA_MAX24] decimal(28,8) NULL,
    [BEA_MAX25] decimal(28,8) NULL,
    [BEA_MAX26] decimal(28,8) NULL,
    [BEA_MAX27] decimal(28,8) NULL,
    [BEA_MAX28] decimal(28,8) NULL,
    [BEA_MAX29] decimal(28,8) NULL,
    [BEA_MAX30] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[BA_STUKL_GEBOGEN] (
    [PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_STUKL_MODELL] (
    [PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_STUKL_SPROSSEN] (
    [PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [BIT] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DURCHGANG] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [VERMASSUNG] int NULL,
    [ERFASUNGSTYP] int NULL,
    [NOPPEN_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_SZR_RESTRICT] (
    [PRODART] nvarchar(40) NOT NULL,
    [PRODGRP] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [REGEL_ID] int NOT NULL,
    [AUSWPRODART] nvarchar(40) NOT NULL,
    [AUSWPRODGRP] nvarchar(40) NOT NULL,
    [PARAM1] decimal(28,8) NULL,
    [PARAM2] decimal(28,8) NULL,
    [PARAM3] decimal(28,8) NULL,
    [PARAM4] decimal(28,8) NULL,
    [AUSWSZR_VARIANTE] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_TECH_INFO] (
    [ID] int NOT NULL,
    [GLAS_1] int NOT NULL,
    [GLAS_2] int NOT NULL,
    [GLAS_3] int NOT NULL,
    [SZR_1] int NOT NULL,
    [SZR_2] int NOT NULL,
    [GAS_1] int NOT NULL,
    [GAS_2] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [B_FAKTOR] decimal(28,8) NOT NULL,
    [DB_WERT] decimal(28,8) NOT NULL,
    [DICKEN_TOLERANZ] decimal(28,8) NOT NULL,
    [GROESSEN_TOL1] decimal(28,8) NOT NULL,
    [GROESSEN_TOL2] decimal(28,8) NOT NULL,
    [SPR1] decimal(28,8) NOT NULL,
    [SPR2] decimal(28,8) NOT NULL,
    [K_WERT] decimal(28,8) NOT NULL,
    [K_WERT_DIN] decimal(28,8) NOT NULL,
    [L_WERT] decimal(28,8) NOT NULL,
    [G_WERT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_TI_TEXTE] (
    [TI_NR] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [BEZ] nvarchar(200) NULL,
    [EINHEIT] nvarchar(30) NULL,
    [NORM] nvarchar(60) NULL,
    [KEEP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_TOE_RULES] (
    [ID] int NOT NULL,
    [SORT_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [PROD_ART_ACTIVE] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [FORMULA_ACTIVE] int NOT NULL,
    [FORMULA] int NOT NULL,
    [EDGE_QUALITY_ACTIVE] int NOT NULL,
    [EDGE_QUALITY] int NOT NULL,
    [BEA_PARAM1_ACTIVE] int NOT NULL,
    [BEA_PARAM1_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM1_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM2_ACTIVE] int NOT NULL,
    [BEA_PARAM2_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM2_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM3_ACTIVE] int NOT NULL,
    [BEA_PARAM3_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM3_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM4_ACTIVE] int NOT NULL,
    [BEA_PARAM4_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM4_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM5_ACTIVE] int NOT NULL,
    [BEA_PARAM5_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM5_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM6_ACTIVE] int NOT NULL,
    [BEA_PARAM6_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM6_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM7_ACTIVE] int NOT NULL,
    [BEA_PARAM7_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM7_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM8_ACTIVE] int NOT NULL,
    [BEA_PARAM8_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM8_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM9_ACTIVE] int NOT NULL,
    [BEA_PARAM9_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM9_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM10_ACTIVE] int NOT NULL,
    [BEA_PARAM10_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM10_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM11_ACTIVE] int NOT NULL,
    [BEA_PARAM11_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM11_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM12_ACTIVE] int NOT NULL,
    [BEA_PARAM12_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM12_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM13_ACTIVE] int NOT NULL,
    [BEA_PARAM13_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM13_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM14_ACTIVE] int NOT NULL,
    [BEA_PARAM14_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM14_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM15_ACTIVE] int NOT NULL,
    [BEA_PARAM15_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM15_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM16_ACTIVE] int NOT NULL,
    [BEA_PARAM16_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM16_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM17_ACTIVE] int NOT NULL,
    [BEA_PARAM17_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM17_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM18_ACTIVE] int NOT NULL,
    [BEA_PARAM18_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM18_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM19_ACTIVE] int NOT NULL,
    [BEA_PARAM19_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM19_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM20_ACTIVE] int NOT NULL,
    [BEA_PARAM20_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM20_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM21_ACTIVE] int NOT NULL,
    [BEA_PARAM21_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM21_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM22_ACTIVE] int NOT NULL,
    [BEA_PARAM22_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM22_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM23_ACTIVE] int NOT NULL,
    [BEA_PARAM23_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM23_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM24_ACTIVE] int NOT NULL,
    [BEA_PARAM24_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM24_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM25_ACTIVE] int NOT NULL,
    [BEA_PARAM25_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM25_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM26_ACTIVE] int NOT NULL,
    [BEA_PARAM26_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM26_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM27_ACTIVE] int NOT NULL,
    [BEA_PARAM27_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM27_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM28_ACTIVE] int NOT NULL,
    [BEA_PARAM28_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM28_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM29_ACTIVE] int NOT NULL,
    [BEA_PARAM29_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM29_TO] decimal(28,8) NOT NULL,
    [BEA_PARAM30_ACTIVE] int NOT NULL,
    [BEA_PARAM30_FROM] decimal(28,8) NOT NULL,
    [BEA_PARAM30_TO] decimal(28,8) NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_TR_WGR] (
    [PRODUKT] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [HWG] int NOT NULL,
    [UWG] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_UEBERMENGEN] (
    [PROD_ID] int NOT NULL,
    [ID] int NOT NULL,
    [UEBERMENGE] decimal(28,8) NULL,
    [UNTERMENGE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_XOPT_JUMBO] (
    [GLASART] int NOT NULL,
    [JUMBO_NR] int NOT NULL,
    [LAENGE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [TRAVEREN_FLAG] nvarchar(1) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BA_XOPT_TISCHJUMBO] (
    [TISCH_ID] nvarchar(4) NOT NULL,
    [GLASART] int NOT NULL,
    [JUMBO_NR] int NOT NULL,
    [AUFLEGER_CODE] int NOT NULL,
    [ANZAHL] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ALCIM_EINH_STKL] (
    [EINHEIT] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [POINT1] decimal(28,8) NULL,
    [POINT2] decimal(28,8) NULL,
    [POINT3] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ALCIM_EINHEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [EINHEIT] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [POINT1] decimal(28,8) NULL,
    [POINT2] decimal(28,8) NULL,
    [POINT3] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ALCIM_RECEIVE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [VERSION] nvarchar(5) NULL,
    [DATEI_INHALT] int NULL,
    [DATEI_ART] int NULL,
    [UNT_POS] int NULL,
    [TEILE_NR] int NULL,
    [HAUPT_AUFTR] int NULL,
    [HAUPT_POS] int NULL,
    [POS_REFERENZ] nvarchar(10) NULL,
    [TEILE_REFERENZ] nvarchar(32) NULL,
    [PROD_POS] int NULL,
    [JOB_NR] int NULL,
    [LOS_TYP] int NULL,
    [LOS_NR] int NULL,
    [BOCK] int NULL,
    [BOCK_TYP] int NULL,
    [LAUF] int NULL,
    [SEQUENZ_LOS] int NULL,
    [STAPEL_NR] int NULL,
    [SEQUENZ_STAPEL] int NULL,
    [ZUGABE_FLAG] int NULL,
    [SERIEN_FLAG] int NULL,
    [MENGEN_FLAG] int NULL,
    [AUFFUELL_FLAG] int NULL,
    [BESCHAFF_ART] int NULL,
    [MASCHINE] int NULL,
    [PROD_DATUM] date NULL,
    [PROD_SCHICHT] int NULL,
    [BOM_ID] int NOT NULL,
    [MENGE] decimal(28,8) NULL,
    [ETIK_NR_VON] decimal(28,8) NULL,
    [ETIK_NR_BIS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_AUFTR] (
    [ANFR_ID] int NOT NULL,
    [ANFR_POS] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [AB_LIEFERANT] nvarchar(20) NULL,
    [AB_DATUM] date NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_AUFTR_STKL] (
    [ANFR_ID] int NOT NULL,
    [ANFR_POS] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [AB_LIEFERANT] nvarchar(20) NULL,
    [AB_DATUM] date NULL,
    [BOM_ID] int NOT NULL,
    [ANFR_BOM_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BW_ANFR_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BW_ANFR_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_HIST] (
    [ID] int NOT NULL,
    [REF_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [PUNKT_ID] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [DATUM] datetime NOT NULL,
    [BEM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL,
    [LIEFTERM_GRND_SGG] nvarchar(40) NULL,
    [CRM_REFERENCE] int NULL,
    [CRM_PROJECT_NO] int NULL,
    [IQUOTE_AUTO_DATE] int NULL
);

CREATE TABLE SYSADM.[BW_ANFR_KTXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [POS_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BW_ANFR_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BW_ANFR_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BW_ANFR_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANFR_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_HIST] (
    [ID] int NOT NULL,
    [REF_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [PUNKT_ID] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [DATUM] datetime NOT NULL,
    [BEM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL,
    [LIEFTERM_GRND_SGG] nvarchar(40) NULL,
    [CRM_REFERENCE] int NULL,
    [CRM_PROJECT_NO] int NULL,
    [IQUOTE_AUTO_DATE] int NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_KTXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [POS_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_OPTI] (
    [OPTI_ID] int NOT NULL,
    [ID] int NOT NULL,
    [GLASSTYPE] int NOT NULL,
    [WASTE] decimal(28,8) NOT NULL,
    [SELECTED] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANGEB_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ANLIEF] (
    [KUNDE] int NOT NULL,
    [DATUM] date NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_ANZAHLUNG] (
    [ID] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [MITARBEITER] nvarchar(40) NOT NULL,
    [ZAHLUNGSART] nvarchar(40) NOT NULL,
    [STATUS] int NOT NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [BEMERKUNG] nvarchar(254) NOT NULL,
    [TYP] int NOT NULL,
    [BACKUP_EXPORT] nvarchar(254) NULL,
    [BACKUP_IMPORT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [PAYMENT_NO] int NOT NULL,
    [PAYMENT_DIRECTION] int NULL,
    [PAYMENT_TYPE] nvarchar(10) NULL,
    [PAYMENT_REF] nvarchar(30) NULL,
    [CURRENCY_RATE] nvarchar(12) NULL,
    [ePAYMENT] int NULL,
    [COLLECTIVE] int NULL,
    [OVER_PAYMENT] int NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_AUFTR] (
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [INT_AUFTR_ID] int NOT NULL,
    [INT_AUFTR_POS] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [DIFF_BIT] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_AUFTR_STL] (
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [INT_AUFTR_ID] int NOT NULL,
    [INT_AUFTR_POS] int NOT NULL,
    [INT_AUFTR_BOM_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [DIFF_BIT] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_EXPORT] (
    [EMPFAENGER] nvarchar(10) NOT NULL,
    [ID] int NOT NULL,
    [STATUS] int NOT NULL,
    [DOK_TYP] int NOT NULL,
    [PDF_LINK] nvarchar(80) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_HIST] (
    [ID] int NOT NULL,
    [REF_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [PUNKT_ID] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [DATUM] datetime NOT NULL,
    [BEM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_INFO] (
    [WGR_KMB_STAT] nvarchar(3) NOT NULL,
    [DATUM] date NOT NULL,
    [ERF_EK1] decimal(28,8) NOT NULL,
    [ERF_EK2] decimal(28,8) NOT NULL,
    [ERF_PLANSTK] decimal(28,8) NOT NULL,
    [OFFEN_MENGE] decimal(28,8) NOT NULL,
    [OFFEN_QM] decimal(28,8) NOT NULL,
    [OFFEN_LFM] decimal(28,8) NOT NULL,
    [OFFEN_VK] decimal(28,8) NOT NULL,
    [OFFEN_EK1] decimal(28,8) NOT NULL,
    [OFFEN_EK2] decimal(28,8) NOT NULL,
    [OFFEN_PLANSTK] decimal(28,8) NOT NULL,
    [ERF_MENGE] decimal(28,8) NOT NULL,
    [ERF_QM] decimal(28,8) NOT NULL,
    [ERF_LFM] decimal(28,8) NOT NULL,
    [ERF_VK] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL,
    [LIEFTERM_GRND_SGG] nvarchar(40) NULL,
    [CRM_REFERENCE] int NULL,
    [CRM_PROJECT_NO] int NULL,
    [IQUOTE_AUTO_DATE] int NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_KTXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [POS_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_LOG] (
    [ANWENDER] nvarchar(80) NOT NULL,
    [BUCHUNG] datetime NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(40) NOT NULL,
    [INHALT] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [GEDRUCKT] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_LWBEW_POS] (
    [TYP] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT] int NOT NULL,
    [BU_DATUM] datetime NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_LWBEW_ST] (
    [TYP] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT] int NOT NULL,
    [BU_DATUM] datetime NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_MTS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [PROD_DATE] datetime NOT NULL,
    [AGG_ID] int NOT NULL,
    [SHIFT] int NOT NULL,
    [SUPERVISOR] nvarchar(40) NOT NULL,
    [CAP_PROD] int NOT NULL,
    [CAP_WIDTH] decimal(28,8) NOT NULL,
    [CAP_HEIGHT] decimal(28,8) NOT NULL,
    [BUCK_BREAK] int NOT NULL,
    [MACHINE_BREAK] int NOT NULL,
    [VENDOR] int NOT NULL,
    [SETUP_TIME] int NOT NULL,
    [RUN_TIME] int NOT NULL,
    [SHEETS_IN_RUN] int NOT NULL,
    [LITES_COMPLETED] int NOT NULL,
    [LITES_PER_SHEET] int NOT NULL,
    [UNITS] int NOT NULL,
    [LITES_PER_UNIT] int NOT NULL,
    [CREW_SIZE] int NOT NULL,
    [DOWN_TIME] int NOT NULL,
    [DOWN_REASON] nvarchar(254) NULL,
    [BOOKED] int NOT NULL,
    [AUTO_IDENT] int NOT NULL,
    [TRIM_LOSS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [CAP_LOCATION] int NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_OPTI] (
    [OPTI_ID] int NOT NULL,
    [ID] int NOT NULL,
    [GLASSTYPE] int NOT NULL,
    [WASTE] decimal(28,8) NOT NULL,
    [SELECTED] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_PARA_KGP] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_PARA_KGW] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_PARA_KUP] (
    [ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_PARA_KUW] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_PINFO] (
    [WGR_KMB_STAT] nvarchar(3) NOT NULL,
    [DATUM] date NOT NULL,
    [PROD1_EK1] decimal(28,8) NOT NULL,
    [PROD1_EK2] decimal(28,8) NOT NULL,
    [PROD1_PLANSTK] decimal(28,8) NOT NULL,
    [PROD2_MENGE] decimal(28,8) NOT NULL,
    [PROD2_QM] decimal(28,8) NOT NULL,
    [PROD2_LFM] decimal(28,8) NOT NULL,
    [PROD2_VK] decimal(28,8) NOT NULL,
    [PROD2_EK1] decimal(28,8) NOT NULL,
    [PROD2_EK2] decimal(28,8) NOT NULL,
    [PROD2_PLANSTK] decimal(28,8) NOT NULL,
    [PROD3_MENGE] decimal(28,8) NOT NULL,
    [PROD3_QM] decimal(28,8) NOT NULL,
    [PROD3_LFM] decimal(28,8) NOT NULL,
    [PROD3_VK] decimal(28,8) NOT NULL,
    [PROD3_EK1] decimal(28,8) NOT NULL,
    [PROD3_EK2] decimal(28,8) NOT NULL,
    [PROD3_PLANSTK] decimal(28,8) NOT NULL,
    [PROD1_MENGE] decimal(28,8) NOT NULL,
    [PROD1_QM] decimal(28,8) NOT NULL,
    [PROD1_LFM] decimal(28,8) NOT NULL,
    [PROD1_VK] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_RELIEF_NR] (
    [TYP] int NOT NULL,
    [AUFTRAG] int NOT NULL,
    [PRAEFIX] nvarchar(4) NOT NULL,
    [NUMMER] decimal(28,8) NOT NULL,
    [SEITE] int NOT NULL,
    [STORNO] int NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [DATUM] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_RINFO] (
    [DATUM] date NOT NULL,
    [WGR_KMB_STAT] nvarchar(3) NOT NULL,
    [RECH_EK1] decimal(28,8) NOT NULL,
    [RECH_EK2] decimal(28,8) NOT NULL,
    [RECH_PLANSTK] decimal(28,8) NOT NULL,
    [RECH_MENGE] decimal(28,8) NOT NULL,
    [RECH_QM] decimal(28,8) NOT NULL,
    [RECH_LFM] decimal(28,8) NOT NULL,
    [RECH_VK] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_SERIAL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [IDENTKOMP] nvarchar(20) NOT NULL,
    [HERSTELLER] int NOT NULL,
    [DATUM] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_AUFTR_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_ANZAHLUNG] (
    [ID] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [TYP] int NOT NULL,
    [MITARBEITER] nvarchar(40) NOT NULL,
    [ZAHLUNGSART] nvarchar(40) NOT NULL,
    [STATUS] int NOT NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [BEMERKUNG] nvarchar(254) NOT NULL,
    [BACKUP_EXPORT] nvarchar(254) NULL,
    [BACKUP_IMPORT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [PAYMENT_NO] int NOT NULL,
    [PAYMENT_DIRECTION] int NULL,
    [PAYMENT_TYPE] nvarchar(10) NULL,
    [PAYMENT_REF] nvarchar(30) NULL,
    [CURRENCY_RATE] nvarchar(12) NULL,
    [ePAYMENT] int NULL,
    [COLLECTIVE] int NULL,
    [OVER_PAYMENT] int NULL
);

CREATE TABLE SYSADM.[BW_BEST_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_AUFTR] (
    [BEST_ID] int NOT NULL,
    [BEST_POS] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [AB_LIEFERANT] nvarchar(20) NULL,
    [TEILLIEFERTERM] datetime NULL,
    [NR_RECHNUNG] nvarchar(60) NULL,
    [RECH_DATUM] date NULL,
    [KUNDEN_INFO] int NOT NULL,
    [DIFF_BIT] int NOT NULL,
    [MENGE_OK] int NOT NULL,
    [GEL_MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_AUFTR_STKL] (
    [BEST_ID] int NOT NULL,
    [BEST_POS] int NOT NULL,
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [AB_LIEFERANT] nvarchar(20) NULL,
    [TEILLIEFERTERM] datetime NULL,
    [BOM_ID] int NOT NULL,
    [NR_RECHNUNG] nvarchar(60) NULL,
    [RECH_DATUM] date NULL,
    [KUNDEN_INFO] int NOT NULL,
    [DIFF_BIT] int NOT NULL,
    [MENGE_OK] int NOT NULL,
    [GEL_MENGE] decimal(28,8) NOT NULL,
    [BEST_BOM_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BW_BEST_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BW_BEST_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_HIST] (
    [ID] int NOT NULL,
    [REF_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [PUNKT_ID] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [DATUM] datetime NOT NULL,
    [BEM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_AB] (
    [LIEFERANT] int NOT NULL,
    [AB_NR] nvarchar(20) NOT NULL,
    [STATUS] int NULL,
    [DATUM_AB] datetime NOT NULL,
    [DATUM_LIEFERTERMIN] datetime NOT NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [PDF_LINK] nvarchar(80) NULL,
    [BENUTZER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_AB_POS] (
    [LIEFERANT] int NOT NULL,
    [AB_NR] nvarchar(20) NOT NULL,
    [POS_NR] int NOT NULL,
    [BEST_NR] int NULL,
    [BEST_POS] int NULL,
    [POS_MENGE] int NOT NULL,
    [POS_PREIS_GES] decimal(28,8) NOT NULL,
    [PROD_ID_REF] int NULL,
    [PROD_ID_LIEFERANT] nvarchar(64) NULL,
    [PROD_KURZBEZ] nvarchar(80) NULL,
    [PROD_LANGBEZ] nvarchar(max) NULL,
    [DATUM_LIEFERTERMIN] datetime NULL,
    [HOEHE] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [NAVIGLASS] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_LIEF] (
    [LIEFERANT] int NOT NULL,
    [LIEFSCH_NR] nvarchar(20) NOT NULL,
    [STATUS] int NULL,
    [DATUM_LIEFSCH] datetime NOT NULL,
    [PDF_LINK] nvarchar(80) NULL,
    [BENUTZER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_LIEF_POS] (
    [LIEFERANT] int NOT NULL,
    [LIEFSCH_NR] nvarchar(20) NOT NULL,
    [POS_NR] int NOT NULL,
    [BEST_NR] int NULL,
    [BEST_POS] int NULL,
    [POS_MENGE] int NOT NULL,
    [PROD_KURZBEZ] nvarchar(80) NULL,
    [PROD_LANGBEZ] nvarchar(max) NULL,
    [BOX_IDENT] nvarchar(40) NULL,
    [BOX_SHEETS] int NULL,
    [BOOKED] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_RECHN] (
    [LIEFERANT] int NOT NULL,
    [RECHNUNGS_NR] nvarchar(20) NOT NULL,
    [STATUS] int NULL,
    [DATUM_RECHNUNG] datetime NOT NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [BETRAG_MWST] decimal(28,8) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [ZAHL_BED] nvarchar(100) NULL,
    [PDF_LINK] nvarchar(80) NULL,
    [BENUTZER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_IMPORT_RECHN_POS] (
    [LIEFERANT] int NOT NULL,
    [RECHNUNGS_NR] nvarchar(20) NOT NULL,
    [POS_NR] int NOT NULL,
    [BEST_NR] int NULL,
    [BEST_POS] int NULL,
    [POS_MENGE] int NOT NULL,
    [POS_PREIS_GES] decimal(28,8) NOT NULL,
    [PROD_ID_REF] int NULL,
    [PROD_ID_LIEFERANT] nvarchar(64) NULL,
    [PROD_KURZBEZ] nvarchar(80) NULL,
    [PROD_LANGBEZ] nvarchar(max) NULL,
    [HOEHE] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [NAVIGLASS] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL,
    [LIEFTERM_GRND_SGG] nvarchar(40) NULL,
    [CRM_REFERENCE] int NULL,
    [CRM_PROJECT_NO] int NULL,
    [IQUOTE_AUTO_DATE] int NULL
);

CREATE TABLE SYSADM.[BW_BEST_KTXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [POS_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_LOG] (
    [ANWENDER] nvarchar(80) NOT NULL,
    [BUCHUNG] datetime NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(40) NOT NULL,
    [INHALT] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [GEDRUCKT] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_LWBEW_POS] (
    [TYP] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT] int NOT NULL,
    [BU_DATUM] datetime NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NULL
);

CREATE TABLE SYSADM.[BW_BEST_LWBEW_ST] (
    [TYP] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT] int NOT NULL,
    [BU_DATUM] datetime NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_MAN] (
    [BEST_ID] int NOT NULL,
    [BEST_POS] int NOT NULL,
    [AB_LIEFERANT] nvarchar(20) NULL,
    [TEILLIEFERTERM] datetime NULL,
    [NR_RECHNUNG] nvarchar(60) NULL,
    [RECH_DATUM] date NULL,
    [DIFF_BIT] int NOT NULL,
    [MENGE_OK] int NOT NULL,
    [GEL_MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BW_BEST_OPENTRANS] (
    [EMPFAENGER] nvarchar(6) NOT NULL,
    [ID] int NOT NULL,
    [STATUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_PARA_KGP] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_PARA_KGW] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_PARA_KUP] (
    [ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_PARA_KUW] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [KU_POS] int NOT NULL,
    [IMPL_EXPL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_POOLPOS] (
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LIEFERTERM] date NULL,
    [NV_SORT] int NOT NULL,
    [KZ_LIEFERANT] int NOT NULL,
    [MITARB] nvarchar(40) NOT NULL,
    [LIEFERTERMBEST] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_POOLSTKL] (
    [AUFTR_ID] int NOT NULL,
    [AUFTR_POS] int NOT NULL,
    [AUFTR_VATER] int NOT NULL,
    [AUFTR_KIND] int NOT NULL,
    [AUFTR_POSITION] int NOT NULL,
    [AUFTR_STLPOS] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LIEFERTERM] date NULL,
    [BOM_ID] int NOT NULL,
    [NV_SORT] int NOT NULL,
    [KZ_LIEFERANT] int NOT NULL,
    [MITARB] nvarchar(40) NOT NULL,
    [LIEFERTERMBEST] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BW_BEST_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BW_BEST_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_BEST_WE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TYP] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_CONT_EST_PROD] (
    [ID] int NOT NULL,
    [EST_PROD] int NOT NULL,
    [EST_PROD_QM] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_DISCOVERY] (
    [DOK_ID] int NULL,
    [DOK_TYPE] int NULL,
    [TRIGGER_TIMESTAMP] datetime NULL,
    [TRIGGER_TYPE] int NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GESTELL_ZUORD] (
    [GESTELL] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [CLOSED] int NOT NULL,
    [SEITE] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [EINHEIT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUT_ANZAHLUNG] (
    [ID] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [MITARBEITER] nvarchar(40) NOT NULL,
    [ZAHLUNGSART] nvarchar(40) NOT NULL,
    [STATUS] int NOT NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [BEMERKUNG] nvarchar(254) NOT NULL,
    [TYP] int NOT NULL,
    [BACKUP_EXPORT] nvarchar(254) NULL,
    [BACKUP_IMPORT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [PAYMENT_NO] int NOT NULL,
    [PAYMENT_DIRECTION] int NULL,
    [PAYMENT_TYPE] nvarchar(10) NULL,
    [PAYMENT_REF] nvarchar(30) NULL,
    [CURRENCY_RATE] nvarchar(12) NULL,
    [ePAYMENT] int NULL,
    [COLLECTIVE] int NULL,
    [OVER_PAYMENT] int NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [BEM] nvarchar(80) NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LINK_TYPE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_GEBOGEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [N_SEHNE] decimal(28,8) NULL,
    [N_STICHHOEHE] decimal(28,8) NULL,
    [N_ABWICKLUNG] decimal(28,8) NULL,
    [N_RADIUS] decimal(28,8) NULL,
    [I_SEHNE] decimal(28,8) NULL,
    [I_STICHHOEHE] decimal(28,8) NULL,
    [I_ABWICKLUNG] decimal(28,8) NULL,
    [I_RADIUS] decimal(28,8) NULL,
    [GRADMASS] decimal(28,8) NULL,
    [STICHHOEHE] decimal(28,8) NULL,
    [SEHNE] decimal(28,8) NULL,
    [FORM_ART] int NULL,
    [A_SEHNE] decimal(28,8) NULL,
    [A_STICHHOEHE] decimal(28,8) NULL,
    [A_ABWICKLUNG] decimal(28,8) NULL,
    [A_RADIUS] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BIT] int NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_GGMOD_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [GGA_SCHEMA] varbinary(max) NULL,
    [BILD_BLOB] varbinary(max) NULL,
    [LFDM] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_GGMOD_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_STL] int NOT NULL,
    [ART_NR] int NOT NULL,
    [FARB_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] int NOT NULL,
    [HOEHE] int NOT NULL,
    [BEZEICHNUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_HIST] (
    [ID] int NOT NULL,
    [REF_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [PUNKT_ID] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [DATUM] datetime NOT NULL,
    [BEM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL,
    [LIEFTERM_GRND_SGG] nvarchar(40) NULL,
    [CRM_REFERENCE] int NULL,
    [CRM_PROJECT_NO] int NULL,
    [IQUOTE_AUTO_DATE] int NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_KTXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [POS_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [ROWID] char(36) NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL,
    [MAKRO_ID] int NULL,
    [XAVANNAH_KEY] nvarchar(36) NULL,
    [XAVANNAH_POS] int NULL,
    [XAVANNAH_IMAGE] varbinary(max) NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_POS_TI] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [TI_NR] int NOT NULL,
    [WERT] nvarchar(250) NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_POS_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [MASS_BIT] int NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [VERMASSUNG] int NULL,
    [DURCHGANG] int NULL,
    [SPR_DIM_ABS] int NOT NULL,
    [SPR_DICKE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NOT NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [FILENAME] nvarchar(80) NULL,
    [ERFASUNGSTYP] int NULL,
    [AWDESIGN] varbinary(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ELEMENT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PR_MENGE] decimal(28,8) NOT NULL,
    [PR_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_BIT] int NULL,
    [EK_MENGE] decimal(28,8) NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [EK_BIT] int NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_STL_ZEIT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [ARBART] int NOT NULL,
    [PERSKOST_GK] decimal(28,8) NOT NULL,
    [PERSVOLLKOST] decimal(28,8) NOT NULL,
    [MASCHKOST] decimal(28,8) NOT NULL,
    [MASCHKOST_GK] decimal(28,8) NOT NULL,
    [MASCHVOLLKOST] decimal(28,8) NOT NULL,
    [PERSKOST] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_GUTSCH_TXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [POS_KZ] int NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [REF] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_INV_BLOCK] (
    [BLOCK_NR] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [ME_EINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [KATEGORIE] nvarchar(6) NOT NULL,
    [BOOKED] int NOT NULL,
    [INHALT_SOLL] int NULL,
    [INHALT_TAT] int NULL,
    [BESTAND_TAT] decimal(28,8) NOT NULL,
    [INV_PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [MITARB] nvarchar(40) NOT NULL,
    [INVENTORY_TAG] nvarchar(100) NULL,
    [MODIFIED] datetime NOT NULL
);

CREATE TABLE SYSADM.[BW_INV_GLOBAL] (
    [AUFN_DATUM] date NOT NULL,
    [GLOBAL_FAKT5] decimal(28,8) NOT NULL,
    [GLOBAL_FAKT_KZ1] nvarchar(8) NOT NULL,
    [FAKT_BEZ1] nvarchar(20) NULL,
    [GLOBAL_FAKT_KZ2] nvarchar(8) NOT NULL,
    [FAKT_BEZ2] nvarchar(20) NULL,
    [GLOBAL_FAKT_KZ3] nvarchar(8) NOT NULL,
    [FAKT_BEZ3] nvarchar(20) NULL,
    [GLOBAL_FAKT_KZ4] nvarchar(8) NOT NULL,
    [FAKT_BEZ4] nvarchar(20) NULL,
    [GLOBAL_FAKT_KZ5] nvarchar(8) NOT NULL,
    [FAKT_BEZ5] nvarchar(20) NULL,
    [GLOBAL_FAKT1] decimal(28,8) NOT NULL,
    [GLOBAL_FAKT2] decimal(28,8) NOT NULL,
    [GLOBAL_FAKT3] decimal(28,8) NOT NULL,
    [GLOBAL_FAKT4] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_INV_LAGERORT] (
    [AUFN_DATUM] date NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_INV_LISTE] (
    [BELEG_NR] int NOT NULL,
    [AUFN_DATUM] date NOT NULL,
    [LAGER_ID] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [BOOKED] int NOT NULL,
    [TIME_STAMP] datetime NOT NULL,
    [PR_LAGER] decimal(28,8) NOT NULL,
    [INV_PREIS] decimal(28,8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [ME_EINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [KATEGORIE] nvarchar(6) NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [SOLL_ERMITTELT] int NOT NULL,
    [INHALT_SOLL] int NULL,
    [INHALT_TAT] int NULL,
    [BESTAND_SOLL] decimal(28,8) NULL,
    [BESTAND_TAT] decimal(28,8) NULL,
    [PREIS_ME] decimal(28,8) NOT NULL,
    [PREIS_BLATT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_INV_PR_BER] (
    [LAGER_ID] int NOT NULL,
    [FAKT5] decimal(28,8) NOT NULL,
    [FAKT_KZ1] nvarchar(8) NOT NULL,
    [INV_PREIS] decimal(28,8) NOT NULL,
    [FAKT_KZ2] nvarchar(8) NOT NULL,
    [FAKT_KZ3] nvarchar(8) NOT NULL,
    [FAKT_KZ4] nvarchar(8) NOT NULL,
    [FAKT_KZ5] nvarchar(8) NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [FAKT1] decimal(28,8) NOT NULL,
    [FAKT2] decimal(28,8) NOT NULL,
    [FAKT3] decimal(28,8) NOT NULL,
    [FAKT4] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_LADELISTE] (
    [NR] int NOT NULL,
    [VERSENDER] int NOT NULL,
    [BEMERKUNG] nvarchar(max) NULL,
    [DATUM] date NOT NULL,
    [SPEDITEUR] nvarchar(40) NOT NULL,
    [ANZTEILE] int NOT NULL,
    [GESGEWICHT] decimal(28,8) NOT NULL,
    [LIEFTEXT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_LAGER_STAT] (
    [DATUM] date NOT NULL,
    [LAGER_ID] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [MENGE_ZU] decimal(28,8) NOT NULL,
    [MENGE_AB] decimal(28,8) NOT NULL,
    [LAGER_IDENT_OLD] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_LAUF_KOPF] (
    [LAUF_NR] int NOT NULL,
    [STATUS] nvarchar(20) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_LAUF_POS] (
    [LAUF_NR] int NOT NULL,
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ARBGANG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_LOCK] (
    [DOK_TYP] int NOT NULL,
    [DOK_NUMMER] int NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [DATUM] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [GUID] char(36) NULL
);

CREATE TABLE SYSADM.[BW_LOCK_KAPA] (
    [DOK_TYP] int NOT NULL,
    [DOK_NUMMER] int NOT NULL,
    [STATUS] int NULL,
    [BEM] nvarchar(254) NULL,
    [KAPA_STATUS] int NULL,
    [ROWID] char(36) NOT NULL,
    [CHANGED] int NULL,
    [KAPA_KALK_STATUS] int NOT NULL
);

CREATE TABLE SYSADM.[BW_LOGBOOK] (
    [DATUM] datetime NOT NULL,
    [MESSAGE] nvarchar(max) NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [PROG_ID] int NOT NULL,
    [PRINTED] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_MAIL] (
    [ID] int NOT NULL,
    [FROM_ID] nvarchar(40) NOT NULL,
    [TO_ID] nvarchar(40) NOT NULL,
    [DATUM] datetime NULL,
    [MAIL] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_NUMVERW] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TYP] int NOT NULL,
    [NV_NAME] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [NV_SORTID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ORDERXML] (
    [ID] int NOT NULL,
    [DOK_TYP] int NOT NULL,
    [DATUM_SAVED] datetime NOT NULL,
    [XML_TEXT] varbinary(max) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_STATUS] int NOT NULL
);

CREATE TABLE SYSADM.[BW_PRINT_JOBS] (
    [JOB_ID] int NOT NULL,
    [JOB_NAME] nvarchar(80) NOT NULL,
    [NV_NAME] nvarchar(40) NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TYP] int NOT NULL,
    [OPTIONS] nvarchar(254) NOT NULL,
    [STATUS_ID] int NOT NULL,
    [STATUS_TEXT] nvarchar(254) NULL,
    [RESPONSE] int NULL,
    [MANDANT] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [PRINT_DEF] int NOT NULL,
    [DATE] date NOT NULL,
    [ARCHIV] int NOT NULL,
    [MSGBOX_TXT] nvarchar(max) NULL,
    [MSGBOX_TITLE] nvarchar(254) NULL,
    [MSGBOX_FLAGS] int NULL,
    [MSGBOX_MSG] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_PROD_AUFBAU] (
    [ID] int NOT NULL,
    [AUFBAU1] nvarchar(248) NOT NULL,
    [AUFBAU2] nvarchar(248) NULL,
    [AUFBAU3] nvarchar(248) NULL,
    [AUFBAU4] nvarchar(248) NULL,
    [AUFBAU5] nvarchar(248) NULL,
    [AUFBAU6] nvarchar(248) NULL,
    [AUFBAU7] nvarchar(248) NULL,
    [AUFBAU8] nvarchar(248) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_PRODUKT_AUFBAU_LENR] (
    [ID] int NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [HASH] nvarchar(32) NOT NULL,
    [AUFBAU_1] nvarchar(248) NULL,
    [AUFBAU_2] nvarchar(248) NULL,
    [AUFBAU_3] nvarchar(248) NULL,
    [AUFBAU_4] nvarchar(248) NULL,
    [AUFBAU_5] nvarchar(248) NULL,
    [AUFBAU_6] nvarchar(248) NULL,
    [AUFBAU_7] nvarchar(248) NULL,
    [AUFBAU_8] nvarchar(248) NULL,
    [ERF_DATUM] datetime NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [NOCREATE] int NOT NULL
);

CREATE TABLE SYSADM.[BW_TEILBEST] (
    [BEST_ID] int NOT NULL,
    [BEST_POS] int NOT NULL,
    [TEIL_BEST_ID] int NOT NULL,
    [TEIL_BEST_POS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_TEMP_DATA] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [LFD_NR] int NOT NULL,
    [PARTNER] int NOT NULL,
    [DATE] datetime NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_TRANSFER] (
    [DOK_ID] int NOT NULL,
    [DATUM_ERFASST] date NOT NULL,
    [DOK_TYP] int NOT NULL,
    [DATUM_DOK] date NULL,
    [MANDANT] int NULL,
    [PARTNER_ID] int NULL,
    [OBJEKT_ID] int NULL,
    [HAUPT_DOK_ID] int NULL,
    [REF_DOK_ID] int NULL,
    [BEARBEITER] nvarchar(40) NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [NR_RECHNUNG] nvarchar(20) NULL,
    [DATUM_RECHNUNG] date NULL,
    [DOK_BETRAG] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL
);

CREATE TABLE SYSADM.[BW_VORLAGE] (
    [ID] int NOT NULL,
    [DOC_TYPE] int NOT NULL,
    [BEARBEITER] nvarchar(40) NOT NULL,
    [VORLAGE] datetime NULL,
    [ZYKLUS] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ZOLL] (
    [ID] int NOT NULL,
    [ZOLLTOUR1] nvarchar(40) NOT NULL,
    [SUBID] int NOT NULL,
    [ZOLLTOUR2] nvarchar(40) NOT NULL,
    [IDENT] int NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [LAND] nvarchar(6) NULL,
    [PLZ] nvarchar(11) NULL,
    [ORT] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [LIEFERTERMIN] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[BW_ZOLL_POS] (
    [ID] int NOT NULL,
    [AUFTRAG_ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ZOLL_NR_IMPORT] nvarchar(15) NULL,
    [ZOLL_NR_EXPORT] nvarchar(15) NULL,
    [ZOLL_BEZ_IMPORT] nvarchar(40) NULL,
    [ZOLL_BEZ_EXPORT] nvarchar(40) NULL,
    [FW_CODE] nvarchar(8) NOT NULL,
    [DRITTLAND] int NOT NULL,
    [STUECKZAHL] decimal(28,8) NOT NULL,
    [LKW_KZ] nvarchar(40) NOT NULL,
    [STKL_POS] int NOT NULL,
    [GEWICHT_NET] decimal(28,8) NOT NULL,
    [GEWICHT_BRU] decimal(28,8) NOT NULL,
    [WARENWERT] decimal(28,8) NOT NULL,
    [WARENWERT_FW] decimal(28,8) NOT NULL,
    [SUBID] int NOT NULL,
    [FLAECHE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_ATP_SETTINGS] (
    [URL] nvarchar(254) NULL,
    [USERNAME] nvarchar(254) NULL,
    [PASS] nvarchar(254) NULL,
    [FORMEL] int NULL,
    [SELEKTION] int NULL,
    [ASNURL] nvarchar(254) NULL,
    [STATUSURL] nvarchar(254) NULL,
    [STATUSURL_ECOM] nvarchar(254) NULL,
    [ASNURL_ECOM] nvarchar(254) NULL,
    [USERNAME_ECOM] nvarchar(254) NULL,
    [PASSWORD_ECOM] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[CU_SGG_CEDEFAULT] (
    [TYP] nvarchar(1) NOT NULL,
    [VALUE] nvarchar(8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CEDIMREST] (
    [CECODE] nvarchar(8) NOT NULL,
    [MAXLENCERT] decimal(28,8) NULL,
    [MAXWIDCERT] decimal(28,8) NULL,
    [MAXLENTEST] decimal(28,8) NULL,
    [MAXWIDTEST] decimal(28,8) NULL,
    [TEXT] nvarchar(30) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CEHS] (
    [CETEMPERED] nvarchar(8) NOT NULL,
    [CEHEATSOAK] nvarchar(8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CEIGU] (
    [CEGLAS1] nvarchar(8) NOT NULL,
    [COATPOSGLAS1] int NOT NULL,
    [CEGLAS2] nvarchar(8) NOT NULL,
    [COATPOSGLAS2] int NOT NULL,
    [CEGLAS3] nvarchar(8) NOT NULL,
    [COATPOSGLAS3] int NOT NULL,
    [SZR1] decimal(28,8) NOT NULL,
    [GAS1] nvarchar(2) NOT NULL,
    [SEALANT1] nvarchar(2) NOT NULL,
    [FRAME1] nvarchar(2) NOT NULL,
    [GORBAR1] nvarchar(2) NOT NULL,
    [SZR2] decimal(28,8) NOT NULL,
    [GAS2] nvarchar(2) NOT NULL,
    [SEALANT2] nvarchar(2) NOT NULL,
    [FRAME2] nvarchar(2) NOT NULL,
    [GORBAR2] nvarchar(2) NOT NULL,
    [IGUCECODE] nvarchar(8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CELAM] (
    [ELEMENT1] nvarchar(16) NOT NULL,
    [ELEMENT2] nvarchar(16) NOT NULL,
    [ELEMENT3] nvarchar(16) NOT NULL,
    [ELEMENT4] nvarchar(16) NOT NULL,
    [ELEMENT5] nvarchar(16) NOT NULL,
    [ELEMENT6] nvarchar(16) NOT NULL,
    [ELEMENT7] nvarchar(16) NOT NULL,
    [ELEMENT8] nvarchar(16) NOT NULL,
    [ELEMENT9] nvarchar(16) NOT NULL,
    [ELEMENT10] nvarchar(16) NOT NULL,
    [LAMCECODE] nvarchar(8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CEPRODLIST] (
    [CECODE] nvarchar(8) NOT NULL,
    [VALDAT] date NULL,
    [AOC1] int NULL,
    [CENORM] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_CREDITLIMIT] (
    [CUST_ID] int NOT NULL,
    [EULER_ID] int NOT NULL,
    [CHANGETIME] datetime NULL,
    [ND_COVER_FLAG] int NULL,
    [ND_COVER_AMOUNT] int NULL,
    [PRIMARY_GUARANTEE] int NULL,
    [TEMPORARY_GUARANTEE] int NULL,
    [CAP_GUARANTEE] int NULL,
    [CAP_PLUS_GUARANTEE] int NULL,
    [MANUALLY_GUARANTEE] int NULL,
    [MANUALLY_GUARANTEE_END_DATE] date NULL,
    [BOSS_GUARANTEE] int NULL,
    [BOSS_GUARANTEE_END_DATE] date NULL,
    [ROWID] char(36) NOT NULL,
    [EULER_CHANGE_FLAG] int NULL,
    [CREDIT_EH] decimal(28,8) NULL,
    [CREDITLIMIT_EH] decimal(28,8) NULL,
    [CREDIT_TURN_EH] decimal(28,8) NULL,
    [CAP_PLUS_EH] decimal(28,8) NULL,
    [CREDITLIMIT_NET_EH] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[CU_SGG_DELIVERY] (
    [ID] int NOT NULL,
    [ORDER_NO] int NOT NULL,
    [ITEM_NO] int NOT NULL,
    [DELIVERY_ID] int NULL,
    [DELIVERY_BARCODE] nvarchar(40) NULL,
    [RACK_BARCODE] nvarchar(40) NULL,
    [RACK_ID] int NULL,
    [RACK_TYPE] nvarchar(40) NULL,
    [RACK_NAME] nvarchar(40) NULL,
    [SHIPPED_QTY] int NULL,
    [DELIVERED_QTY] int NULL,
    [NOT_DELIVERED_QTY] int NULL,
    [SG4P_IMPORT_DATE] datetime NULL,
    [G4U_IMPORT_DATE] datetime NULL,
    [STATUS] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_ECOM_CUST] (
    [ID] int NOT NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERSTELLT] datetime NULL
);

CREATE TABLE SYSADM.[CU_SGG_ECOM_FTP] (
    [URL] nvarchar(254) NULL,
    [USERNAME] nvarchar(254) NULL,
    [PASS] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[CU_SGG_ECOM_ORDER] (
    [ID] int NOT NULL,
    [ESTATUS] int NOT NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERSTELLT] datetime NULL,
    [BEMERKUNG] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[CU_SGG_ECOM_WEB] (
    [URL] nvarchar(254) NULL,
    [USERNAME] nvarchar(254) NULL,
    [PASS] nvarchar(254) NULL,
    [URL_OMS] nvarchar(254) NULL,
    [USERNAME_OMS] nvarchar(254) NULL,
    [PASSWORD_OMS] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[CU_SGG_GLASS4U_TRANSFER] (
    [DOK_NUMMER] int NULL,
    [AKTION] nvarchar(2) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_KU_WGR_GRU] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_LI_WGR_GRU] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[CU_SGG_OMS_TRANSFER] (
    [TYPE] int NULL,
    [NUMBER] int NULL,
    [AKTION] int NULL,
    [STATUS] int NULL,
    [AWSTATUS] int NULL,
    [BEMERKUNG] nvarchar(254) NULL,
    [INSERTDATE] datetime NULL
);

CREATE TABLE SYSADM.[DR_AUFTR_FORM] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [DRUCKPUNKT] int NOT NULL,
    [FORMULAR] int NOT NULL,
    [DRUCKER] nvarchar(254) NOT NULL,
    [BESTAETIGUNG] int NOT NULL,
    [DOK_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[DR_DRUCK] (
    [ID] int NOT NULL,
    [PUNKTE_ID] int NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [REP_ID1] int NULL,
    [FORMULAR1] int NULL,
    [DEVICE1] nvarchar(254) NULL,
    [DRIVER1] nvarchar(254) NULL,
    [PORT1] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[DR_DRUCKPUNKTE] (
    [ID] int NOT NULL,
    [NAME] nvarchar(50) NULL,
    [STATUS_ID] int NOT NULL,
    [TYP] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[DR_KUNDEN_LENR] (
    [KUNDE] int NOT NULL,
    [LENR] nvarchar(40) NOT NULL,
    [GESENDET_AM] datetime NOT NULL,
    [GESENDET_MIT] nvarchar(100) NULL,
    [FORMULAR] int NOT NULL,
    [VERSANDART] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[DR_REPORTE] (
    [ID] int NOT NULL,
    [PUNKTE_ID] int NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [BEZ] nvarchar(40) NULL,
    [STANDARD] int NULL,
    [PROT_KZ] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[DR_SESSION] (
    [CHAR_COL1] nvarchar(254) NULL,
    [CHAR_COL2] nvarchar(254) NULL,
    [CHAR_COL3] nvarchar(254) NULL,
    [CHAR_COL4] nvarchar(254) NULL,
    [CHAR_COL5] nvarchar(254) NULL,
    [CHAR_COL6] nvarchar(254) NULL,
    [CHAR_COL7] nvarchar(254) NULL,
    [CHAR_COL8] nvarchar(254) NULL,
    [CHAR_COL9] nvarchar(254) NULL,
    [CHAR_COL10] nvarchar(254) NULL,
    [CHAR_COL11] nvarchar(254) NULL,
    [CHAR_COL12] nvarchar(254) NULL,
    [INT_COL1] int NULL,
    [INT_COL2] int NULL,
    [INT_COL3] int NULL,
    [INT_COL4] int NULL,
    [INT_COL5] int NULL,
    [INT_COL6] int NULL,
    [INT_COL7] int NULL,
    [INT_COL8] int NULL,
    [INT_COL9] int NULL,
    [DATE_COL1] date NULL,
    [DATE_COL2] date NULL,
    [FLOAT_COL1] float NULL,
    [FLOAT_COL2] float NULL,
    [TIME_COL1] datetime NULL,
    [TIME_COL2] datetime NULL,
    [SESSION_ID] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FAN_ST_TEMP] (
    [ZUSATZ] int NOT NULL,
    [VERTRETER] int NOT NULL,
    [KUNDE] int NOT NULL,
    [DATUM] date NOT NULL,
    [AUFBAU] nvarchar(60) NOT NULL,
    [STUECK] int NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [WERT] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [SPRFLD] int NOT NULL,
    [R_WERT] decimal(28,8) NOT NULL,
    [G_WERT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FAN_ST_TEMP_F] (
    [ZUSATZ] int NOT NULL,
    [DATUM] date NOT NULL,
    [AUFBAU] nvarchar(60) NOT NULL,
    [STUECK] int NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [WERT] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [SPRFLD] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FAN_STATI] (
    [STAT_TYP] int NOT NULL,
    [VERTRETER] int NOT NULL,
    [KUNDE] int NOT NULL,
    [DATUM] date NOT NULL,
    [ZUSATZ] int NOT NULL,
    [AUFBAU] nvarchar(60) NOT NULL,
    [PRODNR1] int NOT NULL,
    [PRODNR2] int NOT NULL,
    [PRODNR3] int NOT NULL,
    [SZR1DICKE] int NOT NULL,
    [SZR2DICKE] int NOT NULL,
    [GAS1] int NOT NULL,
    [GAS2] int NOT NULL,
    [R_PGR] int NOT NULL,
    [MODELL] int NOT NULL,
    [STUECK] int NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [WERT] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [SPRFLD] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_ATT_KOPF] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_ATT_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STKL_NR] int NOT NULL,
    [BEA_PARA5] decimal(28,8) NULL,
    [BEA_PARA6] decimal(28,8) NULL,
    [BEA_PARA7] decimal(28,8) NULL,
    [BEA_PARA8] decimal(28,8) NULL,
    [BEA_PARA9] decimal(28,8) NULL,
    [BEA_PARA10] decimal(28,8) NULL,
    [BEA_PARA11] decimal(28,8) NULL,
    [BEA_PARA12] decimal(28,8) NULL,
    [BEA_PARA13] decimal(28,8) NULL,
    [BEA_PARA14] decimal(28,8) NULL,
    [BEA_PARA15] decimal(28,8) NULL,
    [BEA_PARA16] decimal(28,8) NULL,
    [BEA_PARA17] decimal(28,8) NULL,
    [BEA_PARA1] decimal(28,8) NULL,
    [BEA_PARA2] decimal(28,8) NULL,
    [BEA_PARA3] decimal(28,8) NULL,
    [BEA_PARA4] decimal(28,8) NULL,
    [BEA_PARA18] decimal(28,8) NULL,
    [BEA_PARA19] decimal(28,8) NULL,
    [BEA_PARA20] decimal(28,8) NULL,
    [BEA_PARA21] decimal(28,8) NULL,
    [BEA_PARA22] decimal(28,8) NULL,
    [BEA_PARA23] decimal(28,8) NULL,
    [BEA_PARA24] decimal(28,8) NULL,
    [BEA_PARA25] decimal(28,8) NULL,
    [BEA_PARA26] decimal(28,8) NULL,
    [BEA_PARA27] decimal(28,8) NULL,
    [BEA_PARA28] decimal(28,8) NULL,
    [BEA_PARA29] decimal(28,8) NULL,
    [BEA_PARA30] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[FS_BESTELLINFO] (
    [BESTELLUNG] int NOT NULL,
    [POSITION] int NOT NULL,
    [AUFTRAG] int NOT NULL,
    [A_POSITION] int NOT NULL,
    [A_BOMID] int NOT NULL,
    [STATUS] int NOT NULL,
    [ANWENDER] nvarchar(40) NULL,
    [MODIFIED] datetime NULL,
    [STORNO] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [MENGE] int NULL
);

CREATE TABLE SYSADM.[FS_BOOK_HISTORY] (
    [ID] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOMID] int NOT NULL,
    [SUBPOS] int NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [REG_POINT] int NOT NULL,
    [WORK_TYPE] int NOT NULL,
    [SCANTIME] datetime NULL,
    [AMOUNT] int NOT NULL,
    [ORIGIN] int NOT NULL,
    [BOOK_TYPE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BREAKAGE_REASON] int NULL,
    [BREAKAGE_CAUSER] int NULL,
    [BARCODE] nvarchar(20) NOT NULL,
    [RACK] nvarchar(20) NOT NULL
);

CREATE TABLE SYSADM.[FS_BRUCH] (
    [AUFNR] int NULL,
    [POSNR] int NULL,
    [BOMID] int NULL,
    [DATUM] datetime NULL,
    [KZ_BESTELLT] int NULL,
    [KZ_NACHBESTELLEN] int NULL,
    [MENGE] int NULL,
    [KZ_GRUND] int NULL,
    [KZ_STATUS] int NULL,
    [BEMERKUNG] nvarchar(254) NULL,
    [LABEL] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_DOWNPAYMENT] (
    [ID] int NOT NULL,
    [PAYMENT_NO] int NOT NULL,
    [PAYMENT_DATE] datetime NULL,
    [PAYMENT_DIRECTION] int NULL,
    [PAYMENT_MODE] nvarchar(40) NULL,
    [PAYMENT_TYPE] nvarchar(10) NULL,
    [CURRENCY] nvarchar(8) NULL,
    [CURRENCY_RATE] nvarchar(12) NULL,
    [DOCUMENT_TYPE] int NULL,
    [PAYMENT_REF] nvarchar(30) NULL,
    [ROWID] char(36) NOT NULL,
    [AMOUNT] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[FS_KF_TEXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TYP] int NOT NULL,
    [DRUCK_KZ] nvarchar(1) NULL,
    [BEZ] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KOPF] (
    [ID] int NOT NULL,
    [TYP] int NOT NULL,
    [ALFAK_STATUS] int NOT NULL,
    [FILE_NAME] nvarchar(260) NULL,
    [FILE_DATE] datetime NOT NULL,
    [DOK_ART] int NULL,
    [MODUS] int NULL,
    [AH_IDENT] int NULL,
    [DATUM_LIEFER] date NULL,
    [DATUM_BEST] date NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [VORG_DOK_NUM] int NULL,
    [LADELISTE] int NULL,
    [MANDANT] int NULL,
    [HOEHE_NN] int NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AH_POSTFACH_PLZ] nvarchar(11) NULL,
    [AH_LAND] nvarchar(6) NULL,
    [AH_PROVINZ] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AL_IDENT] int NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_PROVINZ] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [AR_IDENT] int NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AR_POSTFACH_PLZ] nvarchar(11) NULL,
    [AR_LAND] nvarchar(6) NULL,
    [AR_PROVINZ] nvarchar(40) NULL,
    [DATUM_RECHNUNG] date NULL,
    [NR_RECHNUNG] int NULL,
    [EDI_VERSION] nvarchar(8) NULL,
    [STATUS] int NULL,
    [KZ_GESPERRT] int NULL,
    [AH_LIEFER_KZ] int NULL,
    [USER_FIELD] nvarchar(100) NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [FI_MWST1] nvarchar(40) NULL,
    [FI_MWST1_VALUE] decimal(28,8) NULL,
    [DATUM_PROD1] datetime NULL,
    [DATUM_PROD2] datetime NULL,
    [DATUM_PROD3] datetime NULL,
    [OR_TOUR] nvarchar(40) NULL,
    [OR_LIEFERBED] nvarchar(40) NULL,
    [DOK_TYP] nvarchar(40) NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [OR_VERPACKUNG] nvarchar(40) NULL,
    [OR_FACHBERATER] nvarchar(40) NULL,
    [OR_ADIENST] nvarchar(40) NULL,
    [DATUM_ANLIEFERUNG] datetime NULL,
    [OR_AVBEREICH] nvarchar(40) NULL,
    [OR_GESCHART] nvarchar(80) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [PRIORITAET] int NULL
);

CREATE TABLE SYSADM.[FS_KU_BEARB] (
    [BOM_ID] int NOT NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BEA_PARAM21] int NULL,
    [BEA_PARAM22] int NULL,
    [BEA_PARAM23] int NULL,
    [BEA_PARAM24] int NULL,
    [BEA_PARAM25] int NULL,
    [BEA_PARAM26] int NULL,
    [BEA_PARAM27] int NULL,
    [BEA_PARAM28] int NULL,
    [BEA_PARAM29] int NULL,
    [BEA_PARAM30] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[FS_KU_GAS] (
    [KDNR] int NOT NULL,
    [FREMD_TYP] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_MODELL] (
    [BOM_ID] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [MOD_ANZ_ECKEN] int NULL,
    [MOD_DRUCK] int NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_PRODUKTE] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(64) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [PREIS_EINH] nvarchar(20) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [VALID_DATE] date NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [LOGO_POS] int NULL,
    [LOGO_NR] int NULL,
    [ID] int NOT NULL,
    [FARBE] int NOT NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [FLAGS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_PRODUKTE_ATTACH] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_SPROSSEN] (
    [BOM_ID] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_DIM_ABS] int NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_STRUKTE] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(1) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_STRUKTV] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(1) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_STUKL] (
    [STL_FARBE] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [LOGO_POS] int NULL,
    [LOGO_NR] int NULL,
    [ID] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [STRUKTV] int NOT NULL,
    [STRUKTS] int NOT NULL,
    [BREITE] int NULL,
    [HOEHE] int NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [STL_MOD] int NULL,
    [MENGE] decimal(28,8) NULL,
    [FER_BESCHICH_SEITE] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_SZR] (
    [KDNR] int NOT NULL,
    [FREMD_TYP] int NOT NULL,
    [FREMD_DICKE] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_KU_TOUREN] (
    [KDNR] int NOT NULL,
    [FREMD_TOUR] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_BEARB] (
    [BOM_ID] int NOT NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [BEA_PARAM21] int NULL,
    [BEA_PARAM22] int NULL,
    [BEA_PARAM23] int NULL,
    [BEA_PARAM24] int NULL,
    [BEA_PARAM25] int NULL,
    [BEA_PARAM26] int NULL,
    [BEA_PARAM27] int NULL,
    [BEA_PARAM28] int NULL,
    [BEA_PARAM29] int NULL,
    [BEA_PARAM30] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[FS_LI_GAS] (
    [KDNR] int NOT NULL,
    [FREMD_TYP] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_MODELL] (
    [BOM_ID] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [MOD_ANZ_ECKEN] int NULL,
    [MOD_DRUCK] int NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_PRODUKTE] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(64) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [PREIS_EINH] nvarchar(20) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [VALID_DATE] date NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [LOGO_POS] int NULL,
    [LOGO_NR] int NULL,
    [ID] int NOT NULL,
    [FARBE] int NOT NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [FLAGS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_PRODUKTE_ATTACH] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_KZ] nvarchar(1) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_SPROSSEN] (
    [BOM_ID] int NOT NULL,
    [SPR_DIRECTION] int NOT NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_TYP] int NULL,
    [KZ_SPR_KONSTR] int NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [SPR_DIM_ABS] int NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_STRUKTE] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(1) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_STRUKTV] (
    [KDNR] int NOT NULL,
    [FREMD_ID] nvarchar(1) NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_STUKL] (
    [STL_FARBE] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [LOGO_POS] int NULL,
    [LOGO_NR] int NULL,
    [ID] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [STRUKTV] int NOT NULL,
    [STRUKTS] int NOT NULL,
    [BREITE] int NULL,
    [HOEHE] int NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [STL_MOD] int NULL,
    [MENGE] decimal(28,8) NULL,
    [FER_BESCHICH_SEITE] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_SZR] (
    [KDNR] int NOT NULL,
    [FREMD_TYP] int NOT NULL,
    [FREMD_DICKE] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BA_PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_LI_TOUREN] (
    [KDNR] int NOT NULL,
    [FREMD_TOUR] int NOT NULL,
    [FREMD_BEZ] nvarchar(40) NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STKL_NR] int NOT NULL,
    [MOD_NR] int NULL,
    [MOD_SN_NAME] nvarchar(40) NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [STUFE_GLAS] int NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[FS_POOL] (
    [ID] int NOT NULL,
    [SEQUENZ_NR] int NOT NULL,
    [DATENSATZ] nvarchar(max) NOT NULL,
    [STATUS] int NULL,
    [INDEXFELD1] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_POOL_KOPF] (
    [ID] int NOT NULL,
    [DATEI_NAME] nvarchar(80) NOT NULL,
    [TYP] int NULL,
    [DATUM_ERSTELLT] datetime NOT NULL,
    [DATUM_IMPORTIERT] datetime NOT NULL,
    [DATUM_VERARBEITET] datetime NULL,
    [STATUS] int NULL,
    [BENUTZER] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [MITARB] nvarchar(40) NULL
);

CREATE TABLE SYSADM.[FS_POOL_LOG] (
    [ID] int NOT NULL,
    [SEQUENZ_NR] int NOT NULL,
    [NR_MELDUNG] int NULL,
    [MELDUNG] nvarchar(254) NULL,
    [MELDUNG_ART] int NULL,
    [ZEITSTEMPEL] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [ALFAK_STATUS] int NOT NULL,
    [KZ_GESPERRT] int NULL,
    [KOSTENLOS] int NULL,
    [PROD_ID] nvarchar(64) NULL,
    [PROD_BEZ1] nvarchar(40) NULL,
    [PROD_BEZ2] nvarchar(40) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_FARBE] nvarchar(40) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PP_DICKE] decimal(28,8) NULL,
    [PP_FALZ] decimal(28,8) NULL,
    [RUECKSCHNITT] decimal(28,8) NULL,
    [FER_BESCHICH_SEITE] int NULL,
    [FER_STRUKTURVERL] int NULL,
    [FER_STRUKTURSEITE] int NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [FER_KSCHUTZ] int NULL,
    [LOGO_NR] int NULL,
    [LOGO_POS] int NULL,
    [FER_GEST_TYP] int NULL,
    [FER_GEST_ANZ] int NULL,
    [AUFTR_REF] int NULL,
    [POS_REF] int NULL,
    [POS_GRUPPE] int NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [KZ_RANDENT] int NULL,
    [REKLA_FREMD_KEY] nvarchar(6) NULL,
    [CHECK_RESTRIKT] int NULL,
    [PP_BREITE] decimal(28,8) NULL,
    [PP_HOEHE] decimal(28,8) NULL,
    [PP_MENGE] decimal(28,8) NULL,
    [FI_POS_STK] decimal(28,8) NULL,
    [USER_FIELD] nvarchar(100) NULL,
    [FER_BESCHAFFARTNR] int NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [PREISRELEVANT] int NULL,
    [ROWID] char(36) NOT NULL,
    [PREIS_TYP] int NULL,
    [COMPLAINT_REASON] nvarchar(40) NULL,
    [COMPLAINT_LOCATION] nvarchar(40) NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [GRUPPE] int NULL
);

CREATE TABLE SYSADM.[FS_PROTOCOLL] (
    [GUID] nvarchar(36) NOT NULL,
    [SEQUENZ] int NOT NULL,
    [MESSAGETYPE] int NOT NULL,
    [MESSAGEFILENUMBER] int NULL,
    [MESSAGE] nvarchar(max) NULL,
    [EXECUTION] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_PROTOCOLL_HEAD] (
    [AUTO_PROCESS_ID] int NULL,
    [WORKFLOW] nvarchar(254) NULL,
    [GUID] nvarchar(36) NOT NULL,
    [EXECUTION] datetime NOT NULL,
    [MESSAGETYPE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [PROCESS_ID] int NULL
);

CREATE TABLE SYSADM.[FS_SPROSSEN] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STKL_NR] int NOT NULL,
    [SPR_TYP] int NULL,
    [SPR_BER_KZ] int NULL,
    [SPR_DIRECTION] int NULL,
    [SPR_ABSTAND5] decimal(28,8) NULL,
    [SPR_ABSTAND6] decimal(28,8) NULL,
    [SPR_ABSTAND7] decimal(28,8) NULL,
    [SPR_ABSTAND8] decimal(28,8) NULL,
    [SPR_ABSTAND9] decimal(28,8) NULL,
    [SPR_ABSTAND10] decimal(28,8) NULL,
    [SPR_ABSTAND11] decimal(28,8) NULL,
    [SPR_ABSTAND12] decimal(28,8) NULL,
    [SPR_ABSTAND13] decimal(28,8) NULL,
    [SPR_ABSTAND14] decimal(28,8) NULL,
    [SPR_ABSTAND15] decimal(28,8) NULL,
    [SPR_ABSTAND16] decimal(28,8) NULL,
    [SPR_ABSTAND17] decimal(28,8) NULL,
    [SPR_ABSTAND18] decimal(28,8) NULL,
    [SPR_ABSTAND19] decimal(28,8) NULL,
    [SPR_ABSTAND20] decimal(28,8) NULL,
    [SPR_ASYM1] decimal(28,8) NULL,
    [SPR_ASYM2] decimal(28,8) NULL,
    [SPR_ASYM3] decimal(28,8) NULL,
    [SPR_ASYM4] decimal(28,8) NULL,
    [SPR_ASYM5] decimal(28,8) NULL,
    [SPR_ASYM6] decimal(28,8) NULL,
    [SPR_ASYM7] decimal(28,8) NULL,
    [SPR_ASYM8] decimal(28,8) NULL,
    [SPR_ASYM9] decimal(28,8) NULL,
    [SPR_ASYM10] decimal(28,8) NULL,
    [SPR_ASYM11] decimal(28,8) NULL,
    [SPR_ASYM12] decimal(28,8) NULL,
    [SPR_ASYM13] decimal(28,8) NULL,
    [SPR_ASYM14] decimal(28,8) NULL,
    [SPR_ASYM15] decimal(28,8) NULL,
    [SPR_ASYM16] decimal(28,8) NULL,
    [SPR_ASYM17] decimal(28,8) NULL,
    [SPR_ASYM18] decimal(28,8) NULL,
    [SPR_ASYM19] decimal(28,8) NULL,
    [SPR_ASYM20] decimal(28,8) NULL,
    [SPR_MENGE] decimal(28,8) NULL,
    [NOPPEN_KZ] int NULL,
    [SPR_ABSTAND1] decimal(28,8) NULL,
    [SPR_ABSTAND2] decimal(28,8) NULL,
    [SPR_ABSTAND3] decimal(28,8) NULL,
    [SPR_ABSTAND4] decimal(28,8) NULL,
    [DIAGONAL_POS] int NULL,
    [AWD_NAME] nvarchar(40) NULL,
    [FARB_CODE] nvarchar(40) NULL,
    [FARB_BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_STATUS] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STKL_NR] int NOT NULL,
    [BOM_ID] int NULL,
    [BOM_NODE] int NULL,
    [BOM_LEVEL] int NULL,
    [BOM_POS] int NULL,
    [BOM_PRODUKT] nvarchar(64) NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [STL_FARBE] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_PRODGRP] int NULL,
    [FER_BESCHICH_SEITE] int NULL,
    [FER_STRUKTURVERL] int NULL,
    [FER_STRUKTURSEITE] int NULL,
    [STL_PRODART] int NULL,
    [STL_BREITE] decimal(28,8) NULL,
    [STL_HOEHE] decimal(28,8) NULL,
    [STL_MENGE] decimal(28,8) NULL,
    [PR_PREIS_ME] decimal(28,8) NULL,
    [USER_FIELD] nvarchar(100) NULL,
    [BEARB_INS] int NULL,
    [FER_BESCHAFFARTNR] int NULL,
    [BOM_BASE_ID] int NULL,
    [PR_PREISDRUCK] int NULL,
    [STL_MOD] int NULL,
    [PREISRELEVANT] int NULL,
    [ROWID] char(36) NOT NULL,
    [BEARB_INS2] int NULL,
    [PREIS_TYP] int NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [SUPPLIER_ID] int NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL
);

CREATE TABLE SYSADM.[FS_TEXT] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [DRUCK_KZ] nvarchar(1) NULL,
    [POS_KZ] int NULL,
    [BEZ] nvarchar(max) NULL,
    [TEXT_NR] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[FS_TRANSFER] (
    [ID] int NOT NULL,
    [TYP] int NOT NULL,
    [DATUM_ERSTELLT] datetime NULL,
    [STATUS] int NOT NULL,
    [DATEI_NAME] nvarchar(40) NULL,
    [DATENFELD1] nvarchar(40) NULL,
    [DATENFELD2] nvarchar(40) NULL,
    [DATENFELD3] nvarchar(40) NULL,
    [DATENFELD4] nvarchar(40) NULL,
    [DATENFELD5] nvarchar(40) NULL,
    [DATENFELD6] nvarchar(40) NULL,
    [DATENFELD7] nvarchar(40) NULL,
    [DATENFELD8] nvarchar(40) NULL,
    [DATENFELD9] nvarchar(40) NULL,
    [DATENFELD10] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ADIENST] (
    [BEZ] nvarchar(40) NOT NULL,
    [BEMERKUNG] nvarchar(254) NULL,
    [PLZ_VON] nvarchar(11) NULL,
    [PLZ_BIS] nvarchar(11) NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [VORWAHL_BEREICHE] nvarchar(254) NULL,
    [TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ANREDEN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ARBGANG] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [BEARB_BIT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_AREA] (
    [AREA] int NOT NULL,
    [GUARANTEE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ATT_REMARK] (
    [ID] int NOT NULL,
    [BEMERKUNG] nvarchar(80) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ATT_TYPE] (
    [ID] nvarchar(1) NOT NULL,
    [DESCR] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [DOCTYPE] int NULL,
    [IQUOTE_RELEASE] int NOT NULL
);

CREATE TABLE SYSADM.[KA_AUFBAU] (
    [DOKTYP] int NOT NULL,
    [PARAMETER] int NULL,
    [TAGE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_AUFBAU_LENR] (
    [PRODUKTART] nvarchar(40) NOT NULL,
    [PRODUKTGRP] nvarchar(40) NOT NULL,
    [ABW_PRODUKTART] nvarchar(40) NOT NULL,
    [LEVEL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_AUTO_PROCESS] (
    [ID] int NOT NULL,
    [WORKFLOW_ID] int NOT NULL,
    [SEQUENZ] int NOT NULL,
    [INTERVALL] int NOT NULL,
    [LETZTE_AUSF] datetime NULL,
    [TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [EXECUTE_NOW] int NOT NULL,
    [THREAD] int NULL,
    [FROM_HOUR] int NULL,
    [TO_HOUR] int NULL,
    [MAX_TIME] int NULL,
    [SHOULD_EXECUTE] int NOT NULL
);

CREATE TABLE SYSADM.[KA_AVBEREICH] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [GESCH_ART] nvarchar(80) NOT NULL,
    [AUFTR_TYP] nvarchar(40) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [DEF_STOCK_LOCATION] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BANKEN] (
    [BLZ] nvarchar(20) NOT NULL,
    [NAME] nvarchar(40) NULL,
    [SITZ] nvarchar(40) NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KURZBEZ] nvarchar(20) NULL,
    [BIC] nvarchar(11) NULL,
    [PLZ] nvarchar(11) NULL,
    [STRASSE] nvarchar(40) NULL,
    [LAND] nvarchar(2) NULL,
    [FX_IMPORT] int NOT NULL,
    [FX_EXPORT] int NOT NULL,
    [EXPORT_LFD] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BESCHAFFUNGSART] (
    [TYP] int NOT NULL,
    [ID] int NOT NULL,
    [BEZ] nvarchar(30) NOT NULL,
    [BEST_KZ] int NOT NULL,
    [LAG_KZ] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BESCHICHTART] (
    [ID] int NOT NULL,
    [KURZ_BEZ] nvarchar(1) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BESCHICHTSEITE] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [KURZ_BEZ] nvarchar(1) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BONITAET] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BRANCHEN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_BUCH_PERIODE] (
    [NUMMER] int NOT NULL,
    [DATUM] date NOT NULL,
    [AUFTRAG] int NULL,
    [GUTSCHRIFT] int NULL,
    [BESTELLUNG] int NULL,
    [FREMD_KEY] nvarchar(10) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CALENDAR_STATE] (
    [PROVINZ] nvarchar(20) NOT NULL,
    [LAND_KZ] nvarchar(6) NOT NULL,
    [VON] datetime NOT NULL,
    [BIS] datetime NOT NULL,
    [JAHR] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CEKAL_CLASS] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [PLATZHALTER] nvarchar(6) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [BOM_LEVEL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CRYSTAL] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(254) NOT NULL,
    [PATH] nvarchar(254) NOT NULL,
    [DSN] nvarchar(40) NOT NULL,
    [PARAM] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CRYSTAL_GRP] (
    [GRUPPE_ID] nvarchar(40) NOT NULL,
    [REPORT_ID] int NOT NULL,
    [RECHT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CRYSTAL_MIT] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [REPORT_ID] int NOT NULL,
    [RECHT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CTRL_FORMEL] (
    [CTRL_NAME] nvarchar(100) NOT NULL,
    [EVENT] int NOT NULL,
    [QBE_MODE] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_CUSTOMIZINGDATA] (
    [CUSTOMIZING_TYPE] int NOT NULL,
    [BORDER1] nvarchar(60) NOT NULL,
    [BORDER2] nvarchar(60) NOT NULL,
    [BORDER3] nvarchar(60) NOT NULL,
    [CUSTOMIZING_VALUE] nvarchar(60) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_DOK_TYP] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL
);

CREATE TABLE SYSADM.[KA_EBENEN] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [KURZ_BEZ] nvarchar(1) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ERLKO] (
    [ID] nvarchar(3) NOT NULL,
    [ID1] int NOT NULL,
    [ID2] int NOT NULL,
    [BEM] nvarchar(40) NULL,
    [ERLKTODEB] nvarchar(40) NULL,
    [ERLKTOKRE] nvarchar(40) NULL,
    [ERLKTODEB_GU] nvarchar(40) NULL,
    [ERLKTOKRE_GU] nvarchar(40) NULL,
    [KSTSTDEB] nvarchar(40) NULL,
    [KSTSTKRE] nvarchar(40) NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [FREMD_KEY] nvarchar(40) NULL,
    [ID3] int NOT NULL,
    [ID4] int NOT NULL,
    [ID5] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ERLKO_PROD] (
    [PROD_ID] int NOT NULL,
    [ID1] int NOT NULL,
    [ID2] int NOT NULL,
    [BEM] nvarchar(40) NULL,
    [ERLKTODEB] nvarchar(40) NULL,
    [ERLKTOKRE] nvarchar(40) NULL,
    [ERLKTODEB_GU] nvarchar(40) NULL,
    [ERLKTOKRE_GU] nvarchar(40) NULL,
    [KSTSTDEB] nvarchar(40) NULL,
    [KSTSTKRE] nvarchar(40) NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [FREMD_KEY] nvarchar(40) NULL,
    [ID3] int NOT NULL,
    [ID4] int NOT NULL,
    [ID5] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FAHRER] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FAMILY] (
    [PRODUKT] int NOT NULL,
    [FAMILY] nvarchar(35) NULL,
    [FAMILYCODE] nvarchar(2) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FARBE] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(40) NULL,
    [BA_WGR] nvarchar(3) NULL,
    [BA_WGR_STAT] nvarchar(3) NULL,
    [EK_KZ_BESTELL] int NULL,
    [LAG_KZ_LAGER] int NULL,
    [LAG_PRIME] int NULL,
    [R] int NOT NULL,
    [G] int NOT NULL,
    [B] int NOT NULL,
    [SONDERFARBE] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FAVORITEN] (
    [MENU_ID] int NOT NULL,
    [USER_ID] nvarchar(40) NOT NULL,
    [BEZ] nvarchar(60) NULL,
    [LANGUAGE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [ID] int NOT NULL,
    [PARENT] int NULL,
    [POSITION] int NULL
);

CREATE TABLE SYSADM.[KA_FB_TYP] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(160) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FERTSCHLS] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA] (
    [ID] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [KURZINFO] nvarchar(max) NULL,
    [VERSION] nvarchar(4) NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [TLF4] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [FAX] nvarchar(40) NULL,
    [UST_ID] nvarchar(40) NULL,
    [FA_NAME] nvarchar(40) NULL,
    [FA_STRASSE] nvarchar(40) NULL,
    [FA_PLZ] nvarchar(11) NULL,
    [FA_ORT] nvarchar(40) NULL,
    [FA_BLZ] nvarchar(20) NULL,
    [FA_KONTO] nvarchar(20) NULL,
    [STEUER_NR] nvarchar(20) NULL,
    [BETRIEBS_NR] nvarchar(20) NULL,
    [DUMMY1] int NULL,
    [FILIALNR] int NULL,
    [DUMMY2] int NULL,
    [DATUM_ERF_ORG] int NOT NULL,
    [FIBU_DB] nvarchar(20) NULL,
    [GESTELLE_VORHALT] int NOT NULL,
    [STAT_VORHALT] int NOT NULL,
    [AINFO_VORHALT] int NOT NULL,
    [ZEITW_VORHALT] int NOT NULL,
    [PROV_VORHALT] int NOT NULL,
    [LAGER_VORHALT] int NOT NULL,
    [DUMMY3] int NULL,
    [DUMMY4] int NULL,
    [DUMMY5] int NULL,
    [DUMMY6] int NULL,
    [KZ_LAGER] int NULL,
    [DUMMY7] int NULL,
    [DUMMY8] int NULL,
    [KZ_RABATT] int NULL,
    [HISTORIE_DB] nvarchar(20) NULL,
    [DUMMY9] int NULL,
    [RR_LANGUAGE] int NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [DUMMY10] int NULL,
    [DUMMY11] int NULL,
    [DUMMY12] int NULL,
    [DUMMY13] int NULL,
    [DUMMY14] int NULL,
    [DUMMY15] int NULL,
    [MISCHFAKTOR1_2] decimal(28,8) NULL,
    [MISCHFAKTOR2_2] decimal(28,8) NULL,
    [MISCHFAKTOR1_3] decimal(28,8) NULL,
    [MISCHFAKTOR2_3] decimal(28,8) NULL,
    [MISCHFAKTOR3_3] decimal(28,8) NULL,
    [SERIAL_VON] decimal(28,8) NOT NULL,
    [SERIAL_BIS] decimal(28,8) NOT NULL,
    [SERIAL] decimal(28,8) NOT NULL,
    [EURO_FAKTOR] decimal(28,8) NULL,
    [DUMMYA2] nvarchar(254) NULL,
    [DUMMYA3] nvarchar(254) NULL,
    [DUMMYA1] nvarchar(254) NULL,
    [FIBU_USER] nvarchar(20) NULL,
    [FIBU_PASSWD] nvarchar(20) NULL,
    [FIBU_MANDANT] int NULL,
    [MAIL_MODE] int NOT NULL,
    [DIM_DATABASE] int NULL,
    [DIM_FRACTION] int NULL,
    [VORLAUF_PROD1] int NULL,
    [VORLAUF_PROD2] int NULL,
    [VORLAUF_PROD3] int NULL,
    [EIGENWAEHRUNG] nvarchar(8) NOT NULL,
    [DEF_FW_ART] int NULL,
    [ARC_LAST_YEAR] date NULL,
    [ARC_STAT] int NULL,
    [ARC_ARCHIV] int NULL,
    [ARC_DELETE] int NULL,
    [SPERR_TEILLIEF] nvarchar(40) NOT NULL,
    [SPERR_REKLA] nvarchar(40) NULL,
    [EDITION] nvarchar(254) NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [ARC_MODE] int NOT NULL,
    [ARC_DAYS] int NOT NULL,
    [ARC_STATPKT] int NOT NULL,
    [GEWICHTSFAKTOR] decimal(28,8) NOT NULL,
    [OPTIONEN] nvarchar(254) NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [MOD_ZWANG_ANG] int NULL,
    [MOD_ZWANG_AUF] int NULL,
    [MOD_ZWANG_BES] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [FIBU_ASC] int NULL,
    [BU_KREIS] nvarchar(20) NULL,
    [LAGERMODUS] int NOT NULL,
    [ANG_LAST_YEAR] date NULL,
    [ANG_STAT] int NULL,
    [ANG_ARCHIV] int NULL,
    [ANG_DELETE] int NULL,
    [ANG_MODE] int NOT NULL,
    [ANG_DAYS] int NOT NULL,
    [ANG_STATPKT] int NOT NULL,
    [GUT_LAST_YEAR] date NULL,
    [GUT_STAT] int NULL,
    [GUT_ARCHIV] int NULL,
    [GUT_DELETE] int NULL,
    [GUT_MODE] int NOT NULL,
    [GUT_DAYS] int NOT NULL,
    [GUT_STATPKT] int NOT NULL,
    [BES_LAST_YEAR] date NULL,
    [BES_STAT] int NULL,
    [BES_ARCHIV] int NULL,
    [BES_DELETE] int NULL,
    [BES_MODE] int NOT NULL,
    [BES_DAYS] int NOT NULL,
    [BES_STATPKT] int NOT NULL,
    [ANF_LAST_YEAR] date NULL,
    [ANF_STAT] int NULL,
    [ANF_ARCHIV] int NULL,
    [ANF_DELETE] int NULL,
    [ANF_MODE] int NOT NULL,
    [ANF_DAYS] int NOT NULL,
    [ANF_STATPKT] int NOT NULL,
    [BEARB_EXACT] decimal(28,8) NULL,
    [ALC_AVBEREICH] nvarchar(40) NULL,
    [ORDER_TAG] int NOT NULL,
    [DATE_PICTURE] nvarchar(15) NOT NULL,
    [TIME_PICTURE] nvarchar(15) NOT NULL,
    [EK_RECH_KONTR] int NOT NULL,
    [EK_DURCHSCHNITT] int NOT NULL,
    [LOGO] int NOT NULL,
    [KZ_FORM_KATALOG] int NOT NULL,
    [KZ_MOD_DRUCK] int NULL,
    [CUST_VERSION] nvarchar(10) NULL,
    [GESTNR] int NOT NULL,
    [GESTNR_BIS] int NOT NULL,
    [LADELISTE] int NOT NULL,
    [GESTNR_VON] int NULL,
    [PRINTSERVER] nvarchar(40) NULL,
    [LAG_LEVEL1] nvarchar(40) NULL,
    [LAG_LEVEL2] nvarchar(40) NULL,
    [LAG_LEVEL3] nvarchar(40) NULL,
    [LAG_LEVEL4] nvarchar(40) NULL,
    [FIBU_KONTO] nvarchar(20) NULL,
    [OPTI_PROD] nvarchar(254) NULL,
    [VERPACKUNGSART] nvarchar(40) NULL,
    [MAX_FACH1] int NOT NULL,
    [MAX_FACH2] int NOT NULL,
    [ARC_VPROV] int NULL,
    [ANG_VPROV] int NULL,
    [GUT_VPROV] int NULL,
    [BES_VPROV] int NULL,
    [ANF_VPROV] int NULL,
    [EURO] int NULL,
    [INDIV_MODE] int NULL,
    [STKL_ANZ] decimal(28,8) NOT NULL,
    [GUTNR_IS_DOKNR] int NOT NULL,
    [KORE_ACTIV] int NULL,
    [EK_LIEF_SEARCH] int NOT NULL,
    [LADELISTENNR] int NOT NULL,
    [RABATT_DEFAULT] int NOT NULL,
    [FAX_PRO_AUFTR] int NOT NULL,
    [TEILLIEF_POS] int NOT NULL,
    [KZ_FIBU_EXP] int NOT NULL,
    [KZ_FIBU_IMP] int NOT NULL,
    [MISCH_RABATT] int NOT NULL,
    [MISCH_INDIV_PR] int NOT NULL,
    [BUILD_KMB_RAB] int NOT NULL,
    [KMB_LEVEL] int NOT NULL,
    [MIN_KANTE_ISO] int NOT NULL,
    [LAGER_STKL] int NOT NULL,
    [VPROV_REKLAMATION] int NULL,
    [RECH_MON_PKT] int NOT NULL,
    [RECH_MON_STATPKT] int NOT NULL,
    [SPACER_GEORGIENS] int NULL,
    [ISO_BESTELLARTIKEL] int NULL,
    [KREDITLIMIT] int NULL,
    [ISO_VSG_PREIS] int NULL,
    [BAR_MIN_SIZE] int NULL,
    [ALFAKVERSION] int NOT NULL,
    [REKL_EXCL_QUANTITY] int NULL,
    [GRENZSTATUS] int NOT NULL,
    [MCODE_DORMA] nvarchar(3) NULL,
    [MCODE_BOHLE] nvarchar(3) NULL,
    [MCODE_PAULI] nvarchar(3) NULL,
    [STAT_W_O_INVOICE] int NOT NULL,
    [TRANS_SSTAT_ACTIVE] int NULL,
    [TRANS_ZW_ACTIVE] int NULL,
    [MAIL_PRO_AUFTR] int NOT NULL,
    [VORLAGE_TAGE] int NOT NULL,
    [INCH_SIGN] nvarchar(1) NULL,
    [DIM_THICKNESS] int NULL,
    [WIEDERVORLAGE_KZ] nvarchar(1) NOT NULL,
    [ALTWAEHRUNG] nvarchar(8) NOT NULL,
    [CEKAL_DEF_TEXT] int NOT NULL,
    [MASSRUNDUNG] int NULL,
    [FW_DIFF] int NULL,
    [PROD_FLAG] int NULL,
    [PC_ACTIVE] int NULL,
    [LOG_DAYS] int NULL,
    [FOLGEAUFTRAEGE] int NULL,
    [BEA_NULL_RABATT] int NULL,
    [AWBAR_BATCHNR] int NULL,
    [VPROV_INTERACTIVE] int NULL,
    [DELETEDOC_WO_CHECK] int NULL,
    [RUECKREFDOK] int NULL,
    [UMSATZ_STUKL] int NULL,
    [INHERIT_VARIANTS] int NULL,
    [REPLIKATION_AKTIV] int NOT NULL,
    [EDGE_MIN_SIZE] int NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [GGA_DSN] nvarchar(40) NULL,
    [RACK_MODE] int NOT NULL,
    [RACK_MANDANT] int NOT NULL,
    [RACK_AVBEREICH] nvarchar(40) NOT NULL,
    [COMMONBASE_MANDANT] int NOT NULL,
    [COMMONBASE_GRUPPE] int NOT NULL,
    [EDI_VORHALT] int NOT NULL,
    [KAPS_FILE] nvarchar(40) NULL,
    [HISTORIE_VORHALT] int NOT NULL,
    [DEF_GUTSCH_REKLA_ORT] nvarchar(40) NOT NULL,
    [KREDITLIMIT_VORHALT] int NOT NULL,
    [DEFAULT_MIN_DB] int NOT NULL,
    [DEFAULT_MAX_DB] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [DEFAULT_LENR] nvarchar(40) NOT NULL,
    [GRENZSTATUS_WEA] int NOT NULL,
    [VORLAGE_TAGE_AUFTR] int NOT NULL,
    [VORLAGE_TAGE_GUTSCH] int NOT NULL,
    [VORLAGE_TAGE_ANFR] int NOT NULL,
    [VORLAGE_TAGE_BEST] int NOT NULL,
    [ORDERXML_VORHALT] int NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_BANK] (
    [MANDANT] int NOT NULL,
    [IBAN] nvarchar(40) NULL,
    [KTN] nvarchar(20) NOT NULL,
    [HAUPT_KZ] int NULL,
    [BANK_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_DIGITALE_SIGNATUR] (
    [ID] int NOT NULL,
    [PRIVATE_KEY] nvarchar(max) NULL,
    [PUBLIC_KEY] nvarchar(max) NULL,
    [CERTIFICATION_CODE] nvarchar(max) NULL,
    [AUDIT_FILE_VERSION] nvarchar(10) NULL,
    [TAX_ENTITY] nvarchar(20) NULL,
    [COMPANY_TAX_ID] nvarchar(20) NULL,
    [PRODUCT_ID] nvarchar(254) NULL,
    [PRODUCT_VERSION] nvarchar(30) NULL,
    [TAX_COUNTRY_REGION] nvarchar(10) NULL,
    [DEFAULT_VALUE] nvarchar(15) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_KAPA] (
    [MANDANT] int NOT NULL,
    [TAG] int NOT NULL,
    [PRDKTART] nvarchar(40) NOT NULL,
    [KAPA] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_LIEFERADR] (
    [MANDANT_ID] int NOT NULL,
    [ID] int NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [TLF4] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [FAX] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_MITARB] (
    [ID] nvarchar(40) NOT NULL,
    [ANREDE] nvarchar(40) NOT NULL,
    [NAME] nvarchar(40) NULL,
    [VORNAME] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF4] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [FUNKTION] nvarchar(40) NOT NULL,
    [DOMAIN] nvarchar(80) NULL,
    [LOGINNAME] nvarchar(80) NOT NULL,
    [DEVICE_DRUCK] nvarchar(254) NULL,
    [DRIVER_DRUCK] nvarchar(254) NULL,
    [PORT_DRUCK] nvarchar(254) NULL,
    [DEVICE_FAX] nvarchar(254) NULL,
    [DRIVER_FAX] nvarchar(254) NULL,
    [PORT_FAX] nvarchar(254) NULL,
    [DEVICE] nvarchar(40) NULL,
    [DRIVER] nvarchar(40) NULL,
    [PORT] nvarchar(40) NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PERS_NR] int NULL,
    [PASSWORD] nvarchar(64) NOT NULL,
    [EXT_CUSTOMER] int NOT NULL,
    [DATUM_VOR_AUFTR] datetime NULL,
    [DATUM_VOR_ANGEB] datetime NULL,
    [DATUM_VOR_BEST] datetime NULL,
    [CLIENT_LIMIT] int NOT NULL,
    [MANDANT] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [MENU_LANG] decimal(28,8) NOT NULL,
    [SQLUSER] nvarchar(8) NULL,
    [IS_AIS_USER] int NULL,
    [ROWID] char(36) NOT NULL,
    [GEBURTSTAG] date NULL,
    [DEVICE_PDF] nvarchar(254) NULL,
    [DRIVER_PDF] nvarchar(254) NULL,
    [PORT_PDF] nvarchar(254) NULL,
    [TERMIN] int NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_MITARB_FRM] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [FRM_ID] nvarchar(40) NOT NULL,
    [XKOORD] decimal(28,8) NULL,
    [YKOORD] decimal(28,8) NULL,
    [WIDTH] decimal(28,8) NULL,
    [HEIGHT] decimal(28,8) NULL,
    [STATE] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_MITARB_P] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [TYP] int NOT NULL,
    [PARAM] nvarchar(150) NULL,
    [DATATYPE] nvarchar(1) NOT NULL,
    [MESSAGE_ID] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_MITARB_TP] (
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABLE_ID] nvarchar(40) NOT NULL,
    [XML_DATA] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_PARAMS] (
    [ID] int NOT NULL,
    [PARAM] int NOT NULL,
    [VALUE] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FIRMA_RND] (
    [MANDANT] int NOT NULL,
    [RND_PUNKT] int NOT NULL,
    [RND_TAB] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRUPPE] nvarchar(40) NOT NULL,
    [PREIS_EINHEIT] nvarchar(20) NOT NULL,
    [KZ_NETTO_PREISE] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FORMEL] (
    [ID] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [BEM] nvarchar(50) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FORMEL_PUNKT] (
    [PUNKT] int NOT NULL,
    [FORMEL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_FORMULARE] (
    [ID] int NOT NULL,
    [PUNKTE_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ANZAHL] int NULL,
    [TEXT_KZ] nvarchar(40) NULL,
    [ID_FUSS] int NOT NULL,
    [ID_KOPF] int NOT NULL,
    [STANDARD] int NULL,
    [ATTRIB1] int NULL,
    [ATTRIB2] int NULL,
    [ATTRIB3] int NULL,
    [ATTRIB4] int NULL,
    [ATTRIB5] int NULL,
    [BEZ] nvarchar(40) NULL,
    [NAME1] nvarchar(40) NULL,
    [PROT_KZ] int NULL,
    [DRUCK_MODUS] int NULL,
    [STKL_DRUCK] int NULL,
    [MOD_DECIMAL] int NULL,
    [ABW_STATUS_ID] int NOT NULL,
    [FORM_SPRACHE] int NOT NULL,
    [FORM_AKT_SPR] int NULL,
    [SAMMELDRUCK] int NOT NULL,
    [POS_DIFF_FORM] int NOT NULL,
    [VERM_SKZ] int NOT NULL,
    [VS_BREITE] int NULL,
    [VS_HOEHE] int NULL,
    [PREISDRUCK] int NOT NULL,
    [ATTRIB6] int NULL,
    [ATTRIB7] int NULL,
    [ATTRIB8] int NULL,
    [ATTRIB9] int NULL,
    [ATTRIB10] int NULL,
    [ATTRIB11] int NULL,
    [ATTRIB12] int NULL,
    [ATTRIB13] int NULL,
    [ATTRIB14] int NULL,
    [SCALEZVMTEXT] decimal(28,8) NULL,
    [SCALESEGTEXT] decimal(28,8) NULL,
    [DMS] int NOT NULL,
    [SCALETEXTSEG] decimal(28,8) NULL,
    [SCALESHAPETEXT] decimal(28,8) NULL,
    [ATTRIB15] int NULL,
    [ATTRIB16] int NULL,
    [ATTRIB17] int NULL,
    [ATTRIB18] int NULL,
    [ATTRIB19] int NULL,
    [ATTRIB20] int NULL,
    [TEXT1] int NOT NULL,
    [TEXT2] int NOT NULL,
    [TEXT3] int NOT NULL,
    [TEXT4] int NOT NULL,
    [TEXT5] int NOT NULL,
    [TEXT6] int NOT NULL,
    [TEXT7] int NOT NULL,
    [TEXT8] int NOT NULL,
    [TEXT9] int NOT NULL,
    [TEXT10] int NOT NULL,
    [OPTIONEN] nvarchar(254) NULL,
    [FX_HEADER_PAGE] int NOT NULL,
    [FX_HEADER_FORMFEED] int NOT NULL,
    [FX_HEADER_SAMMEL] int NOT NULL,
    [FX_HEADER_GRUPPENBEZ] int NOT NULL,
    [FX_DETAIL] int NOT NULL,
    [FX_FOOTER_GRUPPENBEZ] int NOT NULL,
    [FX_FOOTER_SAMMEL] int NOT NULL,
    [FX_FOOTER_FORMFEED] int NOT NULL,
    [FX_FOOTER_PAGE] int NOT NULL,
    [VERM_SKZ_SPROSSE] int NOT NULL,
    [VS_SPR_BREITE] int NULL,
    [VS_SPR_HOEHE] int NULL,
    [VS_SPR_FONT] decimal(28,8) NOT NULL,
    [FX_FORM_BEGIN] int NOT NULL,
    [DURCHSCHLAG_ANZ] int NOT NULL,
    [DURCHSCHLAG_CLOSE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [DRUCK_STRING] nvarchar(150) NULL,
    [OVERWRITE_DIM] int NULL,
    [REPORT_GRUPPE] int NULL,
    [IQUOTE_TRANSFER] int NOT NULL,
    [MODEL_LANGUAGE] nvarchar(5) NULL,
    [DIFFERING_SENDER] nvarchar(254) NULL,
    [MAIL_SUBJECT] int NULL,
    [MAIL_BODY] int NULL,
    [DMS_PDF] nvarchar(150) NULL,
    [PDF_PATH] nvarchar(254) NULL,
    [PDF_FILE] nvarchar(254) NULL,
    [PDF_FILE_INTERACTIVE] int NULL,
    [LINKS] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[KA_FUNKTION] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_GESCHART] (
    [BEZ] nvarchar(80) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_GRENZTYP] (
    [ID] nvarchar(40) NOT NULL,
    [GRENZTYP_NR] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ISO_LANG] (
    [CODE] nvarchar(3) NOT NULL,
    [INT_NAME] nvarchar(254) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KALENDER] (
    [ID] date NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [TAG] int NOT NULL,
    [STUNDEN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KATEGORIE] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [TYP] int NOT NULL,
    [VORGABE] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KLASSI_DEF] (
    [ID] int NOT NULL,
    [K_BEZ] nvarchar(40) NOT NULL,
    [K_BEM] nvarchar(40) NULL,
    [K_ART] int NOT NULL,
    [K_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KLASSI_STRING] (
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KOSTENART] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KOSTENSTELLE] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_KUNDENGRPN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAG_LEVEL1] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAG_LEVEL2] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAG_LEVEL3] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAG_LEVEL4] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAGBEWTYP] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAGER_DEF] (
    [ID] int NOT NULL,
    [LEVEL1] nvarchar(40) NOT NULL,
    [LEVEL2] nvarchar(40) NOT NULL,
    [LEVEL4] nvarchar(40) NOT NULL,
    [LEVEL3] nvarchar(40) NOT NULL,
    [LAG_ROHMAT] int NOT NULL,
    [LAG_MULTI] int NOT NULL,
    [LOCKED] int NOT NULL,
    [INPROD] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL,
    [LAG_ART] int NOT NULL,
    [LAG_EINGANG] int NOT NULL,
    [BEZ_ZUSCHNITT] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[KA_LAGER_TEXTE] (
    [ID] int NOT NULL,
    [TEXT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAGERORTE] (
    [BEZ] nvarchar(40) NOT NULL,
    [BEM] nvarchar(254) NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LAND] (
    [LAND_KZ] nvarchar(6) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [RUNDWERT_ID] int NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [KENNZ] nvarchar(2) NULL,
    [AREA] int NOT NULL
);

CREATE TABLE SYSADM.[KA_LAND_KENNZ] (
    [KENNZ] nvarchar(2) NOT NULL,
    [NAME] nvarchar(254) NULL,
    [INT_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LIEFERBED] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LIEFGRPN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LIEFTERM_GRND] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [KZ_LIEFERANT] int NOT NULL
);

CREATE TABLE SYSADM.[KA_LIEFTERM_GRND_SGG] (
    [BEZ] nvarchar(40) NOT NULL,
    [BEZ2] nvarchar(40) NOT NULL,
    [NUMMER] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_LKW] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [LKW_TYP] nvarchar(40) NULL,
    [ANHAENGER] int NOT NULL,
    [BAUJAHR] date NOT NULL,
    [FAHRER] nvarchar(40) NOT NULL,
    [BEIFAHRER] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [LEER_GEWICHT] decimal(28,8) NOT NULL,
    [GES_GEWICHT] decimal(28,8) NOT NULL,
    [LADEFLAECHE] decimal(28,8) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MAHNTEXT] (
    [ID] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MENU] (
    [ID] int NOT NULL,
    [PROG_ID] int NOT NULL,
    [MODUL] nvarchar(40) NULL,
    [BEZ] nvarchar(60) NULL,
    [ICON] nvarchar(40) NULL,
    [LEVEL] int NOT NULL,
    [PARENT] int NOT NULL,
    [FOLGE] int NOT NULL,
    [LSM_CONSTANT] int NOT NULL,
    [EDITION] nvarchar(10) NULL,
    [LANGUAGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MITARB_GRUPPE] (
    [BEZ] nvarchar(40) NOT NULL,
    [ID] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MONATE] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MWST] (
    [ID] int NOT NULL,
    [BEM] nvarchar(40) NULL,
    [ERLKTODEB] nvarchar(20) NULL,
    [ERLKTOKRE] nvarchar(20) NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [MWST] decimal(28,8) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_MWST_MATRIX] (
    [STEUER] int NOT NULL,
    [MWST_ID] int NOT NULL,
    [GRENZE1] nvarchar(60) NOT NULL,
    [GRENZE2] nvarchar(60) NOT NULL,
    [GRENZE3] nvarchar(60) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_OBJEKT] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PARTNERGRPN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_POOL_GLASARTGRP] (
    [BEZ] nvarchar(40) NOT NULL,
    [GRP_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_POSTAL] (
    [POSTAL_CODE] nvarchar(11) NOT NULL,
    [TOWN] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [COUNTRY] nvarchar(6) NULL,
    [ROUTE] nvarchar(40) NULL
);

CREATE TABLE SYSADM.[KA_PRINT_DEF] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(254) NOT NULL,
    [DOK_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PRINT_DETAIL] (
    [ID] int NOT NULL,
    [SEQ_NR] int NOT NULL,
    [DEVICE] nvarchar(254) NOT NULL,
    [DRIVER] nvarchar(254) NOT NULL,
    [PORT] nvarchar(254) NOT NULL,
    [OPTIONS] nvarchar(254) NULL,
    [FORMULAR] int NOT NULL,
    [MANDANT] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [ASK_FOR_DATE] int NOT NULL,
    [ASK_FOR_ARCHIVE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PRIORITAET] (
    [BEZ] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [NUMMER] int NOT NULL,
    [PRIORITAET] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PRMEEINH] (
    [BEZ] nvarchar(20) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [BEM] nvarchar(40) NULL,
    [PRME_ID] int NOT NULL,
    [MASS_SYS] int NOT NULL,
    [FREMD_KEY] nvarchar(40) NULL,
    [GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PROD_BRUCH] (
    [STATUS_ID] int NOT NULL,
    [REKLA_GRND] nvarchar(40) NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [KZ_KOSTENLOS] int NULL,
    [ROWID] char(36) NOT NULL,
    [KZ_LOGISTIC] int NOT NULL
);

CREATE TABLE SYSADM.[KA_PRODPOINT_PRODART] (
    [ERF_NR] int NOT NULL,
    [PRD_BEZ] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PRODUKTART] (
    [BEZ] nvarchar(40) NOT NULL,
    [PRD_NR] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PRODUKTGRP] (
    [PRDKTART_ID] nvarchar(40) NOT NULL,
    [PRDKTGRP_ID] nvarchar(40) NOT NULL,
    [PRDKTGRP_NR] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [PRDKTGRP_ALCIM] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PROG_PUNKTE] (
    [PROG_PKT] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [BIT] int NOT NULL,
    [PROG_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PROGRAMME] (
    [BEZ] nvarchar(80) NOT NULL,
    [ID] int NOT NULL,
    [KOORDINATEN] nvarchar(18) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PROJEKT] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_PROVINZ] (
    [BEZ] nvarchar(20) NOT NULL,
    [MWST] int NOT NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ICMS_ID_FINAL] int NOT NULL,
    [ICMS_ID_DIST] int NOT NULL,
    [ICMS_ID_MANU] int NOT NULL,
    [ICMS_ST] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RECENT] (
    [USER_ID] nvarchar(40) NOT NULL,
    [PROG_ID] int NOT NULL,
    [PROG_TITEL] nvarchar(450) NULL,
    [WHERE_KLAUSEL] nvarchar(450) NULL,
    [LETZTER_AUFRUF ] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RECHTE_GRUPPE] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [PROGRAMM] int NOT NULL,
    [PROG_PKT1] int NOT NULL,
    [PROG_PKT2] int NOT NULL,
    [PROG_PKT3] int NOT NULL,
    [PROG_PKT4] int NOT NULL,
    [PROG_PKT5] int NOT NULL,
    [PROG_PKT6] int NOT NULL,
    [PROG_PKT7] int NOT NULL,
    [PROG_PKT8] int NOT NULL,
    [PROG_PKT9] int NOT NULL,
    [PROG_PKT10] int NOT NULL,
    [PROG_PKT11] int NOT NULL,
    [PROG_PKT12] int NOT NULL,
    [PROG_PKT13] int NOT NULL,
    [PROG_PKT14] int NOT NULL,
    [PROG_PKT15] int NOT NULL,
    [PROG_PKT16] int NOT NULL,
    [PROG_PKT17] int NOT NULL,
    [PROG_PKT18] int NOT NULL,
    [PROG_PKT19] int NOT NULL,
    [PROG_PKT20] int NOT NULL,
    [PROG_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RECHTE_MITARB] (
    [MITARB] nvarchar(40) NOT NULL,
    [PROGRAMM] int NOT NULL,
    [PROG_PKT1] int NOT NULL,
    [PROG_PKT2] int NOT NULL,
    [PROG_PKT3] int NOT NULL,
    [PROG_PKT4] int NOT NULL,
    [PROG_PKT5] int NOT NULL,
    [PROG_PKT6] int NOT NULL,
    [PROG_PKT7] int NOT NULL,
    [PROG_PKT8] int NOT NULL,
    [PROG_PKT9] int NOT NULL,
    [PROG_PKT10] int NOT NULL,
    [PROG_PKT11] int NOT NULL,
    [PROG_PKT12] int NOT NULL,
    [PROG_PKT13] int NOT NULL,
    [PROG_PKT14] int NOT NULL,
    [PROG_PKT15] int NOT NULL,
    [PROG_PKT16] int NOT NULL,
    [PROG_PKT17] int NOT NULL,
    [PROG_PKT18] int NOT NULL,
    [PROG_PKT19] int NOT NULL,
    [PROG_PKT20] int NOT NULL,
    [PROG_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_REKLA_GRND] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [KMB_WGR] nvarchar(3) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [FLAG1] int NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_REKLA_ORT] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_REP_KREISE] (
    [TABELLE] nvarchar(40) NOT NULL,
    [LETZTE] int NOT NULL,
    [BIS] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RND_PUNKTE] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(80) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RND_TAB] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [RND_WERTE] int NOT NULL,
    [RND_STELLEN] int NOT NULL,
    [RND_ART] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_RND_WERTE] (
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_SORT_SCHLS] (
    [ID] int NOT NULL,
    [SORT_BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_SPERRKZ] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [STATUS] int NOT NULL,
    [STATUS_PKT_UML] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_SPRACHEN] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL,
    [ISO_KZ] nvarchar(3) NOT NULL
);

CREATE TABLE SYSADM.[KA_SPROSSTYP] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [KZ_DURCHGANG] int NOT NULL,
    [MASS_RAND] decimal(28,8) NOT NULL,
    [FRTIEFE] decimal(28,8) NOT NULL,
    [SPEZIALKREUZ] int NOT NULL,
    [BREITEKREUZ] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_STATUS] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_STATUS_GRUPPE] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [DOK_TYP] int NOT NULL,
    [STATUS1_VON] int NOT NULL,
    [STATUS1_BIS] int NOT NULL,
    [STATUS2_VON] int NOT NULL,
    [STATUS2_BIS] int NOT NULL,
    [STATUS3_VON] int NOT NULL,
    [STATUS3_BIS] int NOT NULL,
    [STATUS4_VON] int NOT NULL,
    [STATUS4_BIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_STATUS_MITARB] (
    [MITARB] nvarchar(40) NOT NULL,
    [DOK_TYP] int NOT NULL,
    [STATUS1_VON] int NOT NULL,
    [STATUS1_BIS] int NOT NULL,
    [STATUS2_VON] int NOT NULL,
    [STATUS2_BIS] int NOT NULL,
    [STATUS3_VON] int NOT NULL,
    [STATUS3_BIS] int NOT NULL,
    [STATUS4_VON] int NOT NULL,
    [STATUS4_BIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_STATUS_ZUORD] (
    [PUNKT_ID] int NOT NULL,
    [STATUS_ID] int NOT NULL,
    [MIN_STATUS] int NOT NULL,
    [SPERR_STATUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_STATUSPUNKTE] (
    [ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NAME] nvarchar(50) NULL,
    [ROWID] char(36) NOT NULL,
    [WEBKONFIG_FLAG] int NOT NULL
);

CREATE TABLE SYSADM.[KA_STRUKTVERLAUF] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [KURZ_BEZ] nvarchar(1) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_SUBFAMILY] (
    [PRODUKT] int NOT NULL,
    [SUBFAMILY] nvarchar(35) NULL,
    [SUBFAMILYCODE] nvarchar(2) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_SYSTEXTE] (
    [ID] int NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [BEZ] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAGE] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX] (
    [PLZ_ID] nvarchar(11) NOT NULL,
    [STEUER_ID] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_BUSINESS] (
    [TYPE] int NOT NULL,
    [ID] nvarchar(80) NOT NULL,
    [PRODUCTION] int NULL,
    [PAYMENT] int NULL,
    [CREDIT_LIMIT] int NULL,
    [STATISTICS] int NULL,
    [COPY_VALOR_UNITARIO] int NOT NULL,
    [STANDARD] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_BUSINESS_LINK] (
    [TYPE] int NOT NULL,
    [ID] nvarchar(80) NOT NULL,
    [TARGET_ID] nvarchar(80) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_CFOP] (
    [ID] nvarchar(4) NOT NULL,
    [DESCR] nvarchar(160) NULL,
    [MODE] int NOT NULL,
    [NUMMER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_CST] (
    [ID] nvarchar(3) NOT NULL,
    [DESCR] nvarchar(160) NULL,
    [NUMMER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_GENERATOR] (
    [CUSTOMER_TYPE] int NOT NULL,
    [ID] int NOT NULL,
    [ICMS_CONTR] int NOT NULL,
    [STATE_REGISTER] int NOT NULL,
    [ICMS_ST] int NOT NULL,
    [STATE] int NOT NULL,
    [PRODUCT_TYPE] int NOT NULL,
    [RES_PIS] int NOT NULL,
    [RES_CONFIS] int NOT NULL,
    [RES_IPI] int NOT NULL,
    [RES_ICMS] int NOT NULL,
    [RES_CST] nvarchar(3) NOT NULL,
    [RES_CFOP] nvarchar(4) NOT NULL,
    [RES_ICMS_CALC_MODE] int NOT NULL,
    [REMARK] nvarchar(160) NULL,
    [COUNTRY] int NOT NULL,
    [BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [DESCR] nvarchar(max) NULL,
    [LOCKED] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_ICMS_MODE] (
    [ID] int NOT NULL,
    [DESCR] nvarchar(160) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_OPERATION] (
    [ID] int NOT NULL,
    [DESCR] nvarchar(160) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_PRODUCT_TYPE] (
    [ID] int NOT NULL,
    [DESCR] nvarchar(160) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TAX_REDUCTION] (
    [ID] int NOT NULL,
    [PRODUCT] int NOT NULL,
    [PGR] nvarchar(3) NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [CUSTOMER] int NOT NULL,
    [DATE_FROM] date NULL,
    [DATE_TO] date NULL,
    [ICMS_ID] int NOT NULL,
    [LOCKED] int NOT NULL,
    [TEXT_NFE] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TEXT_KZ] (
    [ID] nvarchar(1) NOT NULL,
    [TEXT_BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TEXTE] (
    [ID] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [BEZ] nvarchar(max) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TIPI] (
    [NCM] nvarchar(40) NOT NULL,
    [DESCR] nvarchar(254) NULL,
    [IPI_ID] int NOT NULL,
    [IPI_IMPORT_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TIPI_IVA] (
    [NCM] nvarchar(40) NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [IVA] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TIPI_PGR] (
    [WGR] nvarchar(3) NOT NULL,
    [NCM] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_TOUREN] (
    [BEZ] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NULL,
    [TAG1] int NULL,
    [TAG2] int NULL,
    [TAG3] int NULL,
    [TAG4] int NULL,
    [TAG5] int NULL,
    [TAG6] int NULL,
    [TAG7] int NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [TOUR_NR] nvarchar(20) NULL,
    [KZ_KOSTENLOS] int NULL,
    [SHIFT1] int NULL,
    [SHIFT2] int NULL,
    [SHIFT3] int NULL,
    [SHIFT4] int NULL,
    [SHIFT5] int NULL,
    [SHIFT6] int NULL,
    [SHIFT7] int NULL,
    [ABFAHRT1] datetime NULL,
    [ABFAHRT2] datetime NULL,
    [ABFAHRT3] datetime NULL,
    [ABFAHRT4] datetime NULL,
    [ABFAHRT5] datetime NULL,
    [ABFAHRT6] datetime NULL,
    [ABFAHRT7] datetime NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KOSTEN_PRO_KM] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [DELIVERY_DURATION] int NOT NULL,
    [CALENDAR_WEEK] int NOT NULL,
    [WEEK_SERIES] int NOT NULL
);

CREATE TABLE SYSADM.[KA_USER_MENU] (
    [PROG_ID] int NOT NULL,
    [USER_ID] nvarchar(40) NOT NULL,
    [INVISIBLE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_VERPACKUNGEN] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_VORGANG] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WAEHRUNGEN] (
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [VALUTADATUM] date NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [NK_STELLEN] int NULL,
    [FREMD_KEY2] nvarchar(40) NOT NULL,
    [FW_FELD] int NOT NULL,
    [VALUTAKURS] decimal(28,8) NULL,
    [TRIANGULATION] decimal(28,8) NULL,
    [EUROKURS] decimal(28,8) NULL,
    [KENNZ] nvarchar(3) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WAEHRUNGEN_KENNZ] (
    [KENNZ] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WERTBER] (
    [KATEGORIE] nvarchar(6) NOT NULL,
    [NUMMER] int NULL,
    [BERICHTIGUNG] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WGR] (
    [ID] nvarchar(3) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [BITMAP] nvarchar(20) NULL,
    [MWST_KZ1] int NULL,
    [MWST_UML1] int NULL,
    [MWST_KZ2] int NULL,
    [MWST_UML2] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [VORL_TAGE] int NOT NULL,
    [HK_SONZU] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [DEFAULT_LIEFERZEIT] int NOT NULL,
    [TAX_CODE] nvarchar(25) NULL
);

CREATE TABLE SYSADM.[KA_WGR_GRPBEZ] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WGR_GRUPPEN] (
    [GRP_ID] int NOT NULL,
    [WGR_ID] nvarchar(3) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WGRKMB] (
    [ID] nvarchar(3) NOT NULL,
    [ID1] nvarchar(3) NOT NULL,
    [KOMBIWGR] nvarchar(3) NULL,
    [BEZ] nvarchar(40) NULL,
    [KZ_RABATT] int NULL,
    [KZ_STATISTIK] int NULL,
    [KZ_EINKAUF] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_ONLYBASIS] int NOT NULL,
    [KZ_LOGO] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_WORKFLOW] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(80) NULL,
    [FORMEL_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_XOPT_TISCH] (
    [TISCH_ID] nvarchar(4) NOT NULL,
    [BESCHREIBUNG] nvarchar(30) NOT NULL,
    [TISCH_LAENGE] int NOT NULL,
    [TISCH_HOEHE] int NOT NULL,
    [MIN_Y_Y] int NOT NULL,
    [MIN_X_X] int NOT NULL,
    [MAX_X_X] int NOT NULL,
    [AUTO_BRECHEN] nvarchar(1) NOT NULL,
    [AUTO_AUFLEGER] nvarchar(1) NOT NULL,
    [FORMEN] nvarchar(1) NOT NULL,
    [VSG] nvarchar(1) NOT NULL,
    [ENTSCHICHTEN] nvarchar(1) NOT NULL,
    [REF] int NOT NULL,
    [ZSCH] int NOT NULL,
    [BREAKDIR] int NOT NULL,
    [BREAKSTRT] int NOT NULL,
    [HANDZUSCHNITT] int NOT NULL,
    [SPEZIAL] int NOT NULL,
    [BOECKE_PRO_ZYKLUS] int NOT NULL,
    [RANG] int NOT NULL,
    [MAX_Y_Y] int NOT NULL,
    [MAX_WASTE_X] int NOT NULL,
    [MAX_WASTE_Y] int NOT NULL,
    [MAX_WASTE_Z] int NOT NULL,
    [AUTOTRAV] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ZAHLBED] (
    [BEZ] nvarchar(100) NOT NULL,
    [SKONTOTAGE1] int NULL,
    [SKONTOTAGE2] int NULL,
    [SKONTOTAGE3] int NULL,
    [BRUTTOTAGE] int NULL,
    [MAHNTAGE] int NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [SKONTOSATZ1] decimal(28,8) NULL,
    [SKONTOSATZ2] decimal(28,8) NULL,
    [SKONTOSATZ3] decimal(28,8) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [BRUTTOTAGE2] int NULL,
    [BRUTTOTAGE3] int NULL,
    [BRUTTOTAGE4] int NULL,
    [BRUTTOTAGE5] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ZAHLWEG] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(20) NULL,
    [FREMD_KEY2] nvarchar(20) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ZOLLNR_PROD] (
    [PROD_ID] int NOT NULL,
    [ZOLLNR_EXPORT] nvarchar(15) NOT NULL,
    [TEXT_EXPORT] nvarchar(40) NULL,
    [FW_CODE] nvarchar(8) NOT NULL,
    [ZOLLNR_IMPORT] nvarchar(15) NOT NULL,
    [TEXT_IMPORT] nvarchar(40) NULL,
    [DRITTLAND] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ZOLLNR_WGR] (
    [WGR] nvarchar(3) NOT NULL,
    [ZOLLNR_EXPORT] nvarchar(15) NOT NULL,
    [TEXT_EXPORT] nvarchar(40) NULL,
    [FW_CODE] nvarchar(8) NOT NULL,
    [ZOLLNR_IMPORT] nvarchar(15) NOT NULL,
    [TEXT_IMPORT] nvarchar(40) NULL,
    [DRITTLAND] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KA_ZOLLTOUR] (
    [BEZ] nvarchar(40) NOT NULL,
    [NUMMER] int NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KAMA_KLASSI_DEF] (
    [ID] int NOT NULL,
    [K_BEZ] nvarchar(40) NOT NULL,
    [K_BEM] nvarchar(40) NULL,
    [K_ART] int NOT NULL,
    [K_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KAMA_KLASSI_STRING] (
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_ATTACH] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_DOCK] (
    [ID] int NOT NULL,
    [DOCK_ID] int NOT NULL,
    [DOCK_TEXT] nvarchar(80) NULL,
    [VORGABE] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KALENDER] (
    [ID] int NOT NULL,
    [VON] datetime NOT NULL,
    [BIS] datetime NOT NULL,
    [JAHR] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KG_DECKUNG] (
    [GRUPPE] nvarchar(40) NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [MIN_DB] decimal(28,8) NOT NULL,
    [MAX_DB] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KG_RND] (
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [RND_PUNKT] int NOT NULL,
    [RND_TAB] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRUPPE] nvarchar(40) NOT NULL,
    [PREIS_EINHEIT] nvarchar(20) NOT NULL,
    [KZ_NETTO_PREISE] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KLASSI_WERTE] (
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KOMM] (
    [ID] int NOT NULL,
    [KOMM_ID] int NOT NULL,
    [KOMM_TEXT] nvarchar(80) NULL,
    [VORGABE] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KREDITLIMIT] (
    [ID] int NOT NULL,
    [DATUM_ERSTELLUNG] date NOT NULL,
    [KREDITLIMIT] decimal(28,8) NULL,
    [KREDITLIMIT1] decimal(28,8) NULL,
    [SALDO_J] decimal(28,8) NULL,
    [OBLIGO] decimal(28,8) NULL,
    [SUM_NGEL] decimal(28,8) NOT NULL,
    [SUM_GEL_NBER] decimal(28,8) NOT NULL,
    [SUM_BER_NFIBU] decimal(28,8) NOT NULL,
    [KUNDE_GESPERRT] int NULL,
    [VERTRETER] nvarchar(40) NULL,
    [UMSATZ_LETZTE_12_MONATE] decimal(28,8) NULL,
    [ZAHLUNGSBEDINGUNGEN] nvarchar(100) NULL,
    [ROWID] char(36) NOT NULL,
    [EULER_ID] int NULL,
    [CHANGETIME_EH] datetime NULL,
    [CREDITLIMIT_EH] decimal(28,8) NULL,
    [CREDIT_TURN_EH] decimal(28,8) NULL,
    [CREDIT_EH] decimal(28,8) NULL,
    [CAP_PLUS_EH] decimal(28,8) NULL,
    [MANUAL_EH] decimal(28,8) NULL,
    [MANUAL_DATE_EH] date NULL,
    [BOSS_EH] decimal(28,8) NULL,
    [BOSS_DATE_EH] date NULL
);

CREATE TABLE SYSADM.[KU_KU_DECKUNG] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [MIN_DB] decimal(28,8) NOT NULL,
    [MAX_DB] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KU_OBJEKTE] (
    [KUNDEN_NR] int NOT NULL,
    [OBJEKT_ID] int NOT NULL,
    [VON] date NULL,
    [BIS] date NULL,
    [SITEADDR1] nvarchar(40) NULL,
    [SITEADDR2] nvarchar(40) NULL,
    [SITEADDR3] nvarchar(40) NULL,
    [INFO] nvarchar(max) NULL,
    [MAX_PREIS] decimal(28,8) NULL,
    [MAX_QM] decimal(28,8) NULL,
    [MAX_STUECK] decimal(28,8) NULL,
    [SITEADDR4] nvarchar(40) NULL,
    [CONTACT] nvarchar(40) NULL,
    [PHONE] nvarchar(40) NULL,
    [SUPERVISOR] nvarchar(40) NOT NULL,
    [DATE_ADD] date NULL,
    [QUOTE] nvarchar(20) NULL,
    [FOLGE] nvarchar(40) NULL,
    [VARIATION1] decimal(28,8) NOT NULL,
    [VARIATION2] decimal(28,8) NOT NULL,
    [VARIATION3] decimal(28,8) NOT NULL,
    [VARIATION4] decimal(28,8) NOT NULL,
    [VARIATION5] decimal(28,8) NOT NULL,
    [CLAIMED_SUM] decimal(28,8) NOT NULL,
    [CONTRACT_SUM] decimal(28,8) NOT NULL,
    [LAST_CLAIM] int NOT NULL,
    [EST_PURCH] decimal(28,8) NOT NULL,
    [REAL_PURCH] decimal(28,8) NOT NULL,
    [CLAIMED_PURCH] decimal(28,8) NOT NULL,
    [EST_HOURS] decimal(28,8) NOT NULL,
    [REAL_HOURS] decimal(28,8) NOT NULL,
    [RATE] decimal(28,8) NOT NULL,
    [ALLOWANCE] decimal(28,8) NOT NULL,
    [ABW_ADIENST] nvarchar(40) NOT NULL,
    [ABW_ZAHLBED] nvarchar(100) NOT NULL,
    [STATUS] int NOT NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [POSTFACH] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MANDANT] int NOT NULL,
    [TOUREN_RANGFOLGE] int NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NOT NULL,
    [ZAHLWEG] nvarchar(40) NOT NULL,
    [LIEFERBED] nvarchar(40) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KZ_ABGESCHLOSSEN] int NOT NULL,
    [CONTRACT_ORDER] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [SITESTREET] nvarchar(40) NULL,
    [SITEZIP] nvarchar(11) NULL,
    [SITECOUNTRY] nvarchar(6) NOT NULL,
    [SITEPROVINCE] nvarchar(20) NOT NULL,
    [SITETITLE] nvarchar(40) NOT NULL,
    [SITEDISTANCE] decimal(28,8) NOT NULL,
    [TRANSFER_ORDERENTRY] int NOT NULL,
    [USE_IQUOTE] int NOT NULL
);

CREATE TABLE SYSADM.[KU_KU_RND] (
    [KUNDEN_NR] int NOT NULL,
    [RND_PUNKT] int NOT NULL,
    [RND_TAB] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRUPPE] nvarchar(40) NOT NULL,
    [PREIS_EINHEIT] nvarchar(20) NOT NULL,
    [KZ_NETTO_PREISE] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN] (
    [ID] int NOT NULL,
    [MANDANT] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [EXTERNE_KNR] nvarchar(20) NULL,
    [ADRESS_KOPF] nvarchar(40) NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [PLZ_POSTFACH] nvarchar(11) NULL,
    [POSTFACH] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [KURZINFO] nvarchar(max) NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [ZAHLBED] nvarchar(100) NOT NULL,
    [ZAHLWEG] nvarchar(40) NOT NULL,
    [ZAHL_FB_KZ] int NOT NULL,
    [ZAHL_RLEG_TAG] int NULL,
    [ZAHL_TAG1] int NOT NULL,
    [LIEFERBED] nvarchar(40) NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [SACHBEARB] nvarchar(40) NOT NULL,
    [BONITAET] nvarchar(40) NOT NULL,
    [RANDABZUG] decimal(28,8) NULL,
    [ANLIEF_WERT] decimal(28,8) NOT NULL,
    [AUTO_ZUSCHL_WERT] decimal(28,8) NOT NULL,
    [HK_SONZU] decimal(28,8) NOT NULL,
    [FS_VORG_DOK_NR] int NOT NULL,
    [OP_DATUM] date NULL,
    [KONTO] nvarchar(20) NULL,
    [SAMMEL_KTO] nvarchar(20) NULL,
    [UST_ID] nvarchar(30) NULL,
    [KZ_SPRACH] int NOT NULL,
    [KZ_PRIVAT] int NULL,
    [KZ_SAMMELRE] int NULL,
    [KZ_TEILFAK] int NULL,
    [KZ_TEILLIEF] int NULL,
    [OP_TAGE] int NOT NULL,
    [KREDITMITFILIALE] int NOT NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KZ_KLEINGLAS] int NULL,
    [KZ_KREDITPRUEF] int NULL,
    [KZ_POSTFACH] int NULL,
    [KZ_STATUS] int NULL,
    [KZ_MASSEINH] int NOT NULL,
    [KZ_KINFO] int NULL,
    [KZ_MAHNSPERRE] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KZ_BANKEINZ] int NULL,
    [KZ_NETTOPREISE] int NULL,
    [KZ_DEF_FIL] int NULL,
    [KZ_FAXVERSAND] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [STRUKT_SEITE] int NOT NULL,
    [IBAN] nvarchar(40) NULL,
    [MAHN_TEXT] nvarchar(40) NOT NULL,
    [ZAHL_BLZ] nvarchar(20) NULL,
    [ZAHL_KTN] nvarchar(20) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [EIL_ZUSCHL_TAGE] int NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [MWST_KZ1] int NOT NULL,
    [MWST_KZ2] int NOT NULL,
    [HAUPT_ID] int NOT NULL,
    [KZ_FIBU_MANDANT] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNGBR] int NOT NULL,
    [RUNDUNGHOE] int NOT NULL,
    [DFUE_ART] int NOT NULL,
    [DFUE_LOG] int NOT NULL,
    [DFUE_DATEI] nvarchar(80) NULL,
    [DFUE_BATCH_DATEI] nvarchar(max) NULL,
    [MOD_SYM] int NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [ZAHL_TAG2] int NOT NULL,
    [ZAHL_TAG3] int NOT NULL,
    [SORT_KRIT] int NOT NULL,
    [SKONTO_BERTYP] int NOT NULL,
    [DFUE_LOG_TIMEOUT] int NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [ZWANG_REF] int NOT NULL,
    [FS_AUFTR_REJECT] int NOT NULL,
    [DFUE_SAVE] nvarchar(80) NULL,
    [DFUE_PROTOKOLL] nvarchar(80) NULL,
    [FS_RESTRICT] int NULL,
    [PACKREGEL] int NOT NULL,
    [PMG_DEF] int NOT NULL,
    [FS_EXCHANGE] int NOT NULL,
    [FILIALNR] int NULL,
    [FS_STL] int NULL,
    [KURS_FIX] int NOT NULL,
    [VERPACKUNG] nvarchar(40) NOT NULL,
    [PREISDRUCK] int NOT NULL,
    [PIN] int NULL,
    [EDI_FILE_FROM] int NOT NULL,
    [EDI_FILE_TO] int NOT NULL,
    [EDI_FILE_ACT] int NOT NULL,
    [KZ_DIVERS] int NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FW_ART] int NOT NULL,
    [FS_PRICING_ALLOWED] int NULL,
    [FS_FREE_ALLOWED] int NULL,
    [TEMP] int NULL,
    [AUTO_ZUSCHL_KZ] int NOT NULL,
    [AUTO_ZUSCHL_DATE] date NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [RECH_FAX] nvarchar(40) NULL,
    [FS_CHANGE_ISO] int NOT NULL,
    [BRUTTOTAG2] int NOT NULL,
    [BRUTTOTAG3] int NOT NULL,
    [BRUTTOTAG4] int NOT NULL,
    [BRUTTOTAG5] int NOT NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [ADIENST2] nvarchar(40) NOT NULL,
    [SALDO_J] decimal(28,8) NULL,
    [KREDIT_LIMIT] decimal(28,8) NULL,
    [KREDIT_LIMIT1] decimal(28,8) NULL,
    [OBLIGO] decimal(28,8) NULL,
    [SPRACH_BASIS] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [RE_DATEI] nvarchar(80) NULL,
    [RE_BATCH_DATEI] nvarchar(max) NULL,
    [RE_PROTOKOLL] nvarchar(80) NULL,
    [RE_SAVE] nvarchar(80) NULL,
    [RE_ART] int NULL,
    [STEUERMODUS] int NOT NULL,
    [RE_FILE_FROM] int NOT NULL,
    [RE_FILE_ACT] int NOT NULL,
    [RE_FILE_TO] int NOT NULL,
    [FS_MAIN_REF] int NOT NULL,
    [CE_FLAG] int NOT NULL,
    [EDI_FORMEL] int NOT NULL,
    [FX_EDI] int NOT NULL,
    [FX_CUSTOMER] int NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [EDI_DIM_FRACTION] int NULL,
    [OT_EXPORT_ART] int NULL,
    [OT_EXPORT_FLAGS] int NULL,
    [OT_WEBSERV_ADRESSE] nvarchar(80) NULL,
    [OT_WEBSERV_BENUTZER] nvarchar(40) NULL,
    [OT_WEBSERV_PASSWORT] nvarchar(40) NULL,
    [OT_WEBSERV_GRUPPE] int NULL,
    [OT_WEBSERV_MANDANT] int NULL,
    [OT_ALT_MAILADRESSE_RECHN] nvarchar(40) NULL,
    [HOMEPAGE] nvarchar(254) NULL,
    [MWST_KZ3] int NOT NULL,
    [MWST_KZ4] int NOT NULL,
    [MWST_KZ5] int NOT NULL,
    [UMSATZ_VERTR2] decimal(28,8) NOT NULL,
    [OT_ALT_MAILADRESSE_AB] nvarchar(40) NULL,
    [KZ_STEUERFLAG] int NULL,
    [OT_ALT_MAILADRESSE_LIEFS] nvarchar(40) NULL,
    [ZAHLUNGSTAGVERSCHIEBUNG] int NULL,
    [BANK_ID] int NOT NULL,
    [KZ_ICMS_CONTR] int NULL,
    [KZ_STATE_REGISTER] int NULL,
    [KZ_ICMS_ST] int NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NOT NULL,
    [MINMENGE_VK] decimal(28,8) NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [FS_ESG_EDGE] int NOT NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_LIEF] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [FS_CHECK_BEAPARAM] int NOT NULL,
    [FS_INDIVIDUAL_TEXTS] int NOT NULL,
    [OT_ALT_MAILADRESSE_GUT] nvarchar(40) NULL,
    [FS_NO_SURCHARGE_CALC] int NOT NULL,
    [DFUE_SINGLE] int NULL,
    [DFUE_BARCODE] int NULL,
    [ZUSCHLAG2_BIT] int NOT NULL,
    [MAIL_MODE] int NOT NULL,
    [OT_DATEI_ERW] int NOT NULL,
    [OT_EXCLUDE_PROD_AB] nvarchar(254) NULL,
    [KREDIT_LIMIT_NET] float NULL,
    [KREDIT_LIMIT1_NET] float NULL,
    [EULER_ID] int NULL,
    [FS_NO_SHAPE_DETECTION] int NULL,
    [BUSINESSCASE] int NOT NULL,
    [IQUOTE_CHARTER] int NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN_FORM] (
    [KUNDEN_ID] int NOT NULL,
    [ID] int NOT NULL,
    [PUNKTE_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ANZAHL] int NULL,
    [TEXT_KZ] nvarchar(40) NULL,
    [ID_FUSS] int NOT NULL,
    [ID_KOPF] int NOT NULL,
    [STANDARD] int NULL,
    [ATTRIB1] int NULL,
    [ATTRIB2] int NULL,
    [ATTRIB3] int NULL,
    [ATTRIB4] int NULL,
    [ATTRIB5] int NULL,
    [BEZ] nvarchar(40) NULL,
    [NAME1] nvarchar(40) NULL,
    [PROT_KZ] int NULL,
    [DRUCK_MODUS] int NULL,
    [STKL_DRUCK] int NULL,
    [MOD_DECIMAL] int NULL,
    [ABW_STATUS_ID] int NOT NULL,
    [VERM_SKZ] int NOT NULL,
    [VS_BREITE] int NULL,
    [VS_HOEHE] int NULL,
    [PREISDRUCK] int NOT NULL,
    [ATTRIB6] int NULL,
    [ATTRIB7] int NULL,
    [ATTRIB8] int NULL,
    [ATTRIB9] int NULL,
    [ATTRIB10] int NULL,
    [SCALEZVMTEXT] decimal(28,8) NULL,
    [SCALESEGTEXT] decimal(28,8) NULL,
    [SCALETEXTSEG] decimal(28,8) NULL,
    [SCALESHAPETEXT] decimal(28,8) NULL,
    [TEXT1] int NOT NULL,
    [TEXT2] int NOT NULL,
    [TEXT3] int NOT NULL,
    [TEXT4] int NOT NULL,
    [TEXT5] int NOT NULL,
    [TEXT6] int NOT NULL,
    [TEXT7] int NOT NULL,
    [TEXT8] int NOT NULL,
    [TEXT9] int NOT NULL,
    [TEXT10] int NOT NULL,
    [ATTRIB11] int NULL,
    [ATTRIB12] int NULL,
    [ATTRIB13] int NULL,
    [ATTRIB14] int NULL,
    [ATTRIB15] int NULL,
    [ATTRIB16] int NULL,
    [ATTRIB17] int NULL,
    [ATTRIB18] int NULL,
    [ATTRIB19] int NULL,
    [ATTRIB20] int NULL,
    [OPTIONEN] nvarchar(254) NULL,
    [FX_HEADER_PAGE] int NOT NULL,
    [FX_HEADER_FORMFEED] int NOT NULL,
    [FX_HEADER_SAMMEL] int NOT NULL,
    [FX_HEADER_GRUPPENBEZ] int NOT NULL,
    [FX_DETAIL] int NOT NULL,
    [FX_FOOTER_GRUPPENBEZ] int NOT NULL,
    [FX_FOOTER_SAMMEL] int NOT NULL,
    [FX_FOOTER_FORMFEED] int NOT NULL,
    [FX_FOOTER_PAGE] int NOT NULL,
    [VERM_SKZ_SPROSSE] int NOT NULL,
    [VS_SPR_BREITE] int NULL,
    [VS_SPR_HOEHE] int NULL,
    [VS_SPR_FONT] decimal(28,8) NOT NULL,
    [FX_FORM_BEGIN] int NOT NULL,
    [DURCHSCHLAG_ANZ] int NOT NULL,
    [DURCHSCHLAG_CLOSE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [DRUCK_STRING] nvarchar(150) NULL,
    [OVERWRITE_DIM] int NULL,
    [REPORT_GRUPPE] int NULL,
    [IQUOTE_TRANSFER] int NOT NULL,
    [MODEL_LANGUAGE] nvarchar(5) NULL,
    [DIFFERING_SENDER] nvarchar(254) NULL,
    [MAIL_SUBJECT] int NULL,
    [MAIL_BODY] int NULL,
    [DMS_PDF] nvarchar(150) NULL,
    [PDF_PATH] nvarchar(254) NULL,
    [PDF_FILE] nvarchar(254) NULL,
    [PDF_FILE_INTERACTIVE] int NULL,
    [LINKS] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN_KAPA] (
    [ID] int NOT NULL,
    [TAG] int NOT NULL,
    [PRDKTART] nvarchar(40) NOT NULL,
    [KAPA] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN_MIN] (
    [ID] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [AUTO_ZUSCHL_KZ] int NOT NULL,
    [AUTO_ZUSCHL_WERT] decimal(28,8) NOT NULL,
    [AUTO_ZUSCHL_DATE] date NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN_TXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_KUNDEN_VOR] (
    [ID] int NOT NULL,
    [VORGANG] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [PROJEKT] nvarchar(40) NOT NULL,
    [ERLEDIGT] int NULL,
    [E_DATUM] datetime NULL,
    [E_ART] nvarchar(40) NOT NULL,
    [E_WER] nvarchar(40) NULL,
    [E_BEM] nvarchar(254) NULL,
    [E_TEXT] nvarchar(max) NULL,
    [A_DATUM] datetime NULL,
    [A_ART] nvarchar(40) NOT NULL,
    [A_WER] nvarchar(40) NULL,
    [A_BEM] nvarchar(254) NULL,
    [A_TEXT] nvarchar(max) NULL,
    [F_DATUM] datetime NULL,
    [F_ART] nvarchar(40) NOT NULL,
    [F_WER] nvarchar(40) NULL,
    [F_BEM] nvarchar(254) NULL,
    [F_TEXT] nvarchar(max) NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_LGESTELLE] (
    [KUNDE] int NOT NULL,
    [ZUSCHLARTIKEL] int NOT NULL,
    [GESTSTARTNR] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_LIEF_DAUER] (
    [PARTNER_ID] int NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [RANGFOLGE] int NOT NULL,
    [LIEFERDAUER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_LIMITREQUEST] (
    [ID] int NOT NULL,
    [REQUEST] datetime NOT NULL,
    [REQUESTED_BY] nvarchar(40) NOT NULL,
    [APPROVED_BY] nvarchar(40) NOT NULL,
    [REQ_LIMIT1] decimal(28,8) NULL,
    [REQ_LIMIT2] decimal(28,8) NULL,
    [REMARK] nvarchar(250) NULL,
    [STATUS] int NOT NULL,
    [LIMITED] date NULL,
    [FOLLOW_LIMIT1] decimal(28,8) NULL,
    [FOLLOW_LIMIT2] decimal(28,8) NULL,
    [BACKLOG] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [RESPONSE] nvarchar(max) NULL
);

CREATE TABLE SYSADM.[KU_LOGO] (
    [ID] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [BREITE] decimal(28,8) NOT NULL,
    [HOEHE] decimal(28,8) NOT NULL,
    [MOD_NR] int NOT NULL,
    [TYP] int NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [LOGO_POS] int NOT NULL,
    [LOGO_NR] int NOT NULL,
    [DIST_A] decimal(28,8) NOT NULL,
    [DIST_B] decimal(28,8) NOT NULL,
    [LAGE] int NOT NULL,
    [KZ_NOLOGO] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [VART] int NULL,
    [KANTE] int NULL,
    [WINKEL] decimal(28,8) NULL,
    [REFPUNKT] int NULL,
    [REFKANTE] int NULL,
    [L_BREITE] decimal(28,8) NULL,
    [L_HOEHE] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[KU_MITARB] (
    [ID] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [KZ_ANSPRECHPARTNER] nvarchar(1) NULL,
    [ANREDE] nvarchar(40) NOT NULL,
    [FUNKTION] nvarchar(40) NOT NULL,
    [ABTEILUNG] nvarchar(40) NULL,
    [ZIMMER] nvarchar(40) NULL,
    [INFO] nvarchar(max) NULL,
    [BRIEFKOPF] nvarchar(40) NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [ORT] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [KZ_PARTNERVERTRIEB] int NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [GEBURTSTAG] date NULL,
    [PW_HASH] nvarchar(32) NOT NULL,
    [KZ_WEB] int NOT NULL,
    [LOGINVERSUCHE] int NOT NULL,
    [LOGINS_FAILED] int NOT NULL,
    [BUSINESSCASE] int NOT NULL
);

CREATE TABLE SYSADM.[KU_OBJ_MAX_PROD] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [MAX_MENGE] decimal(28,8) NOT NULL,
    [MAX_QM] decimal(28,8) NOT NULL,
    [MAX_NETTO] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_OBJ_MAX_WGR] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [WGR_ID] nvarchar(3) NOT NULL,
    [MAX_MENGE] decimal(28,8) NOT NULL,
    [MAX_QM] decimal(28,8) NOT NULL,
    [MAX_NETTO] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_OBJ_SUM] (
    [DOC_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [OBJEKT_ID] int NOT NULL,
    [DATUM_LIEFER] date NULL,
    [LFM] decimal(28,8) NOT NULL,
    [CLAIM] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_OBJ_SUM_PROD] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [DOC_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KU_OBJ_SUM_WGR] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [WGR_ID] nvarchar(3) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [DOC_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[KUMA_KLASSI_WERTE] (
    [MA_ID] int NOT NULL,
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_ASN_DETAIL] (
    [HEADER_NR] int NOT NULL,
    [DETAIL_NR] int NOT NULL,
    [STATUS_CODE] nvarchar(1) NULL,
    [PLANT_NR] int NULL,
    [ASN_NR] int NULL,
    [DATE_SHIPPED] date NULL,
    [ASN_LINE] nvarchar(3) NULL,
    [RECORD_TYPE] nvarchar(1) NULL,
    [SP_ORDER_NR] int NULL,
    [SP_ORDER_LINE_NR] int NULL,
    [CRATE_NR] int NULL,
    [CRATE_TYPE] nvarchar(1) NULL,
    [CU_PO_NR] int NULL,
    [CU_PO_LINE_NR] int NULL,
    [QUANTITY_ORDERED] decimal(28,8) NULL,
    [COLOR_CODE] nvarchar(3) NULL,
    [TREATMENT_CODE] nvarchar(3) NULL,
    [COATING_CODE] nvarchar(3) NULL,
    [FABRICATION_CODE] nvarchar(3) NULL,
    [REF_COAT_TYPE] nvarchar(2) NULL,
    [SERIAL_NR] decimal(28,8) NULL,
    [GLASS_SPEC] nvarchar(25) NULL,
    [BASE_LEG_INT] int NULL,
    [BASE_LEG_NUM] int NULL,
    [BASE_LEG_DEN] int NULL,
    [LEFT_LEG_INT] int NULL,
    [LEFT_LEG_NUM] int NULL,
    [LEFT_LEG_DEN] int NULL,
    [RIGHT_LEG_INT] int NULL,
    [RIGHT_LEG_NUM] int NULL,
    [RIGHT_LEG_DEN] int NULL,
    [DETAIL_MESSAGE] nvarchar(25) NULL,
    [JOB_NAME] nvarchar(25) NULL,
    [PAID_CODE] nvarchar(1) NULL,
    [DATE_PRODUCED] date NULL,
    [TOP_LEG_INT] int NULL,
    [TOP_LEG_NUM] int NULL,
    [TOP_LEG_DEN] int NULL,
    [S1_LEG_INT] int NULL,
    [S1_LEG_NUM] int NULL,
    [S1_LEG_DEN] int NULL,
    [S2_LEG_INT] int NULL,
    [S2_LEG_NUM] int NULL,
    [S2_LEG_DEN] int NULL,
    [SHAPE_NR] nvarchar(3) NULL,
    [ERROR_CODE] nvarchar(1) NULL,
    [SEQUENCE_NR] nvarchar(3) NULL,
    [ALFAK_STATUS] int NOT NULL,
    [RELEASE] int NULL,
    [THICKNESS] decimal(28,8) NULL,
    [PRICE_PER_UNIT] decimal(28,8) NULL,
    [QUANTITY_SHIPPED] decimal(28,8) NULL,
    [SQFT_PER_UNIT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_ASN_ERROR] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(80) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_ASN_HEADER] (
    [HEADER_NR] int NOT NULL,
    [FILE_NAME] nvarchar(80) NOT NULL,
    [FILE_DATE] datetime NOT NULL,
    [STATUS_CODE] nvarchar(1) NULL,
    [PLANT_NR] int NULL,
    [ASN_NR] int NULL,
    [DATE_SHIPPED] date NULL,
    [ASN_LINE] int NULL,
    [RECORD_TYPE] nvarchar(1) NULL,
    [CUSTOMER] int NULL,
    [BILL_OF_LADING_NR] int NULL,
    [CARRIER] nvarchar(22) NULL,
    [LOAD_NR] int NULL,
    [INV_PLANT_NR] int NULL,
    [INV_NR] int NULL,
    [TIME_OF_SHIPMENT] datetime NULL,
    [TRAILER_NR] nvarchar(8) NULL,
    [ERROR_CODE] nvarchar(1) NULL,
    [SEQUENCE_NR] nvarchar(3) NULL,
    [ALFAK_STATUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_ASN_MISC] (
    [HEADER_NR] int NOT NULL,
    [MISC_NR] int NOT NULL,
    [STATUS_CODE] nvarchar(1) NULL,
    [PLANT_NR] int NULL,
    [ASN_NR] int NULL,
    [DATE_SHIPPED] date NULL,
    [ASN_LINE] int NULL,
    [RECORD_TYPE] nvarchar(1) NULL,
    [CU_PO_NR] int NULL,
    [CU_PO_LINE_NR] int NULL,
    [CRATE_NR] int NULL,
    [CRATE_TYPE] nvarchar(1) NULL,
    [ERROR_CODE] nvarchar(1) NULL,
    [SEQUENCE_NR] nvarchar(3) NULL,
    [ALFAK_STATUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_BEARB] (
    [ID] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_EK_LOG] (
    [DATUM] datetime NOT NULL,
    [LAGER_ID] int NOT NULL,
    [LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PRODUKT] int NOT NULL,
    [TYP] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [BEST_ID] int NULL,
    [POS_NR] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_KATEGORIE] (
    [ID] nvarchar(10) NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [NUMMER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_LAGERORTE] (
    [ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [VORGABE] int NOT NULL,
    [RES] int NOT NULL,
    [KUNDE] int NOT NULL,
    [LOCKED] int NOT NULL,
    [INPROD] int NOT NULL,
    [DATUM] datetime NOT NULL,
    [LIEFERANT] int NOT NULL,
    [PROD_LAUF] int NULL,
    [PROD_DATUM] date NULL,
    [DATUM_ANF] date NOT NULL,
    [EK_PREIS] decimal(28,8) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [LAG_MENGE_SOLL] decimal(28,8) NOT NULL,
    [BESTAND_ANF] decimal(28,8) NOT NULL,
    [BEM] nvarchar(254) NULL,
    [IDENT_CUST] nvarchar(80) NULL,
    [KZ_EK_GESAMT] int NULL,
    [MIN_MENGE] decimal(28,8) NULL,
    [STD_BEST_MENGE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL,
    [PM_TRAVEREN_FLAG] int NOT NULL,
    [PM_PRIO_OPTI] int NOT NULL,
    [PM_AUFLEGER_CODE] nvarchar(4) NULL,
    [POS_VERPACKUNG] nvarchar(40) NULL,
    [CRITICAL_QUANTITY] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[LG_LASTBOOKED] (
    [STOCK_ID] int NOT NULL,
    [KEY1] int NOT NULL,
    [KEY2] int NOT NULL,
    [KEY3] nvarchar(80) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_LOGBUCH] (
    [TRANS_ID] int IDENTITY(1,1) NOT NULL,
    [TYP] int NOT NULL,
    [LAGER_ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT_VON] int NOT NULL,
    [LAGERORT_NACH] int NOT NULL,
    [AUFTRAG] int NULL,
    [POS] int NULL,
    [BEM] nvarchar(254) NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [PROG_ID] int NOT NULL,
    [BU_DATUM] datetime NOT NULL,
    [AUSF_DATUM] datetime NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [EK] decimal(28,8) NOT NULL,
    [EK_EINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_MODELL] (
    [ID] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MOD_SN_NAME] nvarchar(40) NULL,
    [MOD_SN_TYP] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_PRODUKTE] (
    [ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [MIN_EK] decimal(28,8) NOT NULL,
    [LETZTER_EK] decimal(28,8) NOT NULL,
    [INHALT] int NOT NULL,
    [BEM] nvarchar(max) NULL,
    [HASH] nvarchar(16) NOT NULL,
    [MAX_EK] decimal(28,8) NOT NULL,
    [STD_BEST_MENGE] decimal(28,8) NULL,
    [MAX_EK_DAT] date NULL,
    [MIN_EK_DAT] date NULL,
    [LETZTER_EK_DAT] date NULL,
    [SPRACH_BASIS] int NOT NULL,
    [FARBE] int NOT NULL,
    [KATEGORIE] nvarchar(10) NOT NULL,
    [BREITE] decimal(28,8) NOT NULL,
    [HOEHE] decimal(28,8) NOT NULL,
    [MIN_MENGE] decimal(28,8) NOT NULL,
    [MAX_MENGE] decimal(28,8) NOT NULL,
    [MAIN_PRODUCT] int NULL,
    [QTY_CHECK_PER_LOCATION] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_SNAPSHOT] (
    [ID] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [DATUM] datetime NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_STUKL] (
    [ID] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [STRUKTURVERL] int NOT NULL,
    [STRUKTURSEITE] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [FARBE] int NOT NULL,
    [MOD] int NULL,
    [MENGE] decimal(28,8) NULL,
    [BREITE] decimal(28,8) NULL,
    [HOEHE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LG_TAG] (
    [NUMMER] int NOT NULL,
    [TYP] int NOT NULL,
    [ID] int NULL,
    [POS_NR] int NULL,
    [BOM_ID] int NULL,
    [KUNDEN_ID] int NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [LAND] nvarchar(40) NULL,
    [BESTELL_TXT] nvarchar(40) NULL,
    [KOMM_TXT] nvarchar(80) NULL,
    [HERSTELLER] int NULL,
    [KUNDENPOS] nvarchar(40) NULL,
    [STATUS] int NOT NULL,
    [DATUM] date NULL,
    [GEST_TYP] int NOT NULL,
    [IDENT] nvarchar(20) NOT NULL,
    [PROD_DATUM] date NULL,
    [LIEFER_DATUM] date NULL,
    [ETIK_STATUS] int NOT NULL,
    [GESTELL_TMP] nvarchar(12) NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [LAGER_ID] int NOT NULL,
    [MENGE_SOLL] decimal(28,8) NULL,
    [MENGE_IST] decimal(28,8) NULL,
    [DICKE] decimal(28,8) NULL,
    [GEWICHT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_ATTACH] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_KALENDER] (
    [ID] int NOT NULL,
    [VON] datetime NOT NULL,
    [BIS] datetime NOT NULL,
    [JAHR] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_KLASSI_WERTE] (
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LG_RND] (
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [RND_PUNKT] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRUPPE] nvarchar(40) NOT NULL,
    [PREIS_EINHEIT] nvarchar(20) NOT NULL,
    [KZ_NETTO_PREISE] int NOT NULL,
    [RND_TAB] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LI_OBJEKTE] (
    [LIEFERANT] int NOT NULL,
    [OBJEKT_ID] int NOT NULL,
    [VON] date NULL,
    [BIS] date NULL,
    [SITEADDR1] nvarchar(40) NULL,
    [SITEADDR2] nvarchar(40) NULL,
    [SITEADDR3] nvarchar(40) NULL,
    [INFO] nvarchar(max) NULL,
    [MAX_PREIS] decimal(28,8) NULL,
    [MAX_QM] decimal(28,8) NULL,
    [MAX_STUECK] decimal(28,8) NULL,
    [SITEADDR4] nvarchar(40) NULL,
    [CONTACT] nvarchar(40) NULL,
    [PHONE] nvarchar(40) NULL,
    [SUPERVISOR] nvarchar(40) NOT NULL,
    [DATE_ADD] date NULL,
    [QUOTE] nvarchar(20) NULL,
    [FOLGE] nvarchar(40) NULL,
    [VARIATION1] decimal(28,8) NOT NULL,
    [VARIATION2] decimal(28,8) NOT NULL,
    [VARIATION3] decimal(28,8) NOT NULL,
    [VARIATION4] decimal(28,8) NOT NULL,
    [VARIATION5] decimal(28,8) NOT NULL,
    [CLAIMED_SUM] decimal(28,8) NOT NULL,
    [CONTRACT_SUM] decimal(28,8) NOT NULL,
    [LAST_CLAIM] int NOT NULL,
    [EST_PURCH] decimal(28,8) NOT NULL,
    [REAL_PURCH] decimal(28,8) NOT NULL,
    [CLAIMED_PURCH] decimal(28,8) NOT NULL,
    [EST_HOURS] decimal(28,8) NOT NULL,
    [REAL_HOURS] decimal(28,8) NOT NULL,
    [RATE] decimal(28,8) NOT NULL,
    [ALLOWANCE] decimal(28,8) NOT NULL,
    [ABW_ADIENST] nvarchar(40) NOT NULL,
    [ABW_ZAHLBED] nvarchar(100) NOT NULL,
    [STATUS] int NOT NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [POSTFACH] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MANDANT] int NOT NULL,
    [TOUREN_RANGFOLGE] int NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NOT NULL,
    [ZAHLWEG] nvarchar(40) NOT NULL,
    [LIEFERBED] nvarchar(40) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KZ_ABGESCHLOSSEN] int NOT NULL,
    [CONTRACT_ORDER] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [SITESTREET] nvarchar(40) NULL,
    [SITEZIP] nvarchar(11) NULL,
    [SITECOUNTRY] nvarchar(6) NOT NULL,
    [SITEPROVINCE] nvarchar(20) NOT NULL,
    [SITETITLE] nvarchar(40) NOT NULL,
    [SITEDISTANCE] decimal(28,8) NOT NULL,
    [TRANSFER_ORDERENTRY] int NOT NULL
);

CREATE TABLE SYSADM.[LI_LI_RND] (
    [LIEFERANTEN_NR] int NOT NULL,
    [RND_PUNKT] int NOT NULL,
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRUPPE] nvarchar(40) NOT NULL,
    [PREIS_EINHEIT] nvarchar(20) NOT NULL,
    [KZ_NETTO_PREISE] int NOT NULL,
    [RND_TAB] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LIEF_DAUER] (
    [PARTNER_ID] int NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [RANGFOLGE] int NOT NULL,
    [LIEFERDAUER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LIEF_FORM] (
    [LIEFERANTEN_ID] int NOT NULL,
    [ID] int NOT NULL,
    [PUNKTE_ID] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ANZAHL] int NULL,
    [TEXT_KZ] nvarchar(40) NULL,
    [ID_FUSS] int NOT NULL,
    [ID_KOPF] int NOT NULL,
    [STANDARD] int NULL,
    [ATTRIB1] int NULL,
    [ATTRIB2] int NULL,
    [ATTRIB3] int NULL,
    [ATTRIB4] int NULL,
    [ATTRIB5] int NULL,
    [BEZ] nvarchar(40) NULL,
    [NAME1] nvarchar(40) NULL,
    [PROT_KZ] int NULL,
    [DRUCK_MODUS] int NULL,
    [STKL_DRUCK] int NULL,
    [MOD_DECIMAL] int NULL,
    [ABW_STATUS_ID] int NOT NULL,
    [VERM_SKZ] int NOT NULL,
    [VS_BREITE] int NULL,
    [VS_HOEHE] int NULL,
    [PREISDRUCK] int NOT NULL,
    [ATTRIB6] int NULL,
    [ATTRIB7] int NULL,
    [ATTRIB8] int NULL,
    [ATTRIB9] int NULL,
    [ATTRIB10] int NULL,
    [SCALEZVMTEXT] decimal(28,8) NULL,
    [SCALESEGTEXT] decimal(28,8) NULL,
    [SCALETEXTSEG] decimal(28,8) NULL,
    [SCALESHAPETEXT] decimal(28,8) NULL,
    [TEXT1] int NOT NULL,
    [TEXT2] int NOT NULL,
    [TEXT3] int NOT NULL,
    [TEXT4] int NOT NULL,
    [TEXT5] int NOT NULL,
    [TEXT6] int NOT NULL,
    [TEXT7] int NOT NULL,
    [TEXT8] int NOT NULL,
    [TEXT9] int NOT NULL,
    [TEXT10] int NOT NULL,
    [ATTRIB11] int NULL,
    [ATTRIB12] int NULL,
    [ATTRIB13] int NULL,
    [ATTRIB14] int NULL,
    [ATTRIB15] int NULL,
    [ATTRIB16] int NULL,
    [ATTRIB17] int NULL,
    [ATTRIB18] int NULL,
    [ATTRIB19] int NULL,
    [ATTRIB20] int NULL,
    [OPTIONEN] nvarchar(254) NULL,
    [FX_HEADER_PAGE] int NOT NULL,
    [FX_HEADER_FORMFEED] int NOT NULL,
    [FX_HEADER_SAMMEL] int NOT NULL,
    [FX_HEADER_GRUPPENBEZ] int NOT NULL,
    [FX_DETAIL] int NOT NULL,
    [FX_FOOTER_GRUPPENBEZ] int NOT NULL,
    [FX_FOOTER_SAMMEL] int NOT NULL,
    [FX_FOOTER_FORMFEED] int NOT NULL,
    [FX_FOOTER_PAGE] int NOT NULL,
    [VERM_SKZ_SPROSSE] int NOT NULL,
    [VS_SPR_BREITE] int NULL,
    [VS_SPR_HOEHE] int NULL,
    [VS_SPR_FONT] decimal(28,8) NOT NULL,
    [FX_FORM_BEGIN] int NOT NULL,
    [DURCHSCHLAG_ANZ] int NOT NULL,
    [DURCHSCHLAG_CLOSE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [DRUCK_STRING] nvarchar(150) NULL,
    [OVERWRITE_DIM] int NULL,
    [REPORT_GRUPPE] int NULL,
    [IQUOTE_TRANSFER] int NOT NULL,
    [MODEL_LANGUAGE] nvarchar(5) NULL,
    [DIFFERING_SENDER] nvarchar(254) NULL,
    [MAIL_SUBJECT] int NULL,
    [MAIL_BODY] int NULL,
    [DMS_PDF] nvarchar(150) NULL,
    [PDF_PATH] nvarchar(254) NULL,
    [PDF_FILE] nvarchar(254) NULL,
    [PDF_FILE_INTERACTIVE] int NULL,
    [LINKS] nvarchar(254) NULL
);

CREATE TABLE SYSADM.[LI_LIEFERANTEN] (
    [ID] int NOT NULL,
    [MANDANT] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [EXTERNE_KNR] nvarchar(20) NULL,
    [ADRESS_KOPF] nvarchar(40) NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [PLZ_POSTFACH] nvarchar(11) NULL,
    [POSTFACH] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [KURZINFO] nvarchar(max) NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [ZAHLBED] nvarchar(100) NOT NULL,
    [ZAHLWEG] nvarchar(40) NOT NULL,
    [ZAHL_FB_KZ] int NOT NULL,
    [ZAHL_RLEG_TAG] int NULL,
    [ZAHL_TAG1] int NOT NULL,
    [LIEFERBED] nvarchar(40) NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [SACHBEARB] nvarchar(40) NOT NULL,
    [BONITAET] nvarchar(40) NOT NULL,
    [RANDABZUG] decimal(28,8) NULL,
    [ANLIEF_WERT] decimal(28,8) NOT NULL,
    [AUTO_ZUSCHL_WERT] decimal(28,8) NOT NULL,
    [HK_SONZU] decimal(28,8) NOT NULL,
    [FS_VORG_DOK_NR] int NOT NULL,
    [OP_DATUM] date NULL,
    [KONTO] nvarchar(20) NULL,
    [SAMMEL_KTO] nvarchar(20) NULL,
    [UST_ID] nvarchar(30) NULL,
    [KZ_SPRACH] int NOT NULL,
    [KZ_PRIVAT] int NULL,
    [KZ_SAMMELRE] int NULL,
    [KZ_TEILFAK] int NULL,
    [KZ_TEILLIEF] int NULL,
    [OP_TAGE] int NOT NULL,
    [KREDITMITFILIALE] int NOT NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KZ_KLEINGLAS] int NULL,
    [KZ_KREDITPRUEF] int NULL,
    [KZ_POSTFACH] int NULL,
    [KZ_STATUS] int NULL,
    [KZ_MASSEINH] int NOT NULL,
    [KZ_KINFO] int NULL,
    [KZ_MAHNSPERRE] int NULL,
    [KZ_WIEDERVORLAGE] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KZ_BANKEINZ] int NULL,
    [KZ_NETTOPREISE] int NULL,
    [KZ_DEF_FIL] int NULL,
    [KZ_FAXVERSAND] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [STRUKT_SEITE] int NOT NULL,
    [IBAN] nvarchar(40) NULL,
    [MAHN_TEXT] nvarchar(40) NOT NULL,
    [ZAHL_BLZ] nvarchar(20) NULL,
    [ZAHL_KTN] nvarchar(20) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [EIL_ZUSCHL_TAGE] int NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [MWST_KZ1] int NOT NULL,
    [MWST_KZ2] int NOT NULL,
    [HAUPT_ID] int NOT NULL,
    [KZ_FIBU_MANDANT] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNGBR] int NOT NULL,
    [RUNDUNGHOE] int NOT NULL,
    [DFUE_ART] int NOT NULL,
    [DFUE_LOG] int NOT NULL,
    [DFUE_DATEI] nvarchar(80) NULL,
    [DFUE_BATCH_DATEI] nvarchar(max) NULL,
    [MOD_SYM] int NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [ZAHL_TAG2] int NOT NULL,
    [ZAHL_TAG3] int NOT NULL,
    [SORT_KRIT] int NOT NULL,
    [SKONTO_BERTYP] int NOT NULL,
    [DFUE_LOG_TIMEOUT] int NOT NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [ZWANG_REF] int NOT NULL,
    [FS_AUFTR_REJECT] int NOT NULL,
    [DFUE_SAVE] nvarchar(80) NULL,
    [DFUE_PROTOKOLL] nvarchar(80) NULL,
    [FS_RESTRICT] int NULL,
    [PACKREGEL] int NOT NULL,
    [PMG_DEF] int NOT NULL,
    [FS_EXCHANGE] int NOT NULL,
    [FILIALNR] int NULL,
    [FS_STL] int NULL,
    [KURS_FIX] int NOT NULL,
    [VERPACKUNG] nvarchar(40) NOT NULL,
    [PREISDRUCK] int NOT NULL,
    [PIN] int NULL,
    [EDI_FILE_FROM] int NOT NULL,
    [EDI_FILE_TO] int NOT NULL,
    [EDI_FILE_ACT] int NOT NULL,
    [KZ_DIVERS] int NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FW_ART] int NOT NULL,
    [FS_PRICING_ALLOWED] int NULL,
    [FS_FREE_ALLOWED] int NULL,
    [TEMP] int NULL,
    [AUTO_ZUSCHL_KZ] int NOT NULL,
    [AUTO_ZUSCHL_DATE] date NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [RECH_FAX] nvarchar(40) NULL,
    [FS_CHANGE_ISO] int NOT NULL,
    [BRUTTOTAG2] int NOT NULL,
    [BRUTTOTAG3] int NOT NULL,
    [BRUTTOTAG4] int NOT NULL,
    [BRUTTOTAG5] int NOT NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [ADIENST2] nvarchar(40) NOT NULL,
    [SALDO_J] decimal(28,8) NULL,
    [KREDIT_LIMIT] decimal(28,8) NULL,
    [KREDIT_LIMIT1] decimal(28,8) NULL,
    [OBLIGO] decimal(28,8) NULL,
    [SPRACH_BASIS] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [RE_DATEI] nvarchar(80) NULL,
    [RE_BATCH_DATEI] nvarchar(max) NULL,
    [RE_PROTOKOLL] nvarchar(80) NULL,
    [RE_SAVE] nvarchar(80) NULL,
    [RE_ART] int NULL,
    [STEUERMODUS] int NOT NULL,
    [RE_FILE_FROM] int NOT NULL,
    [RE_FILE_ACT] int NOT NULL,
    [RE_FILE_TO] int NOT NULL,
    [FS_MAIN_REF] int NOT NULL,
    [CE_FLAG] int NOT NULL,
    [EDI_FORMEL] int NOT NULL,
    [FX_EDI] int NOT NULL,
    [FX_CUSTOMER] int NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [EDI_DIM_FRACTION] int NULL,
    [OT_EXPORT_ART] int NULL,
    [OT_EXPORT_FLAGS] int NULL,
    [OT_WEBSERV_ADRESSE] nvarchar(80) NULL,
    [OT_WEBSERV_BENUTZER] nvarchar(40) NULL,
    [OT_WEBSERV_PASSWORT] nvarchar(40) NULL,
    [OT_WEBSERV_GRUPPE] int NULL,
    [OT_WEBSERV_MANDANT] int NULL,
    [OT_ALT_MAILADRESSE_RECHN] nvarchar(40) NULL,
    [HOMEPAGE] nvarchar(254) NULL,
    [MWST_KZ3] int NOT NULL,
    [MWST_KZ4] int NOT NULL,
    [MWST_KZ5] int NOT NULL,
    [UMSATZ_VERTR2] decimal(28,8) NOT NULL,
    [OT_ALT_MAILADRESSE_AB] nvarchar(40) NULL,
    [KZ_STEUERFLAG] int NULL,
    [OT_ALT_MAILADRESSE_LIEFS] nvarchar(40) NULL,
    [ZAHLUNGSTAGVERSCHIEBUNG] int NULL,
    [BANK_ID] int NOT NULL,
    [KZ_ICMS_CONTR] int NULL,
    [KZ_STATE_REGISTER] int NULL,
    [KZ_ICMS_ST] int NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NOT NULL,
    [MINMENGE_VK] decimal(28,8) NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [FS_ESG_EDGE] int NOT NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_LIEF] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [FS_CHECK_BEAPARAM] int NOT NULL,
    [FS_INDIVIDUAL_TEXTS] int NOT NULL,
    [FS_NO_SURCHARGE_CALC] int NOT NULL,
    [DFUE_SINGLE] int NULL,
    [DFUE_BARCODE] int NULL,
    [ZUSCHLAG2_BIT] int NOT NULL,
    [MAIL_MODE] int NOT NULL,
    [OT_DATEI_ERW] int NOT NULL,
    [OT_EXCLUDE_PROD_AB] nvarchar(254) NULL,
    [KREDIT_LIMIT_NET] float NULL,
    [KREDIT_LIMIT1_NET] float NULL,
    [EULER_ID] int NULL,
    [FS_NO_SHAPE_DETECTION] int NULL,
    [BUSINESSCASE] int NOT NULL,
    [IQUOTE_CHARTER] int NULL
);

CREATE TABLE SYSADM.[LI_LIEFERANTEN_KAPA] (
    [ID] int NOT NULL,
    [TAG] int NOT NULL,
    [PRDKTART] nvarchar(40) NOT NULL,
    [KAPA] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LIEFERANTEN_TXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LIEFERANTEN_VOR] (
    [ID] int NOT NULL,
    [VORGANG] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [PROJEKT] nvarchar(40) NOT NULL,
    [ERLEDIGT] int NULL,
    [E_DATUM] datetime NULL,
    [E_ART] nvarchar(40) NOT NULL,
    [E_WER] nvarchar(40) NULL,
    [E_BEM] nvarchar(254) NULL,
    [E_TEXT] nvarchar(max) NULL,
    [A_DATUM] datetime NULL,
    [A_ART] nvarchar(40) NOT NULL,
    [A_WER] nvarchar(40) NULL,
    [A_BEM] nvarchar(254) NULL,
    [A_TEXT] nvarchar(max) NULL,
    [F_DATUM] datetime NULL,
    [F_ART] nvarchar(40) NOT NULL,
    [F_WER] nvarchar(40) NULL,
    [F_BEM] nvarchar(254) NULL,
    [F_TEXT] nvarchar(max) NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_LIEFKARTEI_PROD] (
    [PRODUKT] int NOT NULL,
    [ID] int NOT NULL,
    [VORGABE] int NULL,
    [BEZ1] nvarchar(40) NULL,
    [BEZ2] nvarchar(40) NULL,
    [BEZ3] nvarchar(65) NULL,
    [LIEFERZEIT] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [FARBE] int NOT NULL,
    [SPRACH_ID] int NULL,
    [BEST_INTERN] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [KUNDEN_ID] int NOT NULL,
    [DOKTYP] int NOT NULL,
    [MANDANT] int NULL,
    [AV_BEREICH2] nvarchar(40) NOT NULL,
    [BREITE] int NULL,
    [HOEHE] int NULL,
    [MANDANT2] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [CONDITIONS] xml NULL
);

CREATE TABLE SYSADM.[LI_LIEFKARTEI_WGR] (
    [WGR] nvarchar(3) NOT NULL,
    [ID] int NOT NULL,
    [VORGABE] int NULL,
    [BEZ1] nvarchar(40) NULL,
    [BEZ2] nvarchar(40) NULL,
    [BEZ3] nvarchar(40) NULL,
    [LIEFERZEIT] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [BEST_INTERN] int NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [KUNDEN_ID] int NOT NULL,
    [DOKTYP] int NOT NULL,
    [MANDANT] int NULL,
    [AV_BEREICH2] nvarchar(40) NOT NULL,
    [BREITE] int NULL,
    [HOEHE] int NULL,
    [MANDANT2] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [CONDITIONS] xml NULL
);

CREATE TABLE SYSADM.[LI_MITARB] (
    [ID] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [KZ_ANSPRECHPARTNER] nvarchar(1) NULL,
    [ANREDE] nvarchar(40) NOT NULL,
    [FUNKTION] nvarchar(40) NOT NULL,
    [ABTEILUNG] nvarchar(40) NULL,
    [ZIMMER] nvarchar(40) NULL,
    [INFO] nvarchar(max) NULL,
    [BRIEFKOPF] nvarchar(40) NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [ORT] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [KZ_PARTNERVERTRIEB] int NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [GEBURTSTAG] date NULL
);

CREATE TABLE SYSADM.[LI_OBJ_MAX_PROD] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [MAX_MENGE] decimal(28,8) NOT NULL,
    [MAX_QM] decimal(28,8) NOT NULL,
    [MAX_NETTO] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_OBJ_MAX_WGR] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [WGR_ID] nvarchar(3) NOT NULL,
    [MAX_MENGE] decimal(28,8) NOT NULL,
    [MAX_QM] decimal(28,8) NOT NULL,
    [MAX_NETTO] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_OBJ_SUM] (
    [DOC_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [OBJEKT_ID] int NOT NULL,
    [DATUM_LIEFER] date NULL,
    [LFM] decimal(28,8) NOT NULL,
    [CLAIM] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_OBJ_SUM_PROD] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [DOC_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LI_OBJ_SUM_WGR] (
    [OBJEKT_ID] int NOT NULL,
    [PARTNER_ID] int NOT NULL,
    [WGR_ID] nvarchar(3) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [NETTO] decimal(28,8) NOT NULL,
    [DOC_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[LIMA_KLASSI_WERTE] (
    [MA_ID] int NOT NULL,
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_BEARB] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [BEA_PARAM4] decimal(28,8) NULL,
    [BEA_PARAM5] decimal(28,8) NULL,
    [BEA_PARAM6] decimal(28,8) NULL,
    [BEA_PARAM7] decimal(28,8) NULL,
    [BEA_PARAM8] decimal(28,8) NULL,
    [BEA_PARAM9] decimal(28,8) NULL,
    [BEA_PARAM10] decimal(28,8) NULL,
    [BEA_PARAM11] decimal(28,8) NULL,
    [BEA_PARAM12] decimal(28,8) NULL,
    [BEA_PARAM13] decimal(28,8) NULL,
    [BEA_PARAM14] decimal(28,8) NULL,
    [BEA_PARAM15] decimal(28,8) NULL,
    [BEA_PARAM16] decimal(28,8) NULL,
    [BEA_PARAM17] decimal(28,8) NULL,
    [BEA_TEXT] nvarchar(120) NULL,
    [BOM_ID] int NOT NULL,
    [BEA_PARAM18] decimal(28,8) NULL,
    [BEA_PARAM19] decimal(28,8) NULL,
    [BEA_PARAM20] decimal(28,8) NULL,
    [BEA_TEXT_ORIG] nvarchar(120) NULL,
    [BEA_ANZAHL] decimal(28,8) NULL,
    [BEA_PARAM1] decimal(28,8) NULL,
    [BEA_PARAM2] decimal(28,8) NULL,
    [BEA_PARAM3] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [SN_MAKRO_NAME] nvarchar(254) NULL,
    [BEA_PARAM21] decimal(28,8) NULL,
    [BEA_PARAM22] decimal(28,8) NULL,
    [BEA_PARAM23] decimal(28,8) NULL,
    [BEA_PARAM24] decimal(28,8) NULL,
    [BEA_PARAM25] decimal(28,8) NULL,
    [BEA_PARAM26] decimal(28,8) NULL,
    [BEA_PARAM27] decimal(28,8) NULL,
    [BEA_PARAM28] decimal(28,8) NULL,
    [BEA_PARAM29] decimal(28,8) NULL,
    [BEA_PARAM30] decimal(28,8) NULL,
    [BEA_TEXT_FOREIGN] nvarchar(120) NULL,
    [EDGE_ZUSCHL1] decimal(28,8) NULL,
    [EDGE_ZUSCHL2] decimal(28,8) NULL,
    [EDGE_ZUSCHL3] decimal(28,8) NULL,
    [EDGE_ZUSCHL4] decimal(28,8) NULL,
    [EDGE_ZUSCHL5] decimal(28,8) NULL,
    [EDGE_ZUSCHL6] decimal(28,8) NULL,
    [EDGE_ZUSCHL7] decimal(28,8) NULL,
    [EDGE_ZUSCHL8] decimal(28,8) NULL,
    [ANZ_ZUSCHL_WAAG] int NULL,
    [ANZ_ZUSCHL_SENK] int NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_KOPF] (
    [ID] int NOT NULL,
    [NR_LIEFERSCHEIN] int NULL,
    [SU_LFM_REAL] decimal(28,8) NULL,
    [STATUS] int NOT NULL,
    [DATUM_ERF] date NOT NULL,
    [DATUM_AB] date NULL,
    [DATUM_LIEFERSCHEIN] date NULL,
    [DATUM_RECHNUNG] date NULL,
    [DATUM_PROD1] date NULL,
    [DATUM_PROD2] date NULL,
    [DATUM_PROD3] date NULL,
    [DATUM_LIEFER_PLAN] date NULL,
    [DATUM_LIEFER_TAT] date NULL,
    [DATUM_LIEFERWUNSCH] nvarchar(40) NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [DATUM_FAELLIGK] date NULL,
    [DATUM_BEST] date NULL,
    [LAUF_PROD1] int NULL,
    [LAUF_PROD2] int NULL,
    [LAUF_PROD3] int NULL,
    [BEST_TEXT1] nvarchar(40) NULL,
    [BEST_TEXT2] nvarchar(40) NULL,
    [AH_HAUPT_AUFTR] int NOT NULL,
    [AH_IDENT] int NOT NULL,
    [AH_LIEFERANT] int NULL,
    [AH_MCODE] nvarchar(10) NULL,
    [AH_KOPF] nvarchar(40) NULL,
    [AH_NAME1] nvarchar(40) NULL,
    [AH_NAME2] nvarchar(40) NULL,
    [AH_NAME3] nvarchar(40) NULL,
    [AH_STRASSE] nvarchar(40) NULL,
    [AH_PLZ] nvarchar(11) NULL,
    [AH_ORT] nvarchar(40) NULL,
    [AH_LAND] nvarchar(6) NOT NULL,
    [AH_TELEFON] nvarchar(40) NULL,
    [AH_FAX] nvarchar(40) NULL,
    [AH_ANREDE] nvarchar(40) NULL,
    [AH_PARTNER] nvarchar(40) NULL,
    [AH_BIT1] int NULL,
    [AL_IDENT] int NOT NULL,
    [AL_KOPF] nvarchar(40) NULL,
    [AL_NAME1] nvarchar(40) NULL,
    [AL_NAME2] nvarchar(40) NULL,
    [AL_NAME3] nvarchar(40) NULL,
    [AL_STRASSE] nvarchar(40) NULL,
    [AL_PLZ] nvarchar(11) NULL,
    [AL_ORT] nvarchar(40) NULL,
    [AL_LAND] nvarchar(6) NOT NULL,
    [AL_TELEFON] nvarchar(40) NULL,
    [AL_FAX] nvarchar(40) NULL,
    [AL_ANREDE] nvarchar(40) NULL,
    [AL_PARTNER] nvarchar(40) NULL,
    [AR_IDENT] int NOT NULL,
    [AR_KOPF] nvarchar(40) NULL,
    [AR_NAME1] nvarchar(40) NULL,
    [AR_NAME2] nvarchar(40) NULL,
    [AR_NAME3] nvarchar(40) NULL,
    [AR_STRASSE] nvarchar(40) NULL,
    [AR_PLZ] nvarchar(11) NULL,
    [AR_ORT] nvarchar(40) NULL,
    [AR_LAND] nvarchar(6) NOT NULL,
    [AR_TELEFON] nvarchar(40) NULL,
    [AR_FAX] nvarchar(40) NULL,
    [AR_ANREDE] nvarchar(40) NULL,
    [AR_PARTNER] nvarchar(40) NULL,
    [OR_SPRACH_ID] int NOT NULL,
    [OR_SPRACH_BASIS] int NOT NULL,
    [OR_MANDANT] int NOT NULL,
    [OR_AVBEREICH] nvarchar(40) NOT NULL,
    [OR_BEARBEITER] nvarchar(40) NOT NULL,
    [OR_FACHBERATER] nvarchar(40) NOT NULL,
    [OR_GESCHART] nvarchar(80) NOT NULL,
    [OR_SPERRKZ] nvarchar(40) NOT NULL,
    [OR_ADIENST] nvarchar(40) NOT NULL,
    [OR_VERPACKUNG] nvarchar(40) NOT NULL,
    [OR_LIEFERBED] nvarchar(40) NOT NULL,
    [OR_TOUR] nvarchar(40) NOT NULL,
    [OR_AWTOUR] nvarchar(40) NOT NULL,
    [OR_FAHRER] nvarchar(40) NOT NULL,
    [OR_ZOLLTOUR] nvarchar(40) NOT NULL,
    [OR_GRUPPE] nvarchar(40) NOT NULL,
    [KO_MASSEINH] int NOT NULL,
    [SU_LFM_FAKT] decimal(28,8) NULL,
    [KO_OBJEKT_KUNDE] int NOT NULL,
    [KO_OBJEKT_LIEF] int NOT NULL,
    [KO_SAMMELRE] int NULL,
    [KO_TEILFAK] int NULL,
    [KO_TEILLIEF] int NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [KO_NETTOPREISE] int NULL,
    [KO_FAXVERSAND] int NOT NULL,
    [OR_ADIENST2] nvarchar(40) NOT NULL,
    [SU_QM_REAL] decimal(28,8) NULL,
    [SU_QM_FAKT] decimal(28,8) NULL,
    [SU_GEWICHT] decimal(28,8) NULL,
    [SU_GEWICHT_TARA] decimal(28,8) NULL,
    [SU_STUNDEN] decimal(28,8) NULL,
    [SU_KM] decimal(28,8) NULL,
    [SU_SPRLFM_FAKT] decimal(28,8) NULL,
    [SU_SPRLFM_REAL] decimal(28,8) NULL,
    [FI_VALUTAKURS] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_ZAHLBED] nvarchar(100) NOT NULL,
    [FI_ZAHLWEG] nvarchar(40) NOT NULL,
    [FI_WAEHRUNG] nvarchar(8) NOT NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST1_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST2_BASIS_FW] decimal(28,8) NULL,
    [FI_MWST1_ID] int NOT NULL,
    [FI_MWST2_ID] int NOT NULL,
    [FI_RABATT1] decimal(28,8) NULL,
    [FI_BETR_NETTO] decimal(28,8) NULL,
    [FI_BETR_BRUTTO] decimal(28,8) NULL,
    [FI_BETR_NETTO_FW] decimal(28,8) NULL,
    [FI_UST_ID] nvarchar(40) NULL,
    [FI_FB_KZ] int NOT NULL,
    [FI_RLEG_TAG] int NULL,
    [FI_ZAHL_TAG1] int NULL,
    [FI_BETR_BRUTTO_FW] decimal(28,8) NULL,
    [FI_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME2] decimal(28,8) NULL,
    [FI_BETR_ZWSU1_FW] decimal(28,8) NULL,
    [FI_BETR_ZWSU2_FW] decimal(28,8) NULL,
    [FI_BETR_EK1] decimal(28,8) NULL,
    [FI_BETR_EK2] decimal(28,8) NULL,
    [FI_SKONTO_BASIS] decimal(28,8) NULL,
    [FI_SKONTO_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_SKONTO] decimal(28,8) NULL,
    [FI_BETR_SKONTO_FW] decimal(28,8) NULL,
    [FI_BETR_PROV] decimal(28,8) NULL,
    [FI_BETR_ZWSUMME1] decimal(28,8) NULL,
    [EK_BETR_FESTPREIS] decimal(28,8) NULL,
    [FI_BETR_FESTPR_FW] decimal(28,8) NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BIT] int NOT NULL,
    [ETIKETTEN_TYP] int NULL,
    [DOK_TYP] nvarchar(40) NOT NULL,
    [FI_ZAHL_TAG2] int NULL,
    [FI_ZAHL_TAG3] int NULL,
    [LADELISTE] int NULL,
    [OR_LKW] nvarchar(40) NOT NULL,
    [AH_PLZ_POSTFACH] nvarchar(11) NULL,
    [AH_POSTFACH] nvarchar(40) NULL,
    [AR_PLZ_POSTFACH] nvarchar(11) NULL,
    [AR_POSTFACH] nvarchar(40) NULL,
    [AH_PROVINZ] nvarchar(20) NOT NULL,
    [AL_PROVINZ] nvarchar(20) NOT NULL,
    [AR_PROVINZ] nvarchar(20) NOT NULL,
    [AH_LIEFER_KZ] int NOT NULL,
    [KO_PREISDRUCK] int NOT NULL,
    [HK_MATGEMKOST1] decimal(28,8) NOT NULL,
    [MOD] int NULL,
    [KURS_FIX] int NOT NULL,
    [UMS_VERTR2] int NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [HK_MATGEMKOST2] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST3] decimal(28,8) NOT NULL,
    [HK_LOHNNEBKOST] decimal(28,8) NOT NULL,
    [AH_MAIL] nvarchar(254) NULL,
    [DOC_CLIP_ID] int NOT NULL,
    [DATUM_ANLIEFERUNG] date NULL,
    [FW_ART] int NOT NULL,
    [CONTRACT] int NOT NULL,
    [CLAIM] int NOT NULL,
    [CLAIM_ORDER] int NOT NULL,
    [FREMD_KEY] nvarchar(15) NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FI_BETR_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [KZ_INDIV_KUNDE] int NOT NULL,
    [HK_AKTIV] int NOT NULL,
    [AH_ARCHITECT] int NOT NULL,
    [NR_RECHNUNG] decimal(28,8) NULL,
    [KO_FALZ] decimal(28,8) NULL,
    [SU_STUECK] decimal(28,8) NULL,
    [SU_STUECK_ISO] decimal(28,8) NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [KATEGORIE] int NOT NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [KO_PRIVAT] int NULL,
    [KO_STEUERFLAG] int NULL,
    [FI_MWST3_ID] int NOT NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_MWST4_ID] int NOT NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_MWST5_ID] int NOT NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR1] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR2] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR3] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR4] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR5] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR6] decimal(28,8) NULL,
    [FI_TEILZAHL_BETR7] decimal(28,8) NULL,
    [FI_TEILZAHL_DATUM1] datetime NULL,
    [FI_TEILZAHL_DATUM2] datetime NULL,
    [FI_TEILZAHL_DATUM3] datetime NULL,
    [FI_TEILZAHL_DATUM4] datetime NULL,
    [FI_TEILZAHL_DATUM5] datetime NULL,
    [FI_TEILZAHL_DATUM6] datetime NULL,
    [FI_TEILZAHL_DATUM7] datetime NULL,
    [PRINTGUID1] nvarchar(40) NULL,
    [PRINTGUID2] nvarchar(40) NULL,
    [PRINTGUID3] nvarchar(40) NULL,
    [PRINTSEQ1] int NULL,
    [PRINTSEQ2] int NULL,
    [PRINTSEQ3] int NULL,
    [FI_KALK_FRACHTK] decimal(28,8) NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [INVOICE_CANCELED] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [HASH_CODE] nvarchar(254) NULL,
    [HASH_CODE_DELIVERY] nvarchar(254) NULL,
    [CHANGED] int NULL,
    [PROZ_ERFOLG] int NOT NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_KOPF_EX] (
    [ID] int NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [OTR_ROUTE] int NULL,
    [OTR_SEQUENCE] int NULL,
    [OTR_STATUS] int NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [LIEFERZEIT_VON] datetime NULL,
    [LIEFERZEIT_BIS] datetime NULL,
    [USER_FIELD1] nvarchar(60) NULL,
    [USER_FIELD2] nvarchar(60) NULL,
    [USER_FIELD3] nvarchar(60) NULL,
    [DATUM_VORLAGE] datetime NULL,
    [DISPATCHORDERNO] nvarchar(40) NULL,
    [ECOMMERCENO] nvarchar(40) NULL,
    [LIEFTERM_GRND] nvarchar(40) NULL,
    [ZUSCHLAG2_BIT] int NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_MODELL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [MOD_NUMMER] int NULL,
    [MOD_SN_NAME] nvarchar(254) NULL,
    [MOD_SN_TYP] int NULL,
    [STUFE_GLAS] int NULL,
    [MOD_STUFE5] decimal(28,8) NULL,
    [MOD_STUFE6] decimal(28,8) NULL,
    [MOD_STUFE7] decimal(28,8) NULL,
    [MOD_STUFE8] decimal(28,8) NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [BOM_ID] int NOT NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [MOD_STUFE1] decimal(28,8) NULL,
    [MOD_STUFE2] decimal(28,8) NULL,
    [MOD_STUFE3] decimal(28,8) NULL,
    [MOD_STUFE4] decimal(28,8) NULL,
    [MASS_BIT] int NULL,
    [RUECKSCHNITT1] decimal(28,8) NULL,
    [RUECKSCHNITT2] decimal(28,8) NULL,
    [RUECKSCHNITT3] decimal(28,8) NULL,
    [RUECKSCHNITT4] decimal(28,8) NULL,
    [RUECKSCHNITT5] decimal(28,8) NULL,
    [RUECKSCHNITT6] decimal(28,8) NULL,
    [RUECKSCHNITT7] decimal(28,8) NULL,
    [RUECKSCHNITT8] decimal(28,8) NULL,
    [SN] varbinary(max) NULL,
    [MOD_SN_TEMPLATE] nvarchar(254) NULL,
    [ROTATION] int NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_POS] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POS_TEXT] nvarchar(6) NULL,
    [POS_GRUPPE] int NOT NULL,
    [POS_KOMMISSION] nvarchar(80) NULL,
    [POS_KUNDENPOS] nvarchar(40) NULL,
    [POS_BIT] int NOT NULL,
    [POS_STTXT_NR] int NOT NULL,
    [POS_STATUS] int NOT NULL,
    [POS_AUFTRINFO] int NOT NULL,
    [POS_LIEFERANT] int NOT NULL,
    [PROD_ID] int NOT NULL,
    [PROD_BEZ1] nvarchar(60) NULL,
    [PROD_BEZ2] nvarchar(60) NULL,
    [PROD_BEZ3] nvarchar(65) NULL,
    [PROD_KURZ_BEZ] nvarchar(20) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [PROD_WGR] nvarchar(3) NOT NULL,
    [PROD_WGR_STAT] nvarchar(3) NOT NULL,
    [PROD_KMB] nvarchar(3) NOT NULL,
    [PROD_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_MEEINHEIT] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [PROD_KOMONR] int NOT NULL,
    [PROD_PRODART] int NOT NULL,
    [PROD_PRODGRP] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PP_LFM] decimal(28,8) NOT NULL,
    [PP_GEWICHT] decimal(28,8) NOT NULL,
    [PP_GEWICHT_TARA] decimal(28,8) NOT NULL,
    [PP_QM_FAKT] decimal(28,8) NOT NULL,
    [PP_LFM_FAKT] decimal(28,8) NOT NULL,
    [PP_DICKE] decimal(28,8) NOT NULL,
    [PP_FALZ] decimal(28,8) NOT NULL,
    [PP_PLANSTK] decimal(28,8) NOT NULL,
    [PP_ORIG_MENGE] decimal(28,8) NOT NULL,
    [PP_TEILGEL_MENGE] decimal(28,8) NOT NULL,
    [FI_PREIS_ME] decimal(28,8) NOT NULL,
    [FI_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [FI_RABATT1] decimal(28,8) NOT NULL,
    [FI_BRUTTO] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [FI_BRUTTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO] decimal(28,8) NOT NULL,
    [FI_NETTO_FW] decimal(28,8) NOT NULL,
    [FI_NETTO_GES] decimal(28,8) NOT NULL,
    [FI_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [FI_POS_STK] decimal(28,8) NOT NULL,
    [FI_POS_STK_FW] decimal(28,8) NOT NULL,
    [FI_POS_GES] decimal(28,8) NOT NULL,
    [FI_POS_GES_FW] decimal(28,8) NULL,
    [FI_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_FESTPREIS] decimal(28,8) NOT NULL,
    [EK_POS_STK] decimal(28,8) NOT NULL,
    [EK_POS_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MISCHFAKTOR1] decimal(28,8) NOT NULL,
    [MISCHFAKTOR2] decimal(28,8) NOT NULL,
    [MISCHFAKTOR3] decimal(28,8) NOT NULL,
    [FER_LAUF1] int NOT NULL,
    [FER_LINIE] int NOT NULL,
    [KZ_SERIE] int NOT NULL,
    [FER_UVRAND] int NOT NULL,
    [FER_KSCHUTZ] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [FER_RAHMENTEXT] nvarchar(80) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [LAGER_IDENT] nvarchar(20) NOT NULL,
    [PROVISIONSSATZ] decimal(28,8) NOT NULL,
    [FER_GEST_TYP] int NOT NULL,
    [FER_GEST_ANZ] int NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [REKLA_GRUND] nvarchar(40) NOT NULL,
    [MISCH1] int NOT NULL,
    [MISCH2] int NOT NULL,
    [MISCH3] int NOT NULL,
    [LAG_MIN_BEST] decimal(28,8) NOT NULL,
    [LAG_BEST_MENGE] decimal(28,8) NOT NULL,
    [EK_ZEIT_STK] decimal(28,8) NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [POS_BIT3] int NOT NULL,
    [PROVISIONSSATZ2] decimal(28,8) NOT NULL,
    [AUFBAU_ID] int NOT NULL,
    [ISOAUFBAUKEY] int NOT NULL,
    [PROD_GESTELLNR] varchar(120) NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [POS_TEXT1] nvarchar(40) NULL,
    [POS_TEXT2] nvarchar(40) NULL,
    [POS_TEXT3] nvarchar(40) NULL,
    [POS_TEXT4] nvarchar(40) NULL,
    [POS_TEXT5] nvarchar(40) NULL,
    [AUFTR_REF] int NOT NULL,
    [POS_REF] int NOT NULL,
    [RUECKSCHNITT] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [PMGRP] int NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [GRUPPE] int NOT NULL,
    [KZ_RANDENT] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [PACKREGEL] int NOT NULL,
    [POS_DOCK] nvarchar(80) NULL,
    [PMG_DEF] int NOT NULL,
    [ITM_REGEL_ID] int NOT NULL,
    [ALTERNATIV] int NOT NULL,
    [GRENZTYP4] int NULL,
    [HK_VERWGEMKOST] decimal(28,8) NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [POS_BIT2] int NOT NULL,
    [HK_VERTRGEMKOST] decimal(28,8) NOT NULL,
    [SKIZZEN_DRUCK] int NOT NULL,
    [POS_BLOCK] int NOT NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [HK_GEWINN] decimal(28,8) NOT NULL,
    [VERS_BIT] int NULL,
    [FER_GEST_NR] nvarchar(20) NULL,
    [BOHR_BEZUG] int NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [HK_SONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSELBKOST] decimal(28,8) NOT NULL,
    [HK_SONZU2] decimal(28,8) NOT NULL,
    [HK_EK1] decimal(28,8) NOT NULL,
    [HK_EK2] decimal(28,8) NOT NULL,
    [HK_EK3] decimal(28,8) NOT NULL,
    [HK_VOLLEK1] decimal(28,8) NOT NULL,
    [HK_VOLLEK2] decimal(28,8) NOT NULL,
    [HK_VOLLEK3] decimal(28,8) NOT NULL,
    [HK_VOLLSON] decimal(28,8) NOT NULL,
    [HK_VOLLVERW] decimal(28,8) NOT NULL,
    [HK_VOLLVERT] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU1] decimal(28,8) NOT NULL,
    [HK_VOLLSONZU2] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [HK_VOLLGEWINN] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [KZ_FIXIERT] int NOT NULL,
    [LIORDER_NR] int NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [POS_VERPACKUNG] nvarchar(40) NOT NULL,
    [MINPREIS_PWD] nvarchar(20) NULL,
    [PP_MENGE] decimal(28,8) NOT NULL,
    [PP_BREITE] decimal(28,8) NOT NULL,
    [PP_HOEHE] decimal(28,8) NOT NULL,
    [PP_QM] decimal(28,8) NOT NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [FORMGRP] int NULL,
    [RESTERROR] int NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [CE_KZ] nvarchar(30) NULL,
    [FI_BETR_MWST1] decimal(28,8) NULL,
    [FI_BETR_MWST2] decimal(28,8) NULL,
    [FI_BETR_MWST1_FW] decimal(28,8) NULL,
    [FI_BETR_MWST2_FW] decimal(28,8) NULL,
    [MAKRO_NAME] nvarchar(60) NULL,
    [PROD_FARB_ID] int NOT NULL,
    [PR_GRUPPE] nvarchar(40) NULL,
    [FI_POS_GES_MAN] decimal(28,8) NULL,
    [FI_POS_GES_BIT] int NULL,
    [FI_POS_GES_BEM] nvarchar(80) NULL,
    [FI_POS_GES_RAB] decimal(28,8) NULL,
    [CE_FLAG] int NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_BIT] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [VAR_DATA] nvarchar(max) NULL,
    [VPOS_NR] nvarchar(10) NOT NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [EK_ZUBASIS] decimal(28,8) NULL,
    [FI_POS_GES_AB] decimal(28,8) NULL,
    [PREIS_AEN_BENUTZER] nvarchar(40) NULL,
    [PREIS_AEN_DATUM] datetime NULL,
    [REF_BEST_ID] nvarchar(40) NULL,
    [REF_BEST_POS] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_ID] nvarchar(40) NULL,
    [REF_AUSLIEF_AUFTR_POS] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_ID] nvarchar(40) NULL,
    [REF_ENDKUNDE_BEST_POS] nvarchar(40) NULL,
    [SKIZZEN_DRUCK_SPR] int NOT NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST3] decimal(28,8) NULL,
    [FI_BETR_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_BETR_MWST4] decimal(28,8) NULL,
    [FI_BETR_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_BETR_MWST5] decimal(28,8) NULL,
    [FI_BETR_MWST5_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [PP_UEBERMENGE] decimal(28,8) NULL,
    [PP_UNTERMENGE] decimal(28,8) NULL,
    [PP_WUNSCHMENGE] decimal(28,8) NULL,
    [PRODUCTION_DATE] datetime NULL,
    [FI_PREIS_ME_BASE] decimal(28,8) NULL,
    [PROD_BEZ1_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ2_FOREIGN] nvarchar(60) NULL,
    [PROD_BEZ3_FOREIGN] nvarchar(65) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_POS_EX] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [FI_AVGSTOCK] decimal(28,8) NULL,
    [TAX_CST] nvarchar(3) NOT NULL,
    [TAX_CFOP] nvarchar(4) NOT NULL,
    [TAX_NCM] nvarchar(40) NOT NULL,
    [FI_MWST1_BASIS] decimal(28,8) NULL,
    [FI_MWST2_BASIS] decimal(28,8) NULL,
    [FI_MWST3_BASIS] decimal(28,8) NULL,
    [FI_MWST4_BASIS] decimal(28,8) NULL,
    [FI_MWST5_BASIS] decimal(28,8) NULL,
    [TAX_IVA] decimal(28,8) NULL,
    [FI_PREIS_ME_TAX] decimal(28,8) NULL,
    [EK_PREIS_ME_TAX] decimal(28,8) NULL,
    [PP_MENGE_GES] decimal(28,8) NULL,
    [FI_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_GES] decimal(28,8) NULL,
    [EK_POS_GES_TAX] decimal(28,8) NULL,
    [FI_PREIS_ME_REAL] decimal(28,8) NULL,
    [EK_PREIS_ME_REAL] decimal(28,8) NULL,
    [TAX_BUSINESS_TYPE] nvarchar(80) NOT NULL,
    [FI_PO_ICMS_ST] decimal(28,8) NULL,
    [TAX_ICMS_REDUCTION_ID] int NULL,
    [TAX_GEN_ID] int NULL,
    [LENR] nvarchar(40) NOT NULL,
    [AUFBAU_LENR_ID] int NOT NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [CHANGED] int NULL,
    [POS_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [LAG_LIEFERANT] int NOT NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [DISPATCHORDER_GUID] nvarchar(40) NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [CHECKED] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[MILLET_BW_AUFTR_STKL] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [STL_BEZ] nvarchar(40) NULL,
    [PROD_FARB_BEZ] nvarchar(40) NULL,
    [STL_KURZ_BEZ] nvarchar(20) NULL,
    [STL_STATUS] int NOT NULL,
    [STL_DRUCKPOS] int NULL,
    [STL_MOD] int NULL,
    [STL_BIT] int NOT NULL,
    [STL_PRODART] int NOT NULL,
    [STL_PRODGRP] int NOT NULL,
    [STL_WGR] nvarchar(3) NOT NULL,
    [STL_WGR_STAT] nvarchar(3) NOT NULL,
    [STL_KMB] nvarchar(3) NOT NULL,
    [STL_KMB_STAT] nvarchar(3) NOT NULL,
    [STL_LFM] decimal(28,8) NOT NULL,
    [STL_QM] decimal(28,8) NOT NULL,
    [STL_LFM_FAKT] decimal(28,8) NOT NULL,
    [STL_QM_FAKT] decimal(28,8) NOT NULL,
    [STL_STTXT_NR] int NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [STL_PLANSTK] decimal(28,8) NULL,
    [PR_RABATT] decimal(28,8) NOT NULL,
    [EK_NETTO_GES] decimal(28,8) NOT NULL,
    [EK_ZEIT] decimal(28,8) NOT NULL,
    [STL_KZ_SKONTO] int NULL,
    [PR_PREIS_ME] decimal(28,8) NOT NULL,
    [KZ_LAGER] int NOT NULL,
    [KZ_BESTELLUNG] int NOT NULL,
    [KZ_AUTOZUSCHLAG] int NOT NULL,
    [ID_LIEFERANT] int NOT NULL,
    [FER_STRUKTURVERL] int NOT NULL,
    [FER_STRUKTURSEITE] int NOT NULL,
    [FER_REDUKTION] int NULL,
    [FER_LAUF] int NOT NULL,
    [PR_LISTE] int NOT NULL,
    [PR_SCHLUESSEL] int NOT NULL,
    [PR_EINHEIT] nvarchar(20) NOT NULL,
    [PR_LISTE_EK] int NOT NULL,
    [PR_SCHLUESSEL_EK] int NOT NULL,
    [PR_EINHEIT_EK] nvarchar(20) NOT NULL,
    [PR_MEEINHEIT] nvarchar(20) NOT NULL,
    [PR_PREIS_ME_FW] decimal(28,8) NOT NULL,
    [PR_PREIS_OFFEN] int NULL,
    [PR_PREISDRUCK] int NULL,
    [PR_ZUSCHLAGART] int NOT NULL,
    [PR_BETR_NETTO] decimal(28,8) NOT NULL,
    [PR_BETR_NETTO_FW] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO] decimal(28,8) NOT NULL,
    [PR_BETR_BRUTTO_FW] decimal(28,8) NOT NULL,
    [PR_NETTO_GES] decimal(28,8) NOT NULL,
    [PR_NETTO_GES_FW] decimal(28,8) NOT NULL,
    [EK_QM] decimal(28,8) NOT NULL,
    [EK_QM_FAKT] decimal(28,8) NOT NULL,
    [EK_LFM] decimal(28,8) NOT NULL,
    [EK_LFM_FAKT] decimal(28,8) NOT NULL,
    [EK_PREIS_ME] decimal(28,8) NOT NULL,
    [EK_RABATT] decimal(28,8) NOT NULL,
    [EK_BRUTTO] decimal(28,8) NOT NULL,
    [EK_NETTO] decimal(28,8) NOT NULL,
    [FI_VK_URSPRUNG] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [VER_PREIS_ME] decimal(28,8) NULL,
    [VER_RABATT] decimal(28,8) NULL,
    [PREISRELEVANT] int NOT NULL,
    [PROD_LAGERORT] int NOT NULL,
    [VER_BETR_NETTO] decimal(28,8) NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [KOSTENSTELLE] nvarchar(40) NOT NULL,
    [KOSTENART] nvarchar(40) NOT NULL,
    [FI_ZUBASIS] decimal(28,8) NOT NULL,
    [LIORDER_NR] int NULL,
    [LIORDER_VSG] nvarchar(15) NULL,
    [STL_BIT3] int NOT NULL,
    [KZ_AUTOZUSCHN] int NOT NULL,
    [KZ_SN3] int NOT NULL,
    [PREISRELEVANTEK] int NOT NULL,
    [PROD_RELEVANT] int NOT NULL,
    [BOM_MASTER_ID] int NOT NULL,
    [BEARB_INS] int NOT NULL,
    [LAG_LAGER_ID] int NOT NULL,
    [HASH] nvarchar(16) NOT NULL,
    [STL_BIT2] int NOT NULL,
    [FI_KZ_STEUER] int NOT NULL,
    [FI_KZ_SKONTO] int NOT NULL,
    [VER_BETR_BRUTTO] decimal(28,8) NULL,
    [FER_CUTTING_ONLY] int NOT NULL,
    [VER_NETTO_GES] decimal(28,8) NULL,
    [VER_EINHEIT] nvarchar(20) NULL,
    [HK_MATGEMKOST] decimal(28,8) NOT NULL,
    [HK_VERLUST] decimal(28,8) NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [FER_BESCHAFFARTTYP] int NOT NULL,
    [PREIS_BIT] int NOT NULL,
    [FER_BESCHICH_SEITE] int NOT NULL,
    [PROD_ZUSCHLAG_BIT] int NOT NULL,
    [STL_MENGE] decimal(28,8) NOT NULL,
    [STL_BREITE] decimal(28,8) NOT NULL,
    [STL_HOEHE] decimal(28,8) NOT NULL,
    [STL_DICKE] decimal(28,8) NOT NULL,
    [MINMENGE_EK] decimal(28,8) NULL,
    [MASSRUNDUNG] int NULL,
    [MASSRUNDUNG_EK] int NULL,
    [MASS_BIT] int NULL,
    [PROD_FARB_ID] int NOT NULL,
    [CE_CPIP] nvarchar(30) NULL,
    [CE_LEVEL] int NULL,
    [CE_INFLUENCING] int NULL,
    [FI_ITEM_MWST1] decimal(28,8) NULL,
    [FI_ITEM_MWST2] decimal(28,8) NULL,
    [FI_ITEM_MWST1_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST2_FW] decimal(28,8) NULL,
    [GRENZTYP1] int NULL,
    [GRENZTYP2] int NULL,
    [GRENZTYP3] int NULL,
    [GRENZTYP4] int NULL,
    [EK_AUTO] decimal(28,8) NULL,
    [FI_AUTO] decimal(28,8) NULL,
    [FI_AUTO_FW] decimal(28,8) NULL,
    [FI_MWST1] decimal(28,8) NULL,
    [FI_MWST2] decimal(28,8) NULL,
    [FI_MWST3] decimal(28,8) NULL,
    [FI_MWST4] decimal(28,8) NULL,
    [FI_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST3] decimal(28,8) NULL,
    [FI_ITEM_MWST3_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST4] decimal(28,8) NULL,
    [FI_ITEM_MWST4_FW] decimal(28,8) NULL,
    [FI_ITEM_MWST5] decimal(28,8) NULL,
    [FI_ITEM_MWST5_FW] decimal(28,8) NULL,
    [EK_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT] decimal(28,8) NULL,
    [FI_AUTO_STAT_FW] decimal(28,8) NULL,
    [BOM_BASE_ID] int NULL,
    [PRODUCTION_DATE] datetime NULL,
    [BOM_PUID] int NULL,
    [STL_BEZ_FOREIGN] nvarchar(60) NULL,
    [PR_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_PREIS_ME_BASE] decimal(28,8) NULL,
    [EK_AUTO_EK] decimal(28,8) NULL,
    [EK_AUTO_EK_STAT] decimal(28,8) NULL,
    [BEARB_INS2] int NULL,
    [AREA] nvarchar(40) NULL,
    [AREA_NUMBER] int NULL,
    [DINLR] int NULL,
    [AREA_BOM_PUID] int NULL,
    [RANK] int NULL,
    [STL_ANLIEFERUNG] datetime NULL,
    [CLASSIFIERS] xml NULL,
    [POLINK_GUID] nvarchar(40) NULL,
    [DELIVERY_CONFIRMED] date NULL,
    [BARCODE_START] nvarchar(20) NULL,
    [BARCODE_TYPE] int NULL,
    [PROD_ZUSCHLAG2_BIT] int NULL,
    [EAN] nvarchar(20) NULL
);

CREATE TABLE SYSADM.[OP_MAHNSTUFE] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [MAHNGEBUEHR] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[OP_MAHNUNG] (
    [NR_RECHNUNG] int NOT NULL,
    [OP_ID] int NOT NULL,
    [OP_BETRAG_SOLL] decimal(28,8) NULL,
    [OP_BETRAG_HABEN] decimal(28,8) NULL,
    [OP_IDENT] int NOT NULL,
    [OP_NAME1] nvarchar(40) NULL,
    [OP_NAME2] nvarchar(40) NULL,
    [OP_STRASSE] nvarchar(40) NULL,
    [OP_PLZ] nvarchar(11) NULL,
    [OP_ORT] nvarchar(40) NULL,
    [OP_LAND] nvarchar(6) NULL,
    [OP_TELEFON] nvarchar(40) NULL,
    [OP_FAX] nvarchar(40) NULL,
    [OP_SKONTO] decimal(28,8) NULL,
    [OP_MAHNSTUFE] int NOT NULL,
    [OP_MAHNGEBUEHR] decimal(28,8) NULL,
    [OP_DATUM] date NULL,
    [OP_MITARBEITER] nvarchar(40) NOT NULL,
    [OP_MAHNDATUM] date NULL,
    [ROWID] char(36) NOT NULL,
    [MANDANT] int NOT NULL
);

CREATE TABLE SYSADM.[PA_ATTACH] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [BEM] nvarchar(80) NULL,
    [FILE_NAME] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PA_KALENDER] (
    [ID] int NOT NULL,
    [VON] datetime NOT NULL,
    [BIS] datetime NOT NULL,
    [JAHR] int NOT NULL,
    [MODUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PA_KLASSI_WERTE] (
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PA_MITARB] (
    [ID] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [KZ_ANSPRECHPARTNER] nvarchar(1) NULL,
    [ANREDE] nvarchar(40) NOT NULL,
    [FUNKTION] nvarchar(40) NOT NULL,
    [ABTEILUNG] nvarchar(40) NULL,
    [ZIMMER] nvarchar(40) NULL,
    [INFO] nvarchar(max) NULL,
    [BRIEFKOPF] nvarchar(40) NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [TLF3] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [ORT] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [KZ_PARTNERVERTRIEB] int NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [GEBURTSTAG] date NULL
);

CREATE TABLE SYSADM.[PA_PARTNER] (
    [ID] int NOT NULL,
    [MANDANT] int NOT NULL,
    [MCODE] nvarchar(10) NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [EXTERNE_KNR] nvarchar(20) NULL,
    [ADRESS_KOPF] nvarchar(40) NOT NULL,
    [NAME1] nvarchar(40) NULL,
    [NAME2] nvarchar(40) NULL,
    [NAME3] nvarchar(40) NULL,
    [STRASSE] nvarchar(40) NULL,
    [ORT] nvarchar(40) NULL,
    [PLZ] nvarchar(11) NULL,
    [PLZ_POSTFACH] nvarchar(11) NULL,
    [POSTFACH] nvarchar(40) NULL,
    [LAND] nvarchar(6) NOT NULL,
    [TLF1] nvarchar(40) NULL,
    [TLF2] nvarchar(40) NULL,
    [FAX] nvarchar(40) NULL,
    [MAIL] nvarchar(254) NULL,
    [KURZINFO] nvarchar(max) NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [ZAHLBED] nvarchar(100) NOT NULL,
    [ZAHLWEG] nvarchar(40) NOT NULL,
    [ZAHL_FB_KZ] int NOT NULL,
    [ZAHL_RLEG_TAG] int NULL,
    [ZAHL_TAG1] int NOT NULL,
    [LIEFERBED] nvarchar(40) NOT NULL,
    [TOUR] nvarchar(40) NOT NULL,
    [AUSW_TOUR] nvarchar(40) NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [SACHBEARB] nvarchar(40) NOT NULL,
    [BONITAET] nvarchar(40) NOT NULL,
    [RANDABZUG] decimal(28,8) NULL,
    [ANLIEF_WERT] decimal(28,8) NOT NULL,
    [AUTO_ZUSCHL_WERT] decimal(28,8) NOT NULL,
    [HK_SONZU] decimal(28,8) NOT NULL,
    [FS_VORG_DOK_NR] int NOT NULL,
    [ZWANG_REF] int NOT NULL,
    [KONTO] nvarchar(20) NULL,
    [SAMMEL_KTO] nvarchar(20) NULL,
    [UST_ID] nvarchar(30) NULL,
    [KZ_SPRACH] int NOT NULL,
    [KZ_PRIVAT] int NULL,
    [KZ_SAMMELRE] int NULL,
    [KZ_TEILFAK] int NULL,
    [KZ_TEILLIEF] int NULL,
    [OP_DATUM] date NULL,
    [OP_TAGE] int NOT NULL,
    [KREDITMITFILIALE] int NOT NULL,
    [KZ_KLEINGLAS] int NULL,
    [KZ_KREDITPRUEF] int NULL,
    [KZ_POSTFACH] int NULL,
    [KZ_STATUS] int NULL,
    [KZ_MASSEINH] int NOT NULL,
    [KZ_KINFO] int NULL,
    [KZ_MAHNSPERRE] int NULL,
    [KZ_WIEDERVORLAGE] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [KZ_BANKEINZ] int NULL,
    [KZ_NETTOPREISE] int NULL,
    [KZ_DEF_FIL] int NULL,
    [KZ_FAXVERSAND] int NOT NULL,
    [ENTFERNUNG] decimal(28,8) NOT NULL,
    [TOUREN_RANGFOLGE] int NOT NULL,
    [STRUKT_SEITE] int NOT NULL,
    [IBAN] nvarchar(40) NULL,
    [MAHN_TEXT] nvarchar(40) NOT NULL,
    [ZAHL_BLZ] nvarchar(20) NULL,
    [ZAHL_KTN] nvarchar(20) NULL,
    [EIL_ZUSCHL_TAGE] int NULL,
    [PRIORITAET] nvarchar(40) NOT NULL,
    [MWST_KZ1] int NOT NULL,
    [MWST_KZ2] int NOT NULL,
    [HAUPT_ID] int NOT NULL,
    [KZ_FIBU_MANDANT] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNGBR] int NOT NULL,
    [RUNDUNGHOE] int NOT NULL,
    [DFUE_ART] int NOT NULL,
    [DFUE_LOG] int NOT NULL,
    [DFUE_DATEI] nvarchar(80) NULL,
    [DFUE_BATCH_DATEI] nvarchar(max) NULL,
    [MOD_SYM] int NULL,
    [ETIK_LAYOUT] int NOT NULL,
    [ZAHL_TAG2] int NOT NULL,
    [ZAHL_TAG3] int NOT NULL,
    [SORT_KRIT] int NOT NULL,
    [SKONTO_BERTYP] int NOT NULL,
    [DFUE_LOG_TIMEOUT] int NOT NULL,
    [ETIK_STEUERUNG] nvarchar(10) NULL,
    [PROVINZ] nvarchar(20) NOT NULL,
    [FS_AUFTR_REJECT] int NOT NULL,
    [DFUE_SAVE] nvarchar(80) NULL,
    [DFUE_PROTOKOLL] nvarchar(80) NULL,
    [FS_RESTRICT] int NULL,
    [PACKREGEL] int NOT NULL,
    [PMG_DEF] int NOT NULL,
    [FS_EXCHANGE] int NOT NULL,
    [FILIALNR] int NULL,
    [FS_STL] int NULL,
    [KURS_FIX] int NOT NULL,
    [VERPACKUNG] nvarchar(40) NOT NULL,
    [PREISDRUCK] int NOT NULL,
    [PIN] int NULL,
    [EDI_FILE_FROM] int NOT NULL,
    [EDI_FILE_TO] int NOT NULL,
    [EDI_FILE_ACT] int NOT NULL,
    [KZ_DIVERS] int NOT NULL,
    [SZR_PRODART] nvarchar(40) NULL,
    [FW_ART] int NOT NULL,
    [FS_PRICING_ALLOWED] int NULL,
    [FS_FREE_ALLOWED] int NULL,
    [TEMP] int NULL,
    [AUTO_ZUSCHL_KZ] int NOT NULL,
    [AUTO_ZUSCHL_DATE] date NULL,
    [SZR_PRODGRP] nvarchar(40) NULL,
    [RECH_FAX] nvarchar(40) NULL,
    [FS_CHANGE_ISO] int NOT NULL,
    [BRUTTOTAG2] int NOT NULL,
    [BRUTTOTAG3] int NOT NULL,
    [BRUTTOTAG4] int NOT NULL,
    [BRUTTOTAG5] int NOT NULL,
    [STEUERNUMMER] nvarchar(40) NULL,
    [SZR_VARIANTE] int NULL,
    [ADIENST2] nvarchar(40) NOT NULL,
    [SALDO_J] decimal(28,8) NULL,
    [KREDIT_LIMIT] decimal(28,8) NULL,
    [KREDIT_LIMIT1] decimal(28,8) NULL,
    [OBLIGO] decimal(28,8) NULL,
    [SPRACH_BASIS] int NOT NULL,
    [FER_RAHMENNR] int NOT NULL,
    [ZUSCHLAG_BIT] int NOT NULL,
    [RE_DATEI] nvarchar(80) NULL,
    [RE_BATCH_DATEI] nvarchar(max) NULL,
    [RE_PROTOKOLL] nvarchar(80) NULL,
    [RE_SAVE] nvarchar(80) NULL,
    [RE_ART] int NULL,
    [STEUERMODUS] int NOT NULL,
    [RE_FILE_FROM] int NOT NULL,
    [RE_FILE_ACT] int NOT NULL,
    [RE_FILE_TO] int NOT NULL,
    [FS_MAIN_REF] int NOT NULL,
    [CE_FLAG] int NOT NULL,
    [EDI_FORMEL] int NOT NULL,
    [FX_EDI] int NOT NULL,
    [FX_CUSTOMER] int NOT NULL,
    [EDI_DIM_FRACTION] int NULL,
    [HOMEPAGE] nvarchar(254) NULL,
    [TRANSACTION_TIME] datetime NULL,
    [MWST_KZ3] int NOT NULL,
    [MWST_KZ4] int NOT NULL,
    [MWST_KZ5] int NOT NULL,
    [UMSATZ_VERTR2] decimal(28,8) NOT NULL,
    [KZ_STEUERFLAG] int NULL,
    [ZAHLUNGSTAGVERSCHIEBUNG] int NULL,
    [BANK_ID] int NOT NULL,
    [KZ_ICMS_CONTR] int NULL,
    [KZ_STATE_REGISTER] int NULL,
    [KZ_ICMS_ST] int NULL,
    [TRANSPORT_ID] int NOT NULL,
    [TRANSPORT_RESPONSE] int NULL,
    [VALOR_UNITARIO_MODE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [TRANSFER_LE] int NOT NULL,
    [MINMENGE_EK] decimal(28,8) NOT NULL,
    [MINMENGE_VK] decimal(28,8) NOT NULL,
    [MAIL_ANG] nvarchar(254) NULL,
    [MAIL_AB] nvarchar(254) NULL,
    [MAIL_LIEF] nvarchar(254) NULL,
    [MAIL_RECH] nvarchar(254) NULL,
    [MAIL_GUT] nvarchar(254) NULL,
    [FS_ESG_EDGE] int NOT NULL,
    [FAX_ANG] nvarchar(40) NULL,
    [FAX_AB] nvarchar(40) NULL,
    [FAX_LIEF] nvarchar(40) NULL,
    [FAX_GUT] nvarchar(40) NULL,
    [FS_CHECK_BEAPARAM] int NOT NULL,
    [FS_INDIVIDUAL_TEXTS] int NOT NULL,
    [FS_NO_SURCHARGE_CALC] int NOT NULL,
    [DFUE_SINGLE] int NULL,
    [DFUE_BARCODE] int NULL,
    [ZUSCHLAG2_BIT] int NOT NULL,
    [MAIL_MODE] int NOT NULL,
    [KREDIT_LIMIT_NET] float NULL,
    [KREDIT_LIMIT1_NET] float NULL,
    [EULER_ID] int NULL,
    [FS_NO_SHAPE_DETECTION] int NULL,
    [BUSINESSCASE] int NOT NULL,
    [IQUOTE_CHARTER] int NULL
);

CREATE TABLE SYSADM.[PA_PARTNER_KAPA] (
    [ID] int NOT NULL,
    [TAG] int NOT NULL,
    [PRDKTART] nvarchar(40) NOT NULL,
    [KAPA] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PA_PARTNER_TXT] (
    [ID] int NOT NULL,
    [LFD_NR] int NOT NULL,
    [TEXT_ID] int NULL,
    [DRUCK_KZ] nvarchar(1) NOT NULL,
    [MOD] int NULL,
    [BEZ] nvarchar(max) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PA_PARTNER_VOR] (
    [ID] int NOT NULL,
    [VORGANG] int NOT NULL,
    [PARENT_ID] int NOT NULL,
    [PROJEKT] nvarchar(40) NOT NULL,
    [ERLEDIGT] int NULL,
    [E_DATUM] datetime NULL,
    [E_ART] nvarchar(40) NOT NULL,
    [E_WER] nvarchar(40) NULL,
    [E_BEM] nvarchar(254) NULL,
    [E_TEXT] nvarchar(max) NULL,
    [A_DATUM] datetime NULL,
    [A_ART] nvarchar(40) NOT NULL,
    [A_WER] nvarchar(40) NULL,
    [A_BEM] nvarchar(254) NULL,
    [A_TEXT] nvarchar(max) NULL,
    [F_DATUM] datetime NULL,
    [F_ART] nvarchar(40) NOT NULL,
    [F_WER] nvarchar(40) NULL,
    [F_BEM] nvarchar(254) NULL,
    [F_TEXT] nvarchar(max) NULL,
    [DATUM] date NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PAMA_KLASSI_WERTE] (
    [MA_ID] int NOT NULL,
    [ID] int NOT NULL,
    [K_ID] int NOT NULL,
    [K_WERT] nvarchar(60) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PD_AWBAR] (
    [ID] int NOT NULL,
    [POS_NR] int NOT NULL,
    [POINT] int NOT NULL,
    [ANZAHL] int NULL,
    [DATUM] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [ANZAHL_BRUCH] int NULL,
    [ANZAHL_SOLL] int NULL
);

CREATE TABLE SYSADM.[PD_PROD_DEF] (
    [PRODUKT] int NOT NULL,
    [POINT] int NOT NULL,
    [RANG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PD_PROD_POINT] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NOT NULL,
    [FREMD_KEY] nvarchar(10) NOT NULL,
    [STATUSPUNKT] int NOT NULL,
    [STATUS_KEY] int NOT NULL,
    [BEST_KZ] int NOT NULL,
    [ART] int NOT NULL,
    [BDE_TYP] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PD_PRODGRP_DEF] (
    [PROD_ART] nvarchar(40) NOT NULL,
    [PROD_GRP] nvarchar(40) NOT NULL,
    [POINT] int NOT NULL,
    [RANG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PD_PROTOKOLL] (
    [DATUM] datetime NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TYP] int NOT NULL,
    [TEXT] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PM_KOMM_DOCK] (
    [KU_ID] int NOT NULL,
    [KOMM] int NOT NULL,
    [DOCK] int NOT NULL,
    [PACKREGEL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PM_PACK_SORT] (
    [PACK_ID] int NOT NULL,
    [SORT_SCHLS] int NOT NULL,
    [SORT_RANG] int NOT NULL,
    [SORT_MODE] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PM_PACKREGEL] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [PACKREGEL_ID] int NOT NULL,
    [TRA_REGEL_ID] int NOT NULL,
    [KZ_AUTO] int NOT NULL,
    [PARAM05] decimal(28,8) NULL,
    [PARAM06] decimal(28,8) NULL,
    [PARAM07] decimal(28,8) NULL,
    [PARAM08] decimal(28,8) NULL,
    [PARAM09] decimal(28,8) NULL,
    [PARAM10] decimal(28,8) NULL,
    [PARAM11] decimal(28,8) NULL,
    [PARAM12] decimal(28,8) NULL,
    [PARAM13] decimal(28,8) NULL,
    [PARAM14] decimal(28,8) NULL,
    [PARAM15] decimal(28,8) NULL,
    [PARAM16] decimal(28,8) NULL,
    [PARAM17] decimal(28,8) NULL,
    [PARAM18] decimal(28,8) NULL,
    [PARAM19] decimal(28,8) NULL,
    [PARAM20] decimal(28,8) NULL,
    [PARAM01] decimal(28,8) NULL,
    [PARAM02] decimal(28,8) NULL,
    [PARAM03] decimal(28,8) NULL,
    [PARAM04] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PM_PMG] (
    [LIEF_ID] int NOT NULL,
    [LIEF_DATUM] date NOT NULL,
    [PACKREGEL] int NOT NULL,
    [KOMM] nvarchar(80) NOT NULL,
    [DOCK] nvarchar(80) NOT NULL,
    [KZ_MODELL] int NOT NULL,
    [PACKMITTEL] int NOT NULL,
    [GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PM_PMG_DEF] (
    [NUMMER] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [BIT] int NOT NULL,
    [LIEF_MINUS] int NOT NULL,
    [LIEF_PLUS] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_GRU] (
    [TXT] nvarchar(6) NOT NULL,
    [BEM] nvarchar(40) NULL,
    [NUMMER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_ISO_AUFBAU] (
    [GLAS1] int NOT NULL,
    [ABST1] int NOT NULL,
    [GAS1] int NOT NULL,
    [GLAS2] int NOT NULL,
    [ABST2] int NOT NULL,
    [GAS2] int NOT NULL,
    [GLAS3] int NOT NULL,
    [PREIS_ID] int NOT NULL,
    [REVERSIBEL] int NOT NULL,
    [ROWID] nvarchar(36) NULL,
    [ISO_PRODUKT] int NOT NULL
);

CREATE TABLE SYSADM.[PR_KG_PREIS] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_KG_PREISKPF] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [SCHLS_TXT] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FORM] int NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [RUNDUNG1] int NULL,
    [RUNDUNG2] int NULL,
    [RUNDUNG3] int NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ABW_PRDKTGRP] int NOT NULL,
    [MIN_BREITE] int NOT NULL,
    [MIN_HOEHE] int NOT NULL,
    [SCHLS_NXT] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BRU_PREIS_MIN] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_KU_PREIS] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_KU_PREISKPF] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [SCHLS_TXT] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FORM] int NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [RUNDUNG1] int NULL,
    [RUNDUNG2] int NULL,
    [RUNDUNG3] int NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ABW_PRDKTGRP] int NOT NULL,
    [MIN_BREITE] int NOT NULL,
    [MIN_HOEHE] int NOT NULL,
    [SCHLS_NXT] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BRU_PREIS_MIN] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_LG_PREIS] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_LG_PREISKPF] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [SCHLS_TXT] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FORM] int NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [RUNDUNG1] int NULL,
    [RUNDUNG2] int NULL,
    [RUNDUNG3] int NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ABW_PRDKTGRP] int NOT NULL,
    [MIN_BREITE] int NOT NULL,
    [MIN_HOEHE] int NOT NULL,
    [SCHLS_NXT] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BRU_PREIS_MIN] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_LI_PREIS] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_LI_PREISKPF] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [SCHLS_TXT] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FORM] int NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [RUNDUNG1] int NULL,
    [RUNDUNG2] int NULL,
    [RUNDUNG3] int NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ABW_PRDKTGRP] int NOT NULL,
    [MIN_BREITE] int NOT NULL,
    [MIN_HOEHE] int NOT NULL,
    [SCHLS_NXT] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BRU_PREIS_MIN] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_LISTEN] (
    [LISTE_ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [BEM] nvarchar(80) NULL,
    [VOM] date NULL,
    [BIS] date NULL,
    [DEF_LISTE_VK] int NULL,
    [DEF_LISTE_EK] int NULL,
    [ISO_LISTE_VK] int NULL,
    [BEA_LISTE_VK] int NULL,
    [BEA_LISTE_EK] int NULL,
    [MOD_LISTE_VK] int NULL,
    [MOD_LISTE_EK] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_MISCH_KMB] (
    [ID1] int NOT NULL,
    [ID2] int NOT NULL,
    [ID3] int NOT NULL,
    [PREIS_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_MOD_ZUSCH] (
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [MOD_NR] int NOT NULL,
    [ZUSCH_SEG5] decimal(28,8) NOT NULL,
    [ZUSCH_SEG6] decimal(28,8) NOT NULL,
    [ZUSCH_SEG7] decimal(28,8) NOT NULL,
    [ZUSCH_SEG8] decimal(28,8) NOT NULL,
    [MIN_PREIS] decimal(28,8) NOT NULL,
    [MIN_MENGE] decimal(28,8) NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ZUSCH_SEG1] decimal(28,8) NOT NULL,
    [ZUSCH_SEG2] decimal(28,8) NOT NULL,
    [ZUSCH_SEG3] decimal(28,8) NOT NULL,
    [ZUSCH_SEG4] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_PREIS] (
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PREIS_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [PREIS] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_PREISKPF] (
    [ID] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [SCHLS_TXT] int NOT NULL,
    [BEZ] nvarchar(80) NULL,
    [FORM] int NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [RUNDUNG1] int NULL,
    [RUNDUNG2] int NULL,
    [RUNDUNG3] int NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ABW_PRDKTGRP] int NOT NULL,
    [MIN_BREITE] int NOT NULL,
    [MIN_HOEHE] int NOT NULL,
    [SCHLS_NXT] int NOT NULL,
    [FORMEL] nvarchar(max) NULL,
    [WAEHRUNG] nvarchar(8) NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NOT NULL,
    [MINMENGE] decimal(28,8) NOT NULL,
    [BRU_PREIS_MIN] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_PRGRU_GLAS] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [PRDKT_ID] int NOT NULL,
    [ISOAUST_PRGR] nvarchar(6) NOT NULL,
    [VSGAUST_PRGR] nvarchar(6) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_SCHLUESSEL] (
    [SCHLS_ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [KZ_GESPERRT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_SPROSSENPREISE] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(80) NULL,
    [ELEMENT_ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_VERSICH] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [KALKART] int NOT NULL,
    [ENTSORG] int NOT NULL,
    [REPARATUR] int NOT NULL,
    [PRDART_ART1] nvarchar(40) NOT NULL,
    [PRDART_PROD1] int NOT NULL,
    [PRDART_ENT1] int NOT NULL,
    [PRDART_ART2] nvarchar(40) NOT NULL,
    [PRDART_PROD2] int NOT NULL,
    [PRDART_ENT2] int NOT NULL,
    [PRDART_ART3] nvarchar(40) NOT NULL,
    [PRDART_PROD3] int NOT NULL,
    [PRDART_ENT3] int NOT NULL,
    [PRDART_ART4] nvarchar(40) NOT NULL,
    [PRDART_PROD4] int NOT NULL,
    [PRDART_ENT4] int NOT NULL,
    [PRDART_ART5] nvarchar(40) NOT NULL,
    [PRDART_PROD5] int NOT NULL,
    [PRDART_ENT5] int NOT NULL,
    [PRDGRP_ART1] nvarchar(40) NOT NULL,
    [PRDGRP_GRP1] nvarchar(40) NOT NULL,
    [PRDGRP_PROD1] int NOT NULL,
    [PRDGRP_ENT1] int NOT NULL,
    [PRDGRP_ART2] nvarchar(40) NOT NULL,
    [PRDGRP_GRP2] nvarchar(40) NOT NULL,
    [PRDGRP_PROD2] int NOT NULL,
    [PRDGRP_ENT2] int NOT NULL,
    [PRDGRP_ART3] nvarchar(40) NOT NULL,
    [PRDGRP_GRP3] nvarchar(40) NOT NULL,
    [PRDGRP_PROD3] int NOT NULL,
    [PRDGRP_ENT3] int NOT NULL,
    [PRDGRP_ART4] nvarchar(40) NOT NULL,
    [PRDGRP_GRP4] nvarchar(40) NOT NULL,
    [PRDGRP_PROD4] int NOT NULL,
    [PRDGRP_ENT4] int NOT NULL,
    [PRDGRP_ART5] nvarchar(40) NOT NULL,
    [PRDGRP_GRP5] nvarchar(40) NOT NULL,
    [PRDGRP_PROD5] int NOT NULL,
    [PRDGRP_ENT5] int NOT NULL,
    [PRDGRP_ART6] nvarchar(40) NOT NULL,
    [PRDGRP_GRP6] nvarchar(40) NOT NULL,
    [PRDGRP_PROD6] int NOT NULL,
    [PRDGRP_ENT6] int NOT NULL,
    [PRDGRP_ART7] nvarchar(40) NOT NULL,
    [PRDGRP_GRP7] nvarchar(40) NOT NULL,
    [PRDGRP_PROD7] int NOT NULL,
    [PRDGRP_ENT7] int NOT NULL,
    [PRDGRP_ART8] nvarchar(40) NOT NULL,
    [PRDGRP_GRP8] nvarchar(40) NOT NULL,
    [PRDGRP_PROD8] int NOT NULL,
    [PRDGRP_ENT8] int NOT NULL,
    [PRDGRP_ART9] nvarchar(40) NOT NULL,
    [PRDGRP_GRP9] nvarchar(40) NOT NULL,
    [PRDGRP_PROD9] int NOT NULL,
    [PRDGRP_ENT9] int NOT NULL,
    [PRDGRP_ART10] nvarchar(40) NOT NULL,
    [PRDGRP_GRP10] nvarchar(40) NOT NULL,
    [PRDGRP_PROD10] int NOT NULL,
    [PRDGRP_ENT10] int NOT NULL,
    [EINSATZ1] int NOT NULL,
    [EINSATZ2] int NOT NULL,
    [KLEBER_IN] int NOT NULL,
    [ZEMENT_IN] int NOT NULL,
    [KLEBER_EX] int NOT NULL,
    [ZEMENT_EX] int NOT NULL,
    [NOTVERGL] int NOT NULL,
    [WERKSTATT] decimal(28,8) NOT NULL,
    [NOTEINSATZ1] int NOT NULL,
    [DACH] decimal(28,8) NOT NULL,
    [NOTEINSATZ2] int NOT NULL,
    [BIS1] decimal(28,8) NOT NULL,
    [NOTENTSORG] int NOT NULL,
    [FAHRTKOSTEN] int NOT NULL,
    [BIS2] decimal(28,8) NOT NULL,
    [BIS3] decimal(28,8) NOT NULL,
    [BIS4] decimal(28,8) NOT NULL,
    [ZUSCHLAG1] decimal(28,8) NOT NULL,
    [ZUSCHLAG2] decimal(28,8) NOT NULL,
    [ZUSCHLAG3] decimal(28,8) NOT NULL,
    [ZUSCHLAG4] decimal(28,8) NOT NULL,
    [AB] decimal(28,8) NOT NULL,
    [ABSCHLAG] decimal(28,8) NOT NULL,
    [SON_AB1] decimal(28,8) NOT NULL,
    [SON_ABSCHLAG1] decimal(28,8) NOT NULL,
    [ZUSCHLAG_MAX] int NOT NULL,
    [SON_AB2] decimal(28,8) NOT NULL,
    [SON_ABSCHLAG2] decimal(28,8) NOT NULL,
    [MIN_EIN1] decimal(28,8) NOT NULL,
    [MIN_EIN2] decimal(28,8) NOT NULL,
    [MIN_EIN_WERK1] decimal(28,8) NOT NULL,
    [MIN_EIN_MAX] decimal(28,8) NOT NULL,
    [MIN_EIN_WERK2] decimal(28,8) NOT NULL,
    [NOTVERGL_MIN] decimal(28,8) NOT NULL,
    [NOTEIN1_MIN] decimal(28,8) NOT NULL,
    [NOTEIN2_MIN] decimal(28,8) NOT NULL,
    [FAHRTMAX] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_VK_EK_LISTEN] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [DEF_SCHLS_VK] int NULL,
    [DEF_SCHLS_EK] int NULL,
    [ISO_SCHLS_VK] int NULL,
    [ISO_SCHLS_EK] int NULL,
    [ID] int NOT NULL,
    [MOD_SCHLS_VK] int NULL,
    [MOD_SCHLS_EK] int NULL,
    [BEA_SCHLS_VK] int NULL,
    [BEA_SCHLS_EK] int NULL,
    [ISO_BASIS_TAB] int NOT NULL,
    [HK_CALC] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_ZUSCH_ART] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PR_ZUSCHLAEGE] (
    [MANDANT] int NOT NULL,
    [ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [GUELTIG_AB] datetime NOT NULL,
    [GUELTIG_BIS] datetime NOT NULL,
    [POS_NR] int NOT NULL,
    [KUNDE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [ID2] int NOT NULL,
    [FIX] int NOT NULL,
    [FIX_ABSTATUS] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_BREAKAGE] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [KEYINDEX] int NOT NULL,
    [SUB_POS] int NOT NULL,
    [BOM_NODE] int NULL,
    [MENGE] int NOT NULL,
    [BREAKAGEDATE] datetime NOT NULL,
    [JOBNUMBER_ORG] int NULL,
    [JOBNUMBER_NEW] int NULL,
    [IS_BREAKAGE] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BREAKAGE_REASON] int NULL,
    [BREAKAGE_REGISTRATION] int NULL,
    [BREAKAGE_FROMSCANNER] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_CUTCORRECT] (
    [PRODUKT_BEARB] int NOT NULL,
    [AGG] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [VALUE] decimal(28,8) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_FORMULA] (
    [FORMULA] int NOT NULL,
    [NAME] nvarchar(60) NOT NULL,
    [DESCRIPTION] nvarchar(60) NOT NULL,
    [TEXT] nvarchar(max) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOB] (
    [JOBNUMBER] int NOT NULL,
    [DESCRIPTION] nvarchar(254) NOT NULL,
    [STATUS] int NOT NULL,
    [RACKORGA] int NOT NULL,
    [RACKORGANAME] nvarchar(80) NOT NULL,
    [RACKDEPTHFACTOR] int NOT NULL,
    [CREATIONDATE] datetime NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [PARTIALDELIVERED] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEM] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [BOM_NODE] int NULL,
    [BOM_PRODUKT] int NOT NULL,
    [PRODUKTART] int NOT NULL,
    [AGG] int NULL,
    [LASTAGG] int NULL,
    [STKLPOS] nvarchar(30) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] decimal(28,8) NOT NULL,
    [HOEHE] decimal(28,8) NOT NULL,
    [BREITE_CUT] decimal(28,8) NOT NULL,
    [HOEHE_CUT] decimal(28,8) NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [MOD_NUMMER] int NOT NULL,
    [MOD_NUMMER_CUT] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [TURN_UNIT] int NOT NULL,
    [LOTTYPE] int NOT NULL,
    [LTPRDSQ] int NOT NULL,
    [RACKGROUP] int NULL,
    [LOGICALRACK] int NOT NULL,
    [LOGICALRACKSEQUENCE] int NOT NULL,
    [LOGICALRACKLOTTYPE] int NOT NULL,
    [GROUPSORTED] int NOT NULL,
    [SORTEDINGROUP] int NOT NULL,
    [RACK] int NOT NULL,
    [GROUPNRCREATED] int NOT NULL,
    [PRODUCTIONSEQUENCECREATED] int NOT NULL,
    [GROUPNR] int NOT NULL,
    [GROUPSEQUENCENR] int NOT NULL,
    [RACKTYPE] int NOT NULL,
    [SEQUENCE_OPTIRUN] int NOT NULL,
    [OPTIMIZATION] int NULL,
    [MANUALCUTTING] int NOT NULL,
    [STACKNUMBER] int NOT NULL,
    [STACKPOSITION] int NOT NULL,
    [SHEETS] int NOT NULL,
    [EQUALITY] int NOT NULL,
    [MIRROR] int NOT NULL,
    [ROTATE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [OPTIGRP] int NOT NULL,
    [OPTIGRPPOS] int NOT NULL,
    [OPTIPRIO] int NOT NULL,
    [MENGE_USERCHANGE] int NOT NULL,
    [BREITE_USERCHANGE] int NOT NULL,
    [HOEHE_USERCHANGE] int NOT NULL,
    [SHAPE_USERCHANGE] int NOT NULL,
    [TRIM_USERCHANGE] int NOT NULL,
    [MENGE_CUT] decimal(28,8) NOT NULL,
    [NV_SORTID] int NULL,
    [NV_NAME] nvarchar(40) NULL,
    [KEYINDEX] int NOT NULL,
    [SUB_POS] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEMEDGESHIFT] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [EDGECOUNT] int NOT NULL,
    [EDGESHIFT1] decimal(28,8) NULL,
    [EDGESHIFT2] decimal(28,8) NULL,
    [EDGESHIFT3] decimal(28,8) NULL,
    [EDGESHIFT4] decimal(28,8) NULL,
    [EDGESHIFT5] decimal(28,8) NULL,
    [EDGESHIFT6] decimal(28,8) NULL,
    [EDGESHIFT7] decimal(28,8) NULL,
    [EDGESHIFT8] decimal(28,8) NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [KEYINDEX] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEMFRAME] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [MOD_NUMMER] int NOT NULL,
    [FRAME_LENGTH1] decimal(28,8) NOT NULL,
    [FRAME_LENGTH2] decimal(28,8) NOT NULL,
    [FRAME_LENGTH3] decimal(28,8) NOT NULL,
    [FRAME_LENGTH4] decimal(28,8) NOT NULL,
    [FRAME_LENGTH5] decimal(28,8) NOT NULL,
    [FRAME_LENGTH6] decimal(28,8) NOT NULL,
    [FRAME_LENGTH7] decimal(28,8) NOT NULL,
    [FRAME_LENGTH8] decimal(28,8) NOT NULL,
    [FRAME_LENGTH9] decimal(28,8) NOT NULL,
    [FRAME_LENGTH10] decimal(28,8) NOT NULL,
    [FRAME_LENGTH11] decimal(28,8) NOT NULL,
    [FRAME_LENGTH12] decimal(28,8) NOT NULL,
    [FRAME_LENGTH13] decimal(28,8) NOT NULL,
    [FRAME_LENGTH14] decimal(28,8) NOT NULL,
    [FRAME_LENGTH15] decimal(28,8) NOT NULL,
    [FRAME_ANGLE1] decimal(28,8) NOT NULL,
    [FRAME_ANGLE2] decimal(28,8) NOT NULL,
    [FRAME_ANGLE3] decimal(28,8) NOT NULL,
    [FRAME_ANGLE4] decimal(28,8) NOT NULL,
    [FRAME_ANGLE5] decimal(28,8) NOT NULL,
    [FRAME_ANGLE6] decimal(28,8) NOT NULL,
    [FRAME_ANGLE7] decimal(28,8) NOT NULL,
    [FRAME_ANGLE8] decimal(28,8) NOT NULL,
    [FRAME_ANGLE9] decimal(28,8) NOT NULL,
    [FRAME_ANGLE10] decimal(28,8) NOT NULL,
    [FRAME_ANGLE11] decimal(28,8) NOT NULL,
    [FRAME_ANGLE12] decimal(28,8) NOT NULL,
    [FRAME_ANGLE13] decimal(28,8) NOT NULL,
    [FRAME_ANGLE14] decimal(28,8) NOT NULL,
    [FRAME_ANGLE15] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [KEYINDEX] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEMSHAPE] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [TYPE] int NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [MOD_NUMMER] int NOT NULL,
    [MOD_PARAM1] decimal(28,8) NULL,
    [MOD_PARAM2] decimal(28,8) NULL,
    [MOD_PARAM3] decimal(28,8) NULL,
    [MOD_PARAM4] decimal(28,8) NULL,
    [MOD_PARAM5] decimal(28,8) NULL,
    [MOD_PARAM6] decimal(28,8) NULL,
    [MOD_PARAM7] decimal(28,8) NULL,
    [MOD_PARAM8] decimal(28,8) NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BLOCKNAME] nvarchar(254) NOT NULL,
    [SNFILENAME] nvarchar(254) NOT NULL,
    [TRIM1] decimal(28,8) NULL,
    [TRIM2] decimal(28,8) NULL,
    [TRIM3] decimal(28,8) NULL,
    [TRIM4] decimal(28,8) NULL,
    [KEYINDEX] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEMSHAPEINFO] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [KEYINDEX] int NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [EDGECOUNT] int NOT NULL,
    [EDGEDELETIONMODE] int NOT NULL,
    [EDGEDELETION1] decimal(28,8) NOT NULL,
    [EDGEDELETION2] decimal(28,8) NOT NULL,
    [EDGEDELETION3] decimal(28,8) NOT NULL,
    [EDGEDELETION4] decimal(28,8) NOT NULL,
    [EDGEDELETION5] decimal(28,8) NOT NULL,
    [EDGEDELETION6] decimal(28,8) NOT NULL,
    [EDGEDELETION7] decimal(28,8) NOT NULL,
    [EDGEDELETION8] decimal(28,8) NOT NULL,
    [STEP1] decimal(28,8) NOT NULL,
    [STEP2] decimal(28,8) NOT NULL,
    [STEP3] decimal(28,8) NOT NULL,
    [STEP4] decimal(28,8) NOT NULL,
    [STEP5] decimal(28,8) NOT NULL,
    [STEP6] decimal(28,8) NOT NULL,
    [STEP7] decimal(28,8) NOT NULL,
    [STEP8] decimal(28,8) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBITEMTEMP] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [VARIANTNUMBER] int NOT NULL,
    [JOBNUMBER] int NOT NULL,
    [BOM_NODE] int NULL,
    [BOM_PRODUKT] int NOT NULL,
    [PRODUKTART] int NOT NULL,
    [AGG] int NULL,
    [LASTAGG] int NULL,
    [STKLPOS] nvarchar(30) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [BREITE] decimal(28,8) NOT NULL,
    [HOEHE] decimal(28,8) NOT NULL,
    [BREITE_CUT] decimal(28,8) NOT NULL,
    [HOEHE_CUT] decimal(28,8) NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [MOD_NUMMER] int NOT NULL,
    [MOD_NUMMER_CUT] int NOT NULL,
    [FER_BESCHAFFARTNR] int NOT NULL,
    [TURN_UNIT] int NOT NULL,
    [LOTTYPE] int NOT NULL,
    [LTPRDSQ] int NOT NULL,
    [RACKGROUP] int NULL,
    [LOGICALRACK] int NOT NULL,
    [LOGICALRACKSEQUENCE] int NOT NULL,
    [LOGICALRACKLOTTYPE] int NOT NULL,
    [GROUPSORTED] int NOT NULL,
    [SORTEDINGROUP] int NOT NULL,
    [RACK] int NOT NULL,
    [GROUPNRCREATED] int NOT NULL,
    [PRODUCTIONSEQUENCECREATED] int NOT NULL,
    [GROUPNR] int NOT NULL,
    [GROUPSEQUENCENR] int NOT NULL,
    [RACKTYPE] int NOT NULL,
    [SEQUENCE_OPTIRUN] int NOT NULL,
    [OPTIMIZATION] int NULL,
    [MANUALCUTTING] int NOT NULL,
    [STACKNUMBER] int NOT NULL,
    [STACKPOSITION] int NOT NULL,
    [SHEETS] int NOT NULL,
    [EQUALITY] int NOT NULL,
    [MIRROR] int NOT NULL,
    [ROTATE] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [OPTIGRP] int NOT NULL,
    [OPTIGRPPOS] int NOT NULL,
    [OPTIPRIO] int NOT NULL,
    [MENGE_USERCHANGE] int NOT NULL,
    [BREITE_USERCHANGE] int NOT NULL,
    [HOEHE_USERCHANGE] int NOT NULL,
    [SHAPE_USERCHANGE] int NOT NULL,
    [TRIM_USERCHANGE] int NOT NULL,
    [MENGE_CUT] decimal(28,8) NOT NULL,
    [NV_SORTID] int NULL,
    [NV_NAME] nvarchar(40) NULL,
    [KEYINDEX] int NOT NULL,
    [SUB_POS] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBPRODSEQ] (
    [JOBNUMBER] int NOT NULL,
    [LOTTYPE] int NOT NULL,
    [PRODUCTIONSEQUENCE] int NOT NULL,
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [KEYINDEX] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_JOBTEMP] (
    [JOBNUMBER] int NOT NULL,
    [VARIANTNUMBER] int NOT NULL,
    [DESCRIPTION] nvarchar(254) NOT NULL,
    [STATUS] int NOT NULL,
    [RACKORGA] int NOT NULL,
    [RACKORGANAME] nvarchar(80) NOT NULL,
    [RACKDEPTHFACTOR] int NOT NULL,
    [CREATIONDATE] datetime NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LOCKTIME] datetime NULL
);

CREATE TABLE SYSADM.[PROD_LISTSTATE] (
    [LISTSTATE] int NOT NULL,
    [LISTNAME] nvarchar(254) NOT NULL,
    [VIEWNAME] nvarchar(254) NULL,
    [ISACTIVE] int NOT NULL,
    [STATE] nvarchar(max) NOT NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_LOGRACK] (
    [LOGICALRACK] int NOT NULL,
    [RACKNAME] nvarchar(60) NOT NULL,
    [LOTTYPE] int NOT NULL,
    [SORTKEY_GROUP] int NULL,
    [GROUPSORTED] int NOT NULL,
    [SORTKEY_INGROUP] int NULL,
    [RACKFROM] int NOT NULL,
    [RACKTO] int NOT NULL,
    [RACKCURRENT] int NOT NULL,
    [SORTKEY_CHANGERACK] int NULL,
    [PHYSICALRACKNAME] nvarchar(60) NOT NULL,
    [RACKTYPE] int NOT NULL,
    [NUMSLOTS] int NOT NULL,
    [NUMSHEETS] int NOT NULL,
    [DEPTH] decimal(28,8) NOT NULL,
    [DEFAULT_RACK] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [RESETRACK] int NULL,
    [GROUPINGMETHOD] int NULL
);

CREATE TABLE SYSADM.[PROD_LOGRACKORGA] (
    [RACKORGA] int NOT NULL,
    [LOGICALRACK] int NOT NULL,
    [RACKGROUP] int NULL,
    [LTPRDSQ] int NOT NULL,
    [LTCHECKSEQUENCE] int NOT NULL,
    [FORMULA] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_MACHINECODE] (
    [MACHINECODE] int NOT NULL,
    [MACHINETYPE] int NOT NULL,
    [DESCRIPTION] nvarchar(60) NOT NULL,
    [ISODRIVER] int NULL,
    [ISOSECTION] int NULL,
    [KEYWORD] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTI_PARAMETERS] (
    [OPTIMIZATION] int NOT NULL,
    [OPTIMODE] int NOT NULL,
    [CUTMODE] int NOT NULL,
    [SHAPEOPTI] int NOT NULL,
    [ZCUTCOST] decimal(28,8) NOT NULL,
    [MIN_XX] decimal(28,8) NOT NULL,
    [MAX_XX] decimal(28,8) NOT NULL,
    [MIN_YY] decimal(28,8) NOT NULL,
    [MAX_YY] decimal(28,8) NOT NULL,
    [MIN_XZ] decimal(28,8) NOT NULL,
    [TRIM_LEFT] decimal(28,8) NOT NULL,
    [TRIM_RIGHT] decimal(28,8) NOT NULL,
    [TRIM_BOTTOM] decimal(28,8) NOT NULL,
    [TRIM_TOP] decimal(28,8) NOT NULL,
    [TRIM_SHAPE] decimal(28,8) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTI_PLATES] (
    [OPTIMIZATION] int NOT NULL,
    [PLATENR] int NOT NULL,
    [PATTERNNR] int NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [SLOT] int NOT NULL,
    [LENGTH] decimal(28,8) NOT NULL,
    [HEIGHT] decimal(28,8) NOT NULL,
    [CUT] int NOT NULL,
    [PM_TRAVEREN_FLAG] int NOT NULL,
    [PM_PRIO_OPTI] int NOT NULL,
    [PM_AUFLEGER_CODE] nvarchar(4) NULL,
    [STOCKBOOKED] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [SUPPLIER_INFO] nvarchar(64) NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTI_RESIDUE_PLATES] (
    [OPTIMIZATION] int NOT NULL,
    [PLATENR] int NOT NULL,
    [SEQUENCE] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [PATTERNNR] int NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [LENGTH] decimal(28,8) NOT NULL,
    [HEIGHT] decimal(28,8) NOT NULL,
    [AMOUNT] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTI_SEQUENCE] (
    [OPTIMIZATION] int NOT NULL,
    [SEQUENCE] int NOT NULL,
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_NODE] int NULL,
    [PLATENR] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BOM_ID] int NOT NULL,
    [KEYINDEX] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTI_STATISTICS] (
    [ID] int NOT NULL,
    [OPTIMIZATION] int NULL,
    [OPTIMIZATION_NUMBER] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [PRODUKT_BEZ] nvarchar(40) NOT NULL,
    [AGG] int NOT NULL,
    [OPTIDATE] datetime NOT NULL,
    [OPTIMODE] int NOT NULL,
    [STATUS] int NOT NULL,
    [RESULT] float NOT NULL,
    [RESULTWITHTRIM] float NOT NULL,
    [PLATEAREA] decimal(28,8) NOT NULL,
    [PLATEAREATRIM] decimal(28,8) NOT NULL,
    [SHEETAREA] decimal(28,8) NOT NULL,
    [SHEETCOUNT] int NOT NULL,
    [SAVEFILE] varbinary(max) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [RES_OUT_AREA] decimal(28,8) NOT NULL,
    [RES_IN_AREA] decimal(28,8) NOT NULL,
    [RES_IN_AREATRIM] decimal(28,8) NOT NULL,
    [BREAK_AREA_QUANTITY] decimal(28,8) NOT NULL,
    [BREAK_AREA_COST] decimal(28,8) NOT NULL,
    [BREAK_AREA] decimal(28,8) NOT NULL,
    [NET_AREA_COST] decimal(28,8) NOT NULL,
    [NET_AREA] decimal(28,8) NOT NULL,
    [INVOICE_AREA_COST] decimal(28,8) NOT NULL,
    [INVOICE_AREA] decimal(28,8) NOT NULL,
    [REUSABLE_REST_COST] decimal(28,8) NOT NULL,
    [REUSABLE_REST] decimal(28,8) NOT NULL,
    [WASTE_AREA_COST] decimal(28,8) NOT NULL,
    [WASTE_AREA] decimal(28,8) NOT NULL,
    [OPTI_AREA_COST] decimal(28,8) NOT NULL,
    [OPTI_AREA] decimal(28,8) NOT NULL
);

CREATE TABLE SYSADM.[PROD_OPTIMIZATION] (
    [OPTIMIZATION] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [PRODUKT_BEZ] nvarchar(40) NOT NULL,
    [AGG] int NOT NULL,
    [OPTIDATE] datetime NOT NULL,
    [OPTIMODE] int NOT NULL,
    [STATUS] int NOT NULL,
    [RESULT] float NOT NULL,
    [RESULTWITHTRIM] float NOT NULL,
    [PLATEAREA] decimal(28,8) NOT NULL,
    [PLATEAREATRIM] decimal(28,8) NOT NULL,
    [SHEETAREA] decimal(28,8) NOT NULL,
    [SHEETCOUNT] int NOT NULL,
    [SAVEFILE] varbinary(max) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [BREAK_AREA_QUANTITY] decimal(28,8) NOT NULL,
    [BREAK_AREA_COST] decimal(28,8) NOT NULL,
    [BREAK_AREA] decimal(28,8) NOT NULL,
    [NET_AREA_COST] decimal(28,8) NOT NULL,
    [NET_AREA] decimal(28,8) NOT NULL,
    [INVOICE_AREA_COST] decimal(28,8) NOT NULL,
    [INVOICE_AREA] decimal(28,8) NOT NULL,
    [REUSABLE_REST_COST] decimal(28,8) NOT NULL,
    [REUSABLE_REST] decimal(28,8) NOT NULL,
    [WASTE_AREA_COST] decimal(28,8) NOT NULL,
    [WASTE_AREA] decimal(28,8) NOT NULL,
    [OPTI_AREA_COST] decimal(28,8) NOT NULL,
    [OPTI_AREA] decimal(28,8) NOT NULL,
    [RESIDUE_PLATE_LENGTH] decimal(28,8) NOT NULL
);

CREATE TABLE SYSADM.[PROD_ORGA] (
    [RACKORGA] int NOT NULL,
    [RACKORGANAME] nvarchar(80) NOT NULL,
    [IS_STANDARD] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_PARAMETER] (
    [PARAMETERID] int NOT NULL,
    [INTVALUE1] int NULL,
    [INTVALUE2] int NULL,
    [INTVALUE3] int NULL,
    [STRVALUE1] nvarchar(254) NULL,
    [STRVALUE2] nvarchar(254) NULL,
    [STRVALUE3] nvarchar(254) NULL,
    [DECVALUE1] decimal(28,8) NULL,
    [DECVALUE2] decimal(28,8) NULL,
    [DECVALUE3] decimal(28,8) NULL,
    [DATVALUE1] datetime NULL,
    [DATVALUE2] datetime NULL,
    [DATVALUE3] datetime NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_PT_AGGREGATE] (
    [AGG_ID] int NOT NULL,
    [MACHINECOLOR] nvarchar(12) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [SNDRAWINGVIEW] nvarchar(80) NOT NULL
);

CREATE TABLE SYSADM.[PROD_PT_PARAMETER] (
    [SCANNERPREFIX] nvarchar(10) NOT NULL,
    [SCANNERPOSTFIX] nvarchar(10) NOT NULL,
    [SCANNERTIMEBETWEENKEYSTROKE] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [SERVERVOLUMENAME] nvarchar(5) NOT NULL,
    [SERVERVOLUMEPATH] nvarchar(254) NOT NULL,
    [SERVERUPDATEPATH] nvarchar(254) NOT NULL
);

CREATE TABLE SYSADM.[PROD_RACKGROUP] (
    [RACKGROUP] int NOT NULL,
    [NAME] nvarchar(60) NOT NULL,
    [SORTKEY] int NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_REPORT_TEXT] (
    [TEXTNUMBER] int NOT NULL,
    [LANGUAGE] nvarchar(15) NOT NULL,
    [TEXT] nvarchar(200) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_RESIDUE_PLATES] (
    [ID] int NOT NULL,
    [LAGERORT_ID] int NOT NULL,
    [SLOT] int NOT NULL,
    [OPTIMIZATION] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [LENGTH] decimal(28,8) NOT NULL,
    [HEIGHT] decimal(28,8) NOT NULL,
    [PM_TRAVEREN_FLAG] int NOT NULL,
    [PM_PRIO_OPTI] int NOT NULL,
    [LOCK] int NOT NULL,
    [LOCKTIME] datetime NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [AUFLEGER_CODE] nvarchar(4) NULL,
    [AMOUNT] int NULL,
    [FROM_OPTIMIZATION] int NULL
);

CREATE TABLE SYSADM.[PROD_SHAPETRIM] (
    [MODKATALOG] int NOT NULL,
    [MODNR] int NOT NULL,
    [TRIM1] decimal(28,8) NULL,
    [TRIM2] decimal(28,8) NULL,
    [TRIM3] decimal(28,8) NULL,
    [TRIM4] decimal(28,8) NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL,
    [T1POS] int NOT NULL,
    [T2POS] int NOT NULL,
    [T3POS] int NOT NULL,
    [T4POS] int NOT NULL
);

CREATE TABLE SYSADM.[PROD_SORTKEY] (
    [SORTKEY] int NOT NULL,
    [NAME] nvarchar(60) NOT NULL,
    [LASTCHANGEDATE] datetime NOT NULL,
    [LASTCHANGEUSER] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[PROD_SORTKEYFORMULA] (
    [SORTKEY] int NOT NULL,
    [SEQUENCENR] int NOT NULL,
    [FORMULA] int NOT NULL,
    [SORT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_AU_PROD] (
    [ID] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [RABATT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_AU_PRODGRP] (
    [ID] int NOT NULL,
    [PRODART] nvarchar(40) NOT NULL,
    [PRODGRP] nvarchar(40) NOT NULL,
    [RABATT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_KG_PROD] (
    [OBJEKT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_KG_WGR] (
    [OBJEKT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_KU_PROD] (
    [OBJEKT] int NOT NULL,
    [IDENT] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_KU_WGR] (
    [OBJEKT] int NOT NULL,
    [IDENT] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_LG_PROD] (
    [OBJEKT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_LG_WGR] (
    [OBJEKT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_LI_PROD] (
    [OBJEKT] int NOT NULL,
    [IDENT] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_LI_WGR] (
    [OBJEKT] int NOT NULL,
    [IDENT] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [ID] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RB_RABATT] (
    [ID] int NOT NULL,
    [LISTE_UML] int NOT NULL,
    [SCHLS_UML] int NOT NULL,
    [DEFAULT_KZ] int NULL,
    [VON_DATUM] date NOT NULL,
    [BIS_DATUM] date NOT NULL,
    [MINMENGE] decimal(28,8) NULL,
    [MASSRNDG_BR] int NULL,
    [MASSRNDG_HO] int NULL,
    [RND_PUNKT1] int NOT NULL,
    [RND_TAB1] int NOT NULL,
    [RND_PUNKT2] int NOT NULL,
    [RND_TAB2] int NOT NULL,
    [GUSS_LISTE] int NOT NULL,
    [GUSS_SCHLS] int NOT NULL,
    [SOND_LISTE] int NOT NULL,
    [SOND_SCHLS] int NOT NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [STAF_TYP] nvarchar(40) NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [EK_LIEFERANT] int NOT NULL,
    [RABATT] decimal(28,8) NOT NULL,
    [RABATT1] decimal(28,8) NULL,
    [RABATT2] decimal(28,8) NULL,
    [RABATT3] decimal(28,8) NULL,
    [TRANSACTION_TIME] datetime NULL,
    [ROWID] char(36) NOT NULL,
    [MINSTKPREIS] float NULL
);

CREATE TABLE SYSADM.[RB_STAFFEL] (
    [ID] int NOT NULL,
    [GRENZE] decimal(28,8) NOT NULL,
    [RABATT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_DESCR_FIELD] (
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [SEQNR] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [MOD] int NULL,
    [LAST_CHANGE] datetime NULL,
    [LAST_EXPORT] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_DESCR_TABLE] (
    [TABID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [SEQNR] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [MOD] int NULL,
    [LAST_CHANGE] datetime NULL,
    [LAST_EXPORT] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_FIELD] (
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [TAG] nvarchar(50) NOT NULL,
    [TYPE] nvarchar(50) NOT NULL,
    [NULLS] int NOT NULL,
    [DEF] int NOT NULL,
    [DEF_VALUE] nvarchar(50) NULL,
    [KEY] int NOT NULL,
    [LAST_CHANGE] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_FIELD_CON] (
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [DEF_SEND_IND] int NOT NULL,
    [DEF_SEND] nvarchar(50) NULL,
    [DEF_REC_IND] int NOT NULL,
    [DEF_REC] nvarchar(50) NULL,
    [DEF_MISS_IND] int NOT NULL,
    [DEF_MISS] nvarchar(50) NULL,
    [LOCAL] int NOT NULL,
    [LOCVAL_COND1] nvarchar(250) NULL,
    [LOCVAL_COND2] nvarchar(250) NULL,
    [AFFECTS_HISTORY] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_FIELDVIEW] (
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [NAME] nvarchar(50) NULL,
    [LAST_EXPORT] datetime NULL,
    [LAST_TRANSLATED] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_INDEXES] (
    [IDXID] nvarchar(50) NOT NULL,
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [UNI] int NOT NULL,
    [SEQNUM] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_JOB] (
    [JOBNR] int NOT NULL,
    [TABID] nvarchar(50) NOT NULL,
    [SEQNR] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_JOBHEAD] (
    [JOBNR] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [ADDJOB] int NULL,
    [SEND_FUNC] nvarchar(250) NULL,
    [REC_FUNC] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_LANGUAGE] (
    [LANGUAGE] int NOT NULL,
    [NAME] nvarchar(50) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_MSG] (
    [MSGNR] int NOT NULL,
    [LANGUAGE] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_NUM] (
    [CODE] int NOT NULL,
    [DESCRIPTION] nvarchar(60) NULL,
    [VALUE] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_REF] (
    [REFNR] int NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [TABID] nvarchar(50) NOT NULL,
    [REFFLDID] nvarchar(50) NOT NULL,
    [REFTABID] nvarchar(50) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_REFHEAD] (
    [REFNR] int NOT NULL,
    [REFNAME] nvarchar(50) NOT NULL,
    [DELFLAG] int NOT NULL,
    [DELTXT1] nvarchar(250) NULL,
    [DELTXT2] nvarchar(250) NULL,
    [NOTRANS] int NOT NULL,
    [AUTOTRANSFER] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_SITE] (
    [SITENR] int NOT NULL,
    [DATABASE] nvarchar(250) NOT NULL,
    [HOSTNAME] nvarchar(250) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [DSN] nvarchar(64) NOT NULL,
    [USERNAME] nvarchar(32) NOT NULL,
    [PWD] nvarchar(64) NOT NULL,
    [REP_ACTIVE] int NOT NULL,
    [PATH] nvarchar(254) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_TABLE] (
    [TABID] nvarchar(50) NOT NULL,
    [TAG] nvarchar(50) NOT NULL,
    [TODO] int NOT NULL,
    [HISTORY] int NOT NULL,
    [BEZ_FLDID] nvarchar(50) NULL,
    [LAST_CHANGE] datetime NULL,
    [PARENT_TABLE] nvarchar(50) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_TABLE_HEAD_DETAIL_RELATIONS] (
    [TABID_HEAD] nvarchar(50) NOT NULL,
    [TABID_DETAIL] nvarchar(50) NOT NULL,
    [DETAIL_TABLEFLAG] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_TABLEVIEW] (
    [TABID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [NAME] nvarchar(50) NULL,
    [LAST_EXPORT] datetime NULL,
    [LAST_TRANSLATED] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_TODO] (
    [SITENR] int NOT NULL,
    [TABID] nvarchar(50) NOT NULL,
    [LFDNR] int NULL,
    [COND1] nvarchar(250) NULL,
    [COND2] nvarchar(250) NULL,
    [PAK_NO] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_USER_DESCR_FIELD] (
    [TABID] nvarchar(50) NOT NULL,
    [FLDID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[RR_USER_DESCR_TABLE] (
    [TABID] nvarchar(50) NOT NULL,
    [LANGUAGE] int NOT NULL,
    [TXT] nvarchar(250) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[SST_KU_REKLA] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [REKLA_GRND] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[SST_KUNDEN] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[SST_LI_REKLA] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [REKLA_GRND] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[SST_LIEFERANTEN] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[SST_PARAM] (
    [MANDANT] int NOT NULL,
    [STAT_TYPE] int NOT NULL,
    [EXP_MANDANT] int NOT NULL,
    [EXPORT_PATH] nvarchar(80) NOT NULL,
    [IMPORT_PATH] nvarchar(80) NOT NULL,
    [PROTOCOL_PATH] nvarchar(80) NOT NULL,
    [IMP_MOD_ADIENST] int NOT NULL,
    [IMP_MOD_GRUPPE] int NOT NULL,
    [IMP_MOD_GESCHART] int NOT NULL,
    [IMP_MOD_BRANCHE] int NOT NULL,
    [IMP_MOD_AV_BEREICH] int NOT NULL,
    [IMP_MOD_MANDANT] int NOT NULL,
    [IMP_MOD_KUNDE] int NOT NULL,
    [IMP_MOD_KMB_WGR] int NOT NULL,
    [IMP_MOD_PRODUKT] int NOT NULL,
    [IMP_DEF_ADIENST] nvarchar(40) NOT NULL,
    [IMP_DEF_GRUPPE] nvarchar(40) NOT NULL,
    [IMP_DEF_GESCHART] nvarchar(80) NOT NULL,
    [IMP_DEF_BRANCHE] nvarchar(40) NOT NULL,
    [IMP_DEF_AV_BEREICH] nvarchar(40) NOT NULL,
    [IMP_DEF_MANDANT] int NOT NULL,
    [IMP_DEF_KUNDE] int NOT NULL,
    [IMP_DEF_PRODUKT] int NOT NULL,
    [MITARB_ID] nvarchar(40) NULL,
    [DATUM] date NULL,
    [IMP_DEF_KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_ARD_TEMP] (
    [ABNR] int NOT NULL,
    [RNNR] int NOT NULL,
    [PRODNR1] int NOT NULL,
    [PRODNR2] int NOT NULL,
    [PRODNR3] int NOT NULL,
    [STUECK] int NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [RPGR] int NOT NULL,
    [KG] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_INFO] (
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [AUFTR] int NOT NULL,
    [WGR_KMB_STAT] nvarchar(3) NOT NULL,
    [ERF_DATUM] date NOT NULL,
    [ERF_MENGE] decimal(28,8) NOT NULL,
    [ERF_QM] decimal(28,8) NOT NULL,
    [ERF_LFM] decimal(28,8) NOT NULL,
    [ERF_VK] decimal(28,8) NOT NULL,
    [ERF_EK1] decimal(28,8) NOT NULL,
    [ERF_EK2] decimal(28,8) NOT NULL,
    [ERF_PLANSTK] decimal(28,8) NOT NULL,
    [RECH_DATUM] date NOT NULL,
    [RECH_MENGE] decimal(28,8) NOT NULL,
    [RECH_QM] decimal(28,8) NOT NULL,
    [RECH_LFM] decimal(28,8) NOT NULL,
    [RECH_VK] decimal(28,8) NOT NULL,
    [RECH_EK1] decimal(28,8) NOT NULL,
    [RECH_EK2] decimal(28,8) NOT NULL,
    [RECH_PLANSTK] decimal(28,8) NOT NULL,
    [OFFEN_MENGE] decimal(28,8) NOT NULL,
    [OFFEN_QM] decimal(28,8) NOT NULL,
    [OFFEN_LFM] decimal(28,8) NOT NULL,
    [OFFEN_VK] decimal(28,8) NOT NULL,
    [OFFEN_EK1] decimal(28,8) NOT NULL,
    [OFFEN_EK2] decimal(28,8) NOT NULL,
    [OFFEN_PLANSTK] decimal(28,8) NOT NULL,
    [IDENT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_INFO_PROD] (
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [AUFTR] int NOT NULL,
    [WGR_KMB_STAT] nvarchar(3) NOT NULL,
    [PROD_DATUM] date NOT NULL,
    [PROD_MENGE] decimal(28,8) NOT NULL,
    [PROD_QM] decimal(28,8) NOT NULL,
    [PROD_LFM] decimal(28,8) NOT NULL,
    [PROD_VK] decimal(28,8) NOT NULL,
    [PROD_EK1] decimal(28,8) NOT NULL,
    [PROD_EK2] decimal(28,8) NOT NULL,
    [PROD_PLANSTK] decimal(28,8) NOT NULL,
    [IDENT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_KU_REKLA] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [REKLA_GRND] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_KUNDEN] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_LI_REKLA] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [REKLA_ORT] nvarchar(40) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [REKLA_GRND] nvarchar(40) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_LIEFERANTEN] (
    [DATUM] date NOT NULL,
    [MANDANT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [GRUPPE] nvarchar(40) NOT NULL,
    [PRODUKT] int NOT NULL,
    [ADIENST] nvarchar(40) NOT NULL,
    [GESCHART] nvarchar(80) NOT NULL,
    [BRANCHE] nvarchar(40) NOT NULL,
    [AV_BEREICH] nvarchar(40) NOT NULL,
    [EK1] decimal(28,8) NOT NULL,
    [EK2] decimal(28,8) NOT NULL,
    [GEWICHT] decimal(28,8) NOT NULL,
    [BEA_MENGE] decimal(28,8) NOT NULL,
    [BEA_QM] decimal(28,8) NOT NULL,
    [BEA_LFM] decimal(28,8) NOT NULL,
    [VK2] decimal(28,8) NOT NULL,
    [MENGE] decimal(28,8) NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [LFM] decimal(28,8) NOT NULL,
    [VK] decimal(28,8) NOT NULL,
    [KMB_WGR] nvarchar(3) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_PROV] (
    [ADIENST] nvarchar(40) NOT NULL,
    [DATUM] date NOT NULL,
    [KUNDE] int NOT NULL,
    [AUFTRAG] int NOT NULL,
    [ERF_DATUM] date NOT NULL,
    [RECHNUNG] int NOT NULL,
    [RECH_DATUM] date NOT NULL,
    [PERIODE] int NULL,
    [STATUS] int NULL,
    [BETRAG] decimal(28,8) NOT NULL,
    [PROV_BETRAG] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ST_STATISTIK] (
    [ST_NAME] nvarchar(40) NOT NULL,
    [ST_BEREICH] int NOT NULL,
    [ST_GESAMT] int NOT NULL,
    [ST_JAHR] int NOT NULL,
    [ST_MONAT] int NOT NULL,
    [ST_DIREKT] int NOT NULL,
    [ST_VON_MONAT] int NOT NULL,
    [ST_BIS_MONAT] int NOT NULL,
    [KRITERIUM1] int NOT NULL,
    [KRITERIUM2] int NOT NULL,
    [KRITERIUM3] int NOT NULL,
    [KRITERIUM4] int NOT NULL,
    [KRITERIUM5] int NOT NULL,
    [KRITERIUM6] int NOT NULL,
    [KRITERIUM7] int NOT NULL,
    [KRITERIUM8] int NOT NULL,
    [KRITERIUM9] int NOT NULL,
    [KRITERIUM10] int NOT NULL,
    [VON_ADIENST] nvarchar(40) NULL,
    [VON_GRUPPE] nvarchar(40) NULL,
    [VON_GESCHART] nvarchar(40) NULL,
    [VON_BRANCHE] nvarchar(40) NULL,
    [VON_AV_BEREICH] nvarchar(40) NULL,
    [VON_KLASSIFIKATOR] nvarchar(40) NULL,
    [VON_KLASSI_WERT] nvarchar(40) NULL,
    [VON_MANDANT] int NOT NULL,
    [VON_KUNDE] int NOT NULL,
    [VON_LIEFERANT] int NOT NULL,
    [VON_TOP] nvarchar(40) NULL,
    [VON_WGR] nvarchar(40) NULL,
    [BIS_WGR] nvarchar(40) NULL,
    [VON_PRODUKT] int NOT NULL,
    [BIS_PRODUKT] int NOT NULL,
    [MIND_UMSATZ] int NOT NULL,
    [RUBRIK_VK] int NOT NULL,
    [RUBRIK_EK] int NOT NULL,
    [RUBRIK_DB] int NOT NULL,
    [RUBRIK_DB_PROZ] int NOT NULL,
    [RUBRIK_DP] int NOT NULL,
    [RUBRIK_STUECK] int NOT NULL,
    [RUBRIK_QM] int NOT NULL,
    [RUBRIK_LFM] int NOT NULL,
    [RUBRIK_GEWICHT] int NOT NULL,
    [RUBRIK_VK_PROZ] int NOT NULL,
    [GRAPH_TYPE] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUBRIK_VK2] int NOT NULL,
    [ST_T10_ACTIVE] int NOT NULL,
    [ST_T10_KRITERIUM] int NOT NULL,
    [ST_T10_MAXIMAL] int NOT NULL,
    [ST_T10_SORT_MODE] int NOT NULL,
    [BIS_ADIENST] nvarchar(40) NULL,
    [BIS_GRUPPE] nvarchar(40) NULL,
    [BIS_GESCHART] nvarchar(40) NULL,
    [BIS_BRANCHE] nvarchar(40) NULL,
    [BIS_AV_BEREICH] nvarchar(40) NULL,
    [BIS_KLASSIFIKATOR] nvarchar(40) NULL,
    [BIS_KLASSI_WERT] nvarchar(40) NULL,
    [BIS_MANDANT] int NOT NULL,
    [BIS_KUNDE] int NOT NULL,
    [BIS_LIEFERANT] int NOT NULL,
    [BIS_TOP] nvarchar(40) NULL,
    [MIND_UMSATZ_PRO] int NOT NULL,
    [SEPARATION_MODE] int NOT NULL,
    [SQL_APPENDIX] nvarchar(max) NULL,
    [ADIENST_NOT] int NOT NULL,
    [AV_BEREICH_NOT] int NOT NULL,
    [BRANCHE_NOT] int NOT NULL,
    [GESCHART_NOT] int NOT NULL,
    [GRUPPE_NOT] int NOT NULL,
    [KLASSIFIKATOR_NOT] int NOT NULL,
    [KLASSI_WERT_NOT] int NOT NULL,
    [KUNDE_NOT] int NOT NULL,
    [MANDANT_NOT] int NOT NULL,
    [PRODUKT_NOT] int NOT NULL,
    [TOP_NOT] int NOT NULL,
    [WGR_NOT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[TR_BEWEGUNG] (
    [ID] int NULL,
    [STATUS] nvarchar(3) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[TR_KUNDEN] (
    [ID] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[VP_PROVSATZ] (
    [VP_ADIENST] nvarchar(40) NOT NULL,
    [VP_KALKTYPE] int NOT NULL,
    [VP_WGR] nvarchar(3) NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [MITARB_ID] nvarchar(40) NULL,
    [DATUM] date NULL,
    [VP_LISTE_ID] int NOT NULL,
    [VP_SCHLS_ID] int NOT NULL,
    [VP_DATUM_VALID] date NOT NULL,
    [VP_KUNDE] int NOT NULL,
    [VP_PRODUKT] int NOT NULL,
    [VP_STAFFEL] decimal(28,8) NOT NULL,
    [VP_SATZ] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_ISO_AUF1] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GLASDICKE] decimal(28,8) NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_ISO_AUF2] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [AUS_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_ISO_AUF3] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KG_ISO_AUF1] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GLASDICKE] decimal(28,8) NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KG_ISO_AUF2] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [AUS_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KG_ISO_AUF3] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KG_PRGRU] (
    [OBJEKT] int NOT NULL,
    [KUNDEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [PR_GRU] nvarchar(6) NOT NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KU_ISO_AUF1] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GLASDICKE] decimal(28,8) NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KU_ISO_AUF2] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [AUS_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KU_ISO_AUF3] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_KU_PRGRU] (
    [OBJEKT] int NOT NULL,
    [KUNDE] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [PR_GRU] nvarchar(6) NOT NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LG_ISO_AUF1] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GLASDICKE] decimal(28,8) NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LG_ISO_AUF2] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [AUS_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LG_ISO_AUF3] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LG_PRGRU] (
    [OBJEKT] int NOT NULL,
    [LIEFERANTEN_GRP] nvarchar(40) NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [PR_GRU] nvarchar(6) NOT NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LI_ISO_AUF1] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GLASDICKE] decimal(28,8) NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LI_ISO_AUF2] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [AUS_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LI_ISO_AUF3] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [SON_ID] int NOT NULL,
    [GRENZTYP] int NULL,
    [ZUSCHLAG1] decimal(28,8) NULL,
    [ZUSCHLAG2] decimal(28,8) NULL,
    [ZUSCHLAG3] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [TABELLE] int NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [GRENZWERT1_1] decimal(28,8) NULL,
    [GRENZWERT1_2] decimal(28,8) NULL,
    [GRENZWERT2_1] decimal(28,8) NULL,
    [GRENZWERT2_2] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_LI_PRGRU] (
    [OBJEKT] int NOT NULL,
    [LIEFERANT] int NOT NULL,
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [PR_GRU] nvarchar(6) NOT NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_PRGRU] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [TYP] int NOT NULL,
    [NET_PREIS_MIN] decimal(28,8) NULL,
    [PR_GRU] nvarchar(6) NOT NULL,
    [ZUSCHLTYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [SONST_ZU_TAB] int NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RUNDUNG_BR] int NOT NULL,
    [RUNDUNG_HO] int NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [FLAECHE_MIN] decimal(28,8) NULL,
    [BRU_PREIS_MIN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_SONST] (
    [ID] int NOT NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [MANUELL] int NOT NULL,
    [ZUSCHLAG_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ZUSCHLAG_ART] int NOT NULL,
    [ODER] int NOT NULL,
    [FOLGE] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_SONST_KPF] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZU_STRUKTUR] (
    [LISTE_ID] int NOT NULL,
    [SCHLS_ID] int NOT NULL,
    [KEINE_ZUSCHLAG] decimal(28,8) NULL,
    [AUSSEN_ZUSCHLAG] decimal(28,8) NULL,
    [INNEN_ZUSCHLAG] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_AGGREGATE] (
    [AGG_ID] int NOT NULL,
    [AGG_BEZ] nvarchar(40) NULL,
    [AGG_TYP] int NOT NULL,
    [AGG_PRB] int NOT NULL,
    [KMASCH] decimal(28,8) NOT NULL,
    [KVARI] decimal(28,8) NOT NULL,
    [AGG_SERIENTAB] int NOT NULL,
    [AGG_MINBR] int NULL,
    [AGG_MAXBR] int NULL,
    [AGG_MINHO] int NULL,
    [AGG_MAXHO] int NULL,
    [AGG_MINSZR] int NULL,
    [AGG_MAXSZR] int NULL,
    [AGG_MINGESST] int NULL,
    [AGG_MAXGESST] int NULL,
    [AGG_MINST] int NULL,
    [AGG_MAXST] int NULL,
    [AGG_MAXGEW] int NULL,
    [AGG_GAS] int NULL,
    [AGG_KS] int NULL,
    [AGG_MODELL] int NULL,
    [AGG_SPROSSEN] int NULL,
    [AGG_SCHEIBEN] int NULL,
    [AGG_MAXSV] decimal(28,8) NULL,
    [AGG_MINDURM] decimal(28,8) NULL,
    [AGG_MAXDURM] decimal(28,8) NULL,
    [AGG_MINECKRAD] decimal(28,8) NULL,
    [BARC] int NOT NULL,
    [SPERR_KZ] int NOT NULL,
    [AGG_MINANZ] int NULL,
    [AGG_MAXANZ] int NULL,
    [AGG_MAXECKRAD] decimal(28,8) NULL,
    [AGG_DRB] int NOT NULL,
    [L_MENGE] int NULL,
    [L_EINHEIT] int NULL,
    [AGG_PERSONEN] decimal(28,8) NOT NULL,
    [AGG_KOSTEN] decimal(28,8) NOT NULL,
    [AGG_MAXFL] decimal(28,8) NULL,
    [KLOHN] decimal(28,8) NOT NULL,
    [AGG_MINDG] decimal(28,8) NULL,
    [AGG_MINFL] decimal(28,8) NULL,
    [AGG_FORMEL] int NULL,
    [AGG_MINGW] int NULL,
    [AGG_MAXGW] int NULL,
    [ROWID] char(36) NOT NULL,
    [XOPT_TISCH] nvarchar(4) NULL,
    [PM_Z_SCHNITT] int NOT NULL,
    [PM_SCHNEIDMODUS] int NOT NULL,
    [PM_BRECHSTART] int NOT NULL,
    [PM_REFPUNKT] int NOT NULL,
    [PM_Z_SCHNITTKOSTEN] decimal(28,8) NOT NULL,
    [PM_MIN_XZ] decimal(28,8) NOT NULL,
    [PM_MIN_ZZ] decimal(28,8) NOT NULL,
    [PM_MIN_YY_MIT_Z] decimal(28,8) NOT NULL,
    [PM_MIN_YY_OHNE_Z] decimal(28,8) NOT NULL,
    [PM_MIN_XX_MIT_Y] decimal(28,8) NOT NULL,
    [PM_MIN_XX_OHNE_Y] decimal(28,8) NOT NULL,
    [PM_MIN_RESTBREITE] decimal(28,8) NOT NULL,
    [PM_MAX_XX] decimal(28,8) NOT NULL,
    [PM_MAX_YY] decimal(28,8) NOT NULL,
    [PM_AUTOBRECHEN] int NOT NULL,
    [PM_AUTOAUFLEGER] int NOT NULL,
    [PM_ENTSCHICHTEN] int NOT NULL,
    [PM_VSG] int NOT NULL,
    [PM_TRAVERENERWEITERUNG] int NOT NULL,
    [PM_MASCHINENCODE] int NOT NULL,
    [PM_AUSGABEVERZEICHNIS] nvarchar(254) NOT NULL,
    [PM_MAX_XX_VARIANCEFLAG] int NOT NULL,
    [PM_MAX_YY_VARIANCEFLAG] int NOT NULL,
    [PM_MIN_XX_VARIANCEFLAG] int NOT NULL,
    [PM_MIN_YY_VARIANCEFLAG] int NOT NULL,
    [PM_DEFAULTCUTBACK] decimal(28,8) NULL,
    [PM_REMASTER] int NOT NULL
);

CREATE TABLE SYSADM.[ZW_AGGTYPEN] (
    [AGG_TYP] int NOT NULL,
    [AGG_TYPBEZ] nvarchar(40) NULL,
    [CHECK_MASZE] int NOT NULL,
    [CHECK_SZR] int NOT NULL,
    [CHECK_GST] int NOT NULL,
    [CHECK_EST] int NOT NULL,
    [CHECK_GEW] int NOT NULL,
    [CHECK_MSA] int NOT NULL,
    [CHECK_FL] int NOT NULL,
    [CHECK_GAS] int NOT NULL,
    [CHECK_KS] int NOT NULL,
    [CHECK_MO] int NOT NULL,
    [CHECK_SPR] int NOT NULL,
    [CHECK_STCK] int NOT NULL,
    [CHECK_SV] int NOT NULL,
    [CHECK_DURM] int NOT NULL,
    [CHECK_DRB] int NOT NULL,
    [CHECK_ECKRAD] int NOT NULL,
    [CHECK_DIAG] int NULL,
    [CHECK_GEWI] int NULL,
    [ROWID] char(36) NOT NULL,
    [PM_MACHINETYPE] int NOT NULL
);

CREATE TABLE SYSADM.[ZW_ANGEB_BEST_TEILE] (
    [AUFNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [POSNR] int NOT NULL,
    [SUB_POS] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [DATUM_PROD] date NULL,
    [RFOZ] int NOT NULL,
    [SCHICHT] int NOT NULL,
    [STUECK] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ANGEB_ZEIT] (
    [AGG] int NOT NULL,
    [ARBART] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [ARBFOLGE] int NOT NULL,
    [SUB_POS] int NOT NULL,
    [AVB] int NOT NULL,
    [BMENGE] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [DATUM_PROD] datetime NULL,
    [FARBE] int NOT NULL,
    [FERTIG] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [KOSTEN_GES] decimal(28,8) NOT NULL,
    [KZ_FIXED] int NOT NULL,
    [KZ_IGNORE] int NOT NULL,
    [KZ_NOP] int NOT NULL,
    [KZ_SELECTED] int NULL,
    [MAT] decimal(28,8) NOT NULL,
    [POOL_POS] int NOT NULL,
    [PRIO] int NOT NULL,
    [PRL] int NULL,
    [PROD_ZEIT] date NULL,
    [PRODART] int NULL,
    [PRODGRP] int NULL,
    [RFOZ] int NULL,
    [RZ_KENNUNG] int NOT NULL,
    [SCHICHT] int NULL,
    [STUECK] int NOT NULL,
    [TEIL_ID] int NULL,
    [VWT] decimal(28,8) NOT NULL,
    [WERK] int NOT NULL,
    [ZEIT] decimal(28,8) NOT NULL,
    [ZW_LFM] decimal(28,8) NULL,
    [ZW_QM] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ARTSPERRE] (
    [PRODUKT] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [TOTAL_FLAG] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [ARB_NR] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ARTZUO] (
    [PRODUKT] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_AUFTR_ZEIT] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [AGG] int NOT NULL,
    [ARBART] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [PRODART] int NULL,
    [PRODGRP] int NULL,
    [DATUM_PROD] date NULL,
    [STUECK] int NOT NULL,
    [FERTIG] int NOT NULL,
    [KZ_SELECTED] int NULL,
    [PROD_ZEIT] datetime NULL,
    [KZ_NOP] int NOT NULL,
    [WERK] int NOT NULL,
    [ARBFOLGE] int NOT NULL,
    [KZ_FIXED] int NOT NULL,
    [TEIL_ID] int NULL,
    [SUB_POS] int NOT NULL,
    [BMENGE] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [POOL_POS] int NOT NULL,
    [AVB] int NOT NULL,
    [MAT] decimal(28,8) NOT NULL,
    [PRL] int NULL,
    [SCHICHT] int NULL,
    [PRIO] int NOT NULL,
    [RZ_KENNUNG] int NOT NULL,
    [FARBE] int NOT NULL,
    [KZ_IGNORE] int NOT NULL,
    [ZEIT] decimal(28,8) NOT NULL,
    [KOSTEN_GES] decimal(28,8) NOT NULL,
    [ZW_QM] decimal(28,8) NULL,
    [ZW_LFM] decimal(28,8) NULL,
    [VWT] decimal(28,8) NOT NULL,
    [RFOZ] int NULL,
    [ROWID] char(36) NOT NULL,
    [LAUF] int NULL
);

CREATE TABLE SYSADM.[ZW_BEATYPEN] (
    [BEA_TYP] int NOT NULL,
    [BEA_TYPBEZ] nvarchar(40) NULL,
    [BEA_RFOZ] int NOT NULL,
    [BEA_NHP] int NOT NULL,
    [ALCIMNR] int NOT NULL,
    [MANUAB] int NOT NULL,
    [MANAGG] int NOT NULL,
    [AAXXL] int NOT NULL,
    [AVBEX] nvarchar(20) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_BEST_TEILE] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [SUB_POS] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [DATUM_PROD] date NULL,
    [SCHICHT] int NOT NULL,
    [STUECK] int NOT NULL,
    [RFOZ] int NOT NULL,
    [ROWID] char(36) NOT NULL,
    [LAUF] int NULL
);

CREATE TABLE SYSADM.[ZW_FERTIG] (
    [F_AUFNR] int NOT NULL,
    [F_POSNR] int NOT NULL,
    [F_BOM_ID] int NOT NULL,
    [F_AGG] int NOT NULL,
    [F_ARBART] int NOT NULL,
    [F_PROD_ZEIT] datetime NOT NULL,
    [F_FERTIG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_FOARBSPERRE] (
    [AGG] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_FOLGEZUO] (
    [PRODUKT] int NOT NULL,
    [FOLGEARB] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_FOMASPERRE] (
    [AGG] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [TOTAL_FLAG] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_FORMELZUO] (
    [PRODUKT] int NOT NULL,
    [MODELL] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BISANZ] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_IGLTYPEN] (
    [IGL_TYP] int NOT NULL,
    [IGL_TYPBEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KALENDER] (
    [ARBID] int NOT NULL,
    [AGG_ID] int NOT NULL,
    [ID] date NOT NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [TAG] int NOT NULL,
    [ST1] decimal(28,8) NULL,
    [ST2] decimal(28,8) NULL,
    [ST3] decimal(28,8) NULL,
    [ST4] decimal(28,8) NULL,
    [ST5] decimal(28,8) NULL,
    [ST6] decimal(28,8) NULL,
    [STUNDEN] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KALFI] (
    [ARBID] int NOT NULL,
    [AGG_ID] int NOT NULL,
    [ID] date NOT NULL,
    [NUMMER] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [TAG] int NOT NULL,
    [ST1] decimal(28,8) NULL,
    [ST2] decimal(28,8) NULL,
    [ST3] decimal(28,8) NULL,
    [ST4] decimal(28,8) NULL,
    [ST5] decimal(28,8) NULL,
    [ST6] decimal(28,8) NULL,
    [STUNDEN] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KANTENZUO] (
    [PRODUKT] int NOT NULL,
    [KANTEN_KZ] nvarchar(8) NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KOMBIGRP] (
    [PRODUKT] int NOT NULL,
    [PRG] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KOMBIZUO] (
    [PRODUKT] int NOT NULL,
    [PRA] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_KUSPERRE] (
    [KUNDE] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [PRODUKT] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_LAG_PRGZUO] (
    [PRODUKTGRP_NR] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_LAGER_ZUORD] (
    [ID] int NOT NULL,
    [PRODART] nvarchar(40) NOT NULL,
    [PRODGRP] nvarchar(40) NOT NULL,
    [LAGER_ID] int NOT NULL,
    [FREMD_KEY] nvarchar(6) NULL,
    [BA_PRODUKT] int NOT NULL,
    [PRODUKTBER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_LEITARBART] (
    [AGG_TYP] int NOT NULL,
    [INAGG] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_MAUSFALL] (
    [AGG_NR] int NOT NULL,
    [DVON] date NULL,
    [DBIS] date NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_MODGRU] (
    [TXT] nvarchar(6) NOT NULL,
    [BEM] nvarchar(40) NULL,
    [NUMMER] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_POOLPOS] (
    [PP_AUFNR] int NOT NULL,
    [PP_POS] int NOT NULL,
    [PP_UPOS] int NOT NULL,
    [PP_POOLPOS] int NOT NULL,
    [PP_ANZAHL] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PRAZUO] (
    [PRD_NR] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PRGSPERRE] (
    [PRODUKTGRP_NR] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [TOTAL_FLAG] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [ARB_NR] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PRGTYPEN] (
    [PRG_TYP] int NOT NULL,
    [PRG_TYPBEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PRGZUO] (
    [PRODUKTGRP_NR] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PRIORITY] (
    [KDNR] int NOT NULL,
    [WGR] nvarchar(3) NOT NULL,
    [DDIAM] decimal(28,8) NOT NULL,
    [AGG] int NOT NULL,
    [ARBART] int NOT NULL,
    [PRIO] int NOT NULL,
    [FIRMA] int NOT NULL,
    [ANZAHL] int NOT NULL,
    [K_LAENGE] decimal(28,8) NOT NULL,
    [DICKE] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_PROBER] (
    [NUMMER] int NOT NULL,
    [PRODUKTBER] nvarchar(40) NULL,
    [FIRMA] int NOT NULL,
    [STATUS] int NULL,
    [VERWTAGE] decimal(28,8) NOT NULL,
    [PERSONEN] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SCHICHTSPERRE] (
    [SCHICHTX1] datetime NULL,
    [SCHICHTX2] datetime NULL,
    [SCHICHTX3] datetime NULL,
    [SCHICHTX4] datetime NULL,
    [SCHICHTX5] datetime NULL,
    [SCHICHTX6] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SCHICHTWECHSEL] (
    [SCHICHTW1] datetime NULL,
    [SCHICHTW2] datetime NULL,
    [SCHICHTW3] datetime NULL,
    [SCHICHTW4] datetime NULL,
    [SCHICHTW5] datetime NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SERIEN_WERT] (
    [ID] int NOT NULL,
    [STCK] decimal(28,8) NOT NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SERIENBEZ] (
    [SER_NR] int NOT NULL,
    [SER_BEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SL_TEMP] (
    [SL_AUFNR] int NOT NULL,
    [SL_LEVEL] int NOT NULL,
    [SL_BEA] int NOT NULL,
    [SL_RFOZ] int NOT NULL,
    [SL_ID] int NOT NULL,
    [SL_FOLGE] int NOT NULL,
    [SL_NODE] int NOT NULL,
    [SL_AART] int NOT NULL,
    [SL_KZB] int NULL,
    [SL_NHP] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SL_TEMPS] (
    [SL_LEVEL] int NOT NULL,
    [SL_BEA] int NOT NULL,
    [SL_RFOZ] int NOT NULL,
    [SL_ID] int NOT NULL,
    [SL_FOLGE] int NOT NULL,
    [SL_NODE] int NOT NULL,
    [SL_AART] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SOZU] (
    [ID] int NOT NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [ZUSCHLAG_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [ZUSCHLAG_ART] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [ZUSCHLAG] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SOZU_KPF] (
    [ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_STAT_TEMP] (
    [AGG] int NOT NULL,
    [AART] int NOT NULL,
    [STUECK] int NULL,
    [QM] decimal(28,8) NULL,
    [LFM] decimal(28,8) NULL,
    [ZEIT] decimal(28,8) NULL,
    [KOSTEN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_STATI_GRP] (
    [GRP_NR] int NOT NULL,
    [HP_ART] int NOT NULL,
    [NR1] int NOT NULL,
    [NR2] int NOT NULL,
    [TYP1] int NOT NULL,
    [TYP2] int NOT NULL,
    [PRIORITY] int NOT NULL,
    [GRP_TEXT] nvarchar(40) NOT NULL,
    [GRP_EXCL] nvarchar(40) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_STATISTIK] (
    [AGG] int NOT NULL,
    [AART] int NOT NULL,
    [DATUM] date NOT NULL,
    [STUECK] int NULL,
    [QM] decimal(28,8) NULL,
    [LFM] decimal(28,8) NULL,
    [ZEIT] decimal(28,8) NULL,
    [KOSTEN] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_STGRP_TEMP] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM] int NOT NULL,
    [PART] int NOT NULL,
    [PRGP] int NOT NULL,
    [PROD] int NOT NULL,
    [GRP_NR] int NOT NULL,
    [AVB] nvarchar(40) NOT NULL,
    [MENGE] int NOT NULL,
    [QM] decimal(28,8) NOT NULL,
    [ZK] decimal(28,8) NOT NULL,
    [MK] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_SYPAR] (
    [SCHICHTENZAHL] int NOT NULL,
    [MODUS] int NOT NULL,
    [ABGLEICH] int NOT NULL,
    [DT_HERIT] int NULL,
    [BATCHFLAG] int NULL,
    [AUTOBAL] int NULL,
    [SPERRSTUNDE] int NULL,
    [PRGOST] nvarchar(20) NULL,
    [AUTOPOOL] int NULL,
    [B_EXCLUDE] nvarchar(20) NULL,
    [MESSREC] nvarchar(40) NULL,
    [MESSFLAG] int NULL,
    [FAN_STAT_AKTIV] int NULL,
    [LOWPRIO] int NULL,
    [SPLIT_LIMIT] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_TEMP_ANGEB_ZEIT] (
    [AGG] int NOT NULL,
    [ARBART] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [ARBFOLGE] int NULL,
    [AVB] int NULL,
    [BMENGE] int NULL,
    [BOM_LEVEL] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [DATUM_PROD] datetime NULL,
    [FARBE] int NULL,
    [FERTIG] int NULL,
    [KANTEN] nvarchar(8) NULL,
    [KOSTEN_GES] decimal(28,8) NOT NULL,
    [KZ_FIXED] int NULL,
    [KZ_IGNORE] int NULL,
    [KZ_NOP] int NULL,
    [KZ_SELECTED] int NULL,
    [MAT] decimal(28,8) NULL,
    [POOL_POS] int NULL,
    [PRIO] int NULL,
    [PRL] int NULL,
    [PROD_ZEIT] datetime NULL,
    [PRODART] int NULL,
    [PRODGRP] int NULL,
    [RFOZ] int NULL,
    [RZ_KENNUNG] int NULL,
    [SCHICHT] int NULL,
    [STUECK] int NOT NULL,
    [SUB_POS] int NULL,
    [TEIL_ID] int NULL,
    [VWT] decimal(28,8) NULL,
    [WERK] int NOT NULL,
    [ZEIT] decimal(28,8) NOT NULL,
    [ZW_LFM] decimal(28,8) NULL,
    [ZW_QM] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_TEMP_ZEIT] (
    [AUFNR] int NOT NULL,
    [POSNR] int NOT NULL,
    [BOM_ID] int NOT NULL,
    [BOM_NODE] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL,
    [AGG] int NOT NULL,
    [ARBART] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [PRODART] int NULL,
    [PRODGRP] int NULL,
    [DATUM_PROD] date NULL,
    [STUECK] int NOT NULL,
    [KZ_SELECTED] int NULL,
    [WERK] int NOT NULL,
    [ZEIT] decimal(28,8) NOT NULL,
    [KOSTEN_GES] decimal(28,8) NOT NULL,
    [ARBFOLGE] int NOT NULL,
    [AVB] int NOT NULL,
    [BMENGE] int NOT NULL,
    [FARBE] int NOT NULL,
    [FERTIG] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [KZ_FIXED] int NOT NULL,
    [KZ_IGNORE] int NOT NULL,
    [KZ_NOP] int NOT NULL,
    [MAT] decimal(28,8) NOT NULL,
    [POOL_POS] int NOT NULL,
    [PRIO] int NOT NULL,
    [PRL] int NULL,
    [PROD_ZEIT] datetime NULL,
    [RFOZ] int NULL,
    [RZ_KENNUNG] int NOT NULL,
    [SCHICHT] int NULL,
    [SUB_POS] int NOT NULL,
    [TEIL_ID] int NULL,
    [VWT] decimal(28,8) NOT NULL,
    [ZW_LFM] decimal(28,8) NULL,
    [ZW_QM] decimal(28,8) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_TR_SONDER] (
    [ARBART] int NOT NULL,
    [AGG] int NOT NULL,
    [AGG_MINBR] int NULL,
    [AGG_MAXBR] int NULL,
    [AGG_MINHO] int NULL,
    [AGG_MAXHO] int NULL,
    [AGG_MINSZR] int NULL,
    [AGG_MAXSZR] int NULL,
    [AGG_MINGESST] int NULL,
    [AGG_MAXGESST] int NULL,
    [AGG_MINST] int NULL,
    [AGG_MAXST] int NULL,
    [AGG_MAXGEW] int NULL,
    [AGG_GAS] int NULL,
    [AGG_KS] int NULL,
    [AGG_MODELL] int NULL,
    [AGG_SPROSSEN] int NULL,
    [AGG_SCHEIBEN] int NULL,
    [AGG_MAXFL] decimal(28,8) NULL,
    [AGG_MINANZ] int NULL,
    [AGG_MAXANZ] int NULL,
    [AGG_MAXSV] decimal(28,8) NULL,
    [AGG_MINDURM] decimal(28,8) NULL,
    [AGG_MAXDURM] decimal(28,8) NULL,
    [AGG_DRB] int NOT NULL,
    [AGG_SERIENTAB] int NOT NULL,
    [AGG_MINECKRAD] decimal(28,8) NULL,
    [AGG_MAXECKRAD] decimal(28,8) NULL,
    [AGG_MINFL] decimal(28,8) NULL,
    [AGG_MINDG] decimal(28,8) NULL,
    [AGG_MINGW] int NULL,
    [AGG_MAXGW] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_UEZMATRIX] (
    [ARB1] int NOT NULL,
    [ARB2] int NOT NULL,
    [UEZ] int NOT NULL,
    [RASTER] int NOT NULL,
    [FT_FLAG] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_UNIVZUO] (
    [PRODUKT] int NOT NULL,
    [FOLGEARB] int NOT NULL,
    [MODELL] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [KANTEN] nvarchar(8) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_VOMASPERRE] (
    [AGG] int NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [ARBNR] int NOT NULL,
    [FLAG] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_VWTMATRIX] (
    [PRB1] int NOT NULL,
    [PRB2] int NOT NULL,
    [RASTER] int NOT NULL,
    [VWT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_WGRSPERRE] (
    [WGR] nvarchar(3) NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [AGG_NR] int NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_WGRZUO] (
    [WGR] nvarchar(3) NOT NULL,
    [FOLGE_NR] int NOT NULL,
    [ARB_NR] int NOT NULL,
    [BEMERKUNG] nvarchar(40) NULL,
    [FAKTOR] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_WML_SP] (
    [SP_NR] int NOT NULL,
    [QM_ST] int NOT NULL,
    [HP] int NOT NULL,
    [TYPHP] int NOT NULL,
    [NR0] int NOT NULL,
    [TYP0] int NOT NULL,
    [NR1] int NOT NULL,
    [TYP1] int NOT NULL,
    [NR2] int NOT NULL,
    [TYP2] int NOT NULL,
    [GR_KLASSE] nvarchar(40) NULL,
    [GRP_TEXT1] nvarchar(40) NOT NULL,
    [GRP_TEXT2] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_WML_VIEW] (
    [DATUM] date NOT NULL,
    [SPALTE] int NOT NULL,
    [INHALT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ZEIT] (
    [ID] int NOT NULL,
    [AGG_ID] int NOT NULL,
    [ZEIT_TYP] nvarchar(20) NOT NULL,
    [SPRACH_BASIS] int NOT NULL,
    [GRENZE1] decimal(28,8) NOT NULL,
    [GRENZE2] decimal(28,8) NOT NULL,
    [GRENZE3] decimal(28,8) NOT NULL,
    [ZEIT] decimal(28,8) NOT NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ZEITKPF] (
    [ID] int NOT NULL,
    [AGG_ID] int NOT NULL,
    [BEZ] nvarchar(40) NULL,
    [FORM] int NOT NULL,
    [GTYP1] nvarchar(40) NOT NULL,
    [GTYP2] nvarchar(40) NOT NULL,
    [GTYP3] nvarchar(40) NOT NULL,
    [SONST_ZUSCHL] int NOT NULL,
    [PRIORITY] int NOT NULL,
    [DATUM] date NULL,
    [MITARB_ID] nvarchar(40) NOT NULL,
    [RZ_KENNUNG] int NOT NULL,
    [RZ_FARBE] int NOT NULL,
    [ALTALCIM] int NOT NULL,
    [MINZEIT] decimal(28,8) NOT NULL,
    [RZ_ZEIT] decimal(28,8) NULL,
    [TYPE_ID] int NULL,
    [ROWID] char(36) NOT NULL
);

CREATE TABLE SYSADM.[ZW_ZGLTYPEN] (
    [ZGL_TYP] int NOT NULL,
    [ZGL_TYPBEZ] nvarchar(40) NULL,
    [ROWID] char(36) NOT NULL
);
GO
ALTER TABLE SYSADM.[AD_ATTACH] ADD CONSTRAINT [PK_AD_ATTACH] PRIMARY KEY CLUSTERED ([VORGANG], [NAME]);
ALTER TABLE SYSADM.[AD_CACHE] ADD CONSTRAINT [PK_AD_CACHE] PRIMARY KEY CLUSTERED ([TABLENAME]);
ALTER TABLE SYSADM.[AD_OP] ADD CONSTRAINT [PK_AD_OP] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[AD_PROJEKT] ADD CONSTRAINT [PK_AD_PROJEKT] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[AD_SETTINGS] ADD CONSTRAINT [PK_AD_SETTINGS] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[AD_TERMIN] ADD CONSTRAINT [PK_AD_TERMIN] PRIMARY KEY CLUSTERED ([BEARBEITER], [ZEIT]);
ALTER TABLE SYSADM.[AD_UPDATE] ADD CONSTRAINT [PK_AD_UPDATE] PRIMARY KEY CLUSTERED ([SCRIPT]);
ALTER TABLE SYSADM.[AD_VORGANG] ADD CONSTRAINT [PK_AD_VORGANG] PRIMARY KEY CLUSTERED ([ID], [KUNDE]);
ALTER TABLE SYSADM.[BA_BEARB_WERTE] ADD CONSTRAINT [PK_BA_BEARB_WERTE] PRIMARY KEY CLUSTERED ([PRODUKT], [PARAM_NAME], [WERT]);
ALTER TABLE SYSADM.[BA_CEKAL_PRODUKT] ADD CONSTRAINT [PK_BA_CEKAL_PRODUKT] PRIMARY KEY CLUSTERED ([EBENE], [PRODUKT], [FARB_ID]);
ALTER TABLE SYSADM.[BA_CEKAL_RESTRICT] ADD CONSTRAINT [PK_BA_CEKAL_RESTRICT] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_CEKAL_RESULT] ADD CONSTRAINT [PK_BA_CEKAL_RESULT] PRIMARY KEY CLUSTERED ([CLASS], [S1], [S2], [S3], [S4], [S5], [S6], [S7], [S8], [S9], [S10], [DIM_VALUE]);
ALTER TABLE SYSADM.[BA_CEKAL_TEXT] ADD CONSTRAINT [PK_BA_CEKAL_TEXT] PRIMARY KEY CLUSTERED ([RESULT1], [RESULT2], [RESULT3], [RESULT4], [RESULT5], [RESULT6], [RESULT7], [RESULT8], [RESULT9], [RESULT10]);
ALTER TABLE SYSADM.[BA_CEKAL_ZUORD] ADD CONSTRAINT [PK_BA_CEKAL_ZUORD] PRIMARY KEY CLUSTERED ([CLASS], [EBENE], [PRODUKT], [FARB_ID]);
ALTER TABLE SYSADM.[BA_DOORART_MASTXT] ADD CONSTRAINT [PK_BA_DOORART_MASTXT] PRIMARY KEY CLUSTERED ([SPRACHE], [TXT_ID]);
ALTER TABLE SYSADM.[BA_DOORART_MAT] ADD CONSTRAINT [PK_BA_DOORART_MAT] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [FARB_ID]);
ALTER TABLE SYSADM.[BA_DOORART_MODELLE] ADD CONSTRAINT [PK_BA_DOORART_MODELLE] PRIMARY KEY CLUSTERED ([MODELL_ID]);
ALTER TABLE SYSADM.[BA_DOORART_TI] ADD CONSTRAINT [PK_BA_DOORART_TI] PRIMARY KEY CLUSTERED ([TI_ID]);
ALTER TABLE SYSADM.[BA_EDGEQUALITY] ADD CONSTRAINT [PK_BA_EDGEQUALITY] PRIMARY KEY CLUSTERED ([PRODUKT], [MOD_NR]);
ALTER TABLE SYSADM.[BA_EKKAL] ADD CONSTRAINT [PK_BA_EKKAL] PRIMARY KEY CLUSTERED ([BA_PRODUKT]);
ALTER TABLE SYSADM.[BA_EXCHANGE] ADD CONSTRAINT [PK_BA_EXCHANGE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_EXCHANGE_DETAIL] ADD CONSTRAINT [PK_BA_EXCHANGE_DETAIL] PRIMARY KEY CLUSTERED ([ID], [BA_PRODUKT]);
ALTER TABLE SYSADM.[BA_FITTING_MACRO] ADD CONSTRAINT [PK_BA_FITTING_MACRO] PRIMARY KEY CLUSTERED ([ARTICLE], [AREA], [MACRO], [DIN_LR], [NUMBER]);
ALTER TABLE SYSADM.[BA_GESTELLARTEN] ADD CONSTRAINT [PK_BA_GESTELLARTEN] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_GESTELLE] ADD CONSTRAINT [PK_BA_GESTELLE] PRIMARY KEY CLUSTERED ([GESTELL_NR], [GESTELLART]);
ALTER TABLE SYSADM.[BA_GESTELLE_AUFTR] ADD CONSTRAINT [PK_BA_GESTELLE_AUFTR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_GESTELLE_BEST] ADD CONSTRAINT [PK_BA_GESTELLE_BEST] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_GESTELLE_EA] ADD CONSTRAINT [PK_BA_GESTELLE_EA] PRIMARY KEY CLUSTERED ([LFD_NR]);
ALTER TABLE SYSADM.[BA_GESTELLE_EA_DETAIL] ADD CONSTRAINT [PK_BA_GESTELLE_EA_DETAIL] PRIMARY KEY CLUSTERED ([LFD_NR], [AUFTR_ID], [KUNDEN_ID]);
ALTER TABLE SYSADM.[BA_GUETE_RESTRICT] ADD CONSTRAINT [PK_BA_GUETE_RESTRICT] PRIMARY KEY CLUSTERED ([GLAS], [GAS], [PRIO], [LAND], [ID]);
ALTER TABLE SYSADM.[BA_GUETE_TEXT] ADD CONSTRAINT [PK_BA_GUETE_TEXT] PRIMARY KEY CLUSTERED ([GLAS], [GAS], [PRIO], [LAND]);
ALTER TABLE SYSADM.[BA_ISO_AUFBAU] ADD CONSTRAINT [PK_BA_ISO_AUFBAU] PRIMARY KEY CLUSTERED ([KEYNR]);
ALTER TABLE SYSADM.[BA_KU_PRODUKTE] ADD CONSTRAINT [PK_BA_KU_PRODUKTE] PRIMARY KEY CLUSTERED ([KUNDE], [BA_PRODUKT]);
ALTER TABLE SYSADM.[BA_LAGMA] ADD CONSTRAINT [PK_BA_LAGMA] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [BA_BREITE], [BA_HOEHE], [BA_INHALT]);
ALTER TABLE SYSADM.[BA_LENR] ADD CONSTRAINT [PK_BA_LENR] PRIMARY KEY CLUSTERED ([LENR]);
ALTER TABLE SYSADM.[BA_LENR_DETAIL] ADD CONSTRAINT [PK_BA_LENR_DETAIL] PRIMARY KEY CLUSTERED ([LENR], [SPRACH_ID]);
ALTER TABLE SYSADM.[BA_LENR_TI] ADD CONSTRAINT [PK_BA_LENR_TI] PRIMARY KEY CLUSTERED ([LENR], [TI_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_ATT_POS] ADD CONSTRAINT [PK_BA_MAKRO_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_BEARB] ADD CONSTRAINT [PK_BA_MAKRO_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_GEBOGEN] ADD CONSTRAINT [PK_BA_MAKRO_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_GGMOD_POS] ADD CONSTRAINT [PK_BA_MAKRO_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_GGMOD_STKL] ADD CONSTRAINT [PK_BA_MAKRO_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BA_MAKRO_MODELL] ADD CONSTRAINT [PK_BA_MAKRO_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_POS] ADD CONSTRAINT [PK_BA_MAKRO_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_POS_EX] ADD CONSTRAINT [PK_BA_MAKRO_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_POS_TI] ADD CONSTRAINT [PK_BA_MAKRO_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BA_MAKRO_POS_ZEIT] ADD CONSTRAINT [PK_BA_MAKRO_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BA_MAKRO_SPROSSEN] ADD CONSTRAINT [PK_BA_MAKRO_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_SPROSSENPREISE] ADD CONSTRAINT [PK_BA_MAKRO_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_STKL] ADD CONSTRAINT [PK_BA_MAKRO_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BA_MAKRO_STL_ZEIT] ADD CONSTRAINT [PK_BA_MAKRO_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BA_MAKRO_TXT] ADD CONSTRAINT [PK_BA_MAKRO_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BA_MASSZU] ADD CONSTRAINT [PK_BA_MASSZU] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_MASSZU_WERT] ADD CONSTRAINT [PK_BA_MASSZU_WERT] PRIMARY KEY CLUSTERED ([ID], [DICKE]);
ALTER TABLE SYSADM.[BA_NUMKREISE] ADD CONSTRAINT [PK_BA_NUMKREISE] PRIMARY KEY CLUSTERED ([ID], [AV_BEREICH], [MITARBEITER]);
ALTER TABLE SYSADM.[BA_PLANSTK] ADD CONSTRAINT [PK_BA_PLANSTK] PRIMARY KEY CLUSTERED ([BA_TYP]);
ALTER TABLE SYSADM.[BA_POOL_GLASART] ADD CONSTRAINT [PK_BA_POOL_GLASART] PRIMARY KEY CLUSTERED ([GLASART]);
ALTER TABLE SYSADM.[BA_PROD_VERPEINH] ADD CONSTRAINT [PK_BA_PROD_VERPEINH] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [BA_VERP_EINH]);
ALTER TABLE SYSADM.[BA_PRODGRP_REST] ADD CONSTRAINT [PK_BA_PRODGRP_REST] PRIMARY KEY CLUSTERED ([PRDKTART_ID], [PRDKTGRP_ID], [GLASDICKE_VON], [GLASDICKE_BIS], [REST_ID]);
ALTER TABLE SYSADM.[BA_PRODUKT_AUFBAU] ADD CONSTRAINT [PK_BA_PRODUKT_AUFBAU] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_PRODUKT_KZ] ADD CONSTRAINT [PK_BA_PRODUKT_KZ] PRIMARY KEY CLUSTERED ([PRODUKT], [MANDANT], [AVBEREICH], [GUELTIG_IN]);
ALTER TABLE SYSADM.[BA_PRODUKTE] ADD CONSTRAINT [PK_BA_PRODUKTE] PRIMARY KEY CLUSTERED ([BA_PRODUKT]);
ALTER TABLE SYSADM.[BA_PRODUKTE_ATTACH] ADD CONSTRAINT [PK_BA_PRODUKTE_ATTACH] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [LFD_NR]);
ALTER TABLE SYSADM.[BA_PRODUKTE_BEZ] ADD CONSTRAINT [PK_BA_PRODUKTE_BEZ] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [SPRACH_ID]);
ALTER TABLE SYSADM.[BA_PRODUKTE_FAR] ADD CONSTRAINT [PK_BA_PRODUKTE_FAR] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [BA_FARB_ID]);
ALTER TABLE SYSADM.[BA_PRODUKTE_MOTIV] ADD CONSTRAINT [PK_BA_PRODUKTE_MOTIV] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_PRODUKTE_TI] ADD CONSTRAINT [PK_BA_PRODUKTE_TI] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [TI_NR]);
ALTER TABLE SYSADM.[BA_SN_RULES] ADD CONSTRAINT [PK_BA_SN_RULES] PRIMARY KEY CLUSTERED ([BA_PRODUKTART], [BA_PRODUKTGRP]);
ALTER TABLE SYSADM.[BA_STUKL] ADD CONSTRAINT [PK_BA_STUKL] PRIMARY KEY CLUSTERED ([PRODUKT], [BOM_ID]);
ALTER TABLE SYSADM.[BA_STUKL_BEARB] ADD CONSTRAINT [PK_BA_STUKL_BEARB] PRIMARY KEY CLUSTERED ([PRODUKT], [BOM_ID]);
ALTER TABLE SYSADM.[BA_STUKL_GEBOGEN] ADD CONSTRAINT [PK_BA_STUKL_GEBOGEN] PRIMARY KEY CLUSTERED ([PRODUKT], [BOM_ID]);
ALTER TABLE SYSADM.[BA_STUKL_MODELL] ADD CONSTRAINT [PK_BA_STUKL_MODELL] PRIMARY KEY CLUSTERED ([PRODUKT], [BOM_ID]);
ALTER TABLE SYSADM.[BA_STUKL_SPROSSEN] ADD CONSTRAINT [PK_BA_STUKL_SPROSSEN] PRIMARY KEY CLUSTERED ([PRODUKT], [BOM_ID]);
ALTER TABLE SYSADM.[BA_SZR_RESTRICT] ADD CONSTRAINT [PK_BA_SZR_RESTRICT] PRIMARY KEY CLUSTERED ([PRODART], [PRODGRP], [ID]);
ALTER TABLE SYSADM.[BA_TECH_INFO] ADD CONSTRAINT [PK_BA_TECH_INFO] PRIMARY KEY CLUSTERED ([ID], [GLAS_1], [GLAS_2], [GLAS_3], [SZR_1], [SZR_2], [GAS_1], [GAS_2]);
ALTER TABLE SYSADM.[BA_TI_TEXTE] ADD CONSTRAINT [PK_BA_TI_TEXTE] PRIMARY KEY CLUSTERED ([TI_NR], [SPRACH_ID]);
ALTER TABLE SYSADM.[BA_TOE_RULES] ADD CONSTRAINT [PK_BA_TOE_RULES] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BA_TR_WGR] ADD CONSTRAINT [PK_BA_TR_WGR] PRIMARY KEY CLUSTERED ([PRODUKT], [KOSTENSTELLE]);
ALTER TABLE SYSADM.[BA_UEBERMENGEN] ADD CONSTRAINT [PK_BA_UEBERMENGEN] PRIMARY KEY CLUSTERED ([PROD_ID], [ID]);
ALTER TABLE SYSADM.[BA_XOPT_JUMBO] ADD CONSTRAINT [PK_BA_XOPT_JUMBO] PRIMARY KEY CLUSTERED ([GLASART], [DICKE], [JUMBO_NR]);
ALTER TABLE SYSADM.[BA_XOPT_TISCHJUMBO] ADD CONSTRAINT [PK_BA_XOPT_TISCHJUMBO] PRIMARY KEY CLUSTERED ([TISCH_ID], [GLASART], [DICKE], [JUMBO_NR]);
ALTER TABLE SYSADM.[BW_ALCIM_EINH_STKL] ADD CONSTRAINT [PK_BW_ALCIM_EINH_STKL] PRIMARY KEY CLUSTERED ([EINHEIT]);
ALTER TABLE SYSADM.[BW_ALCIM_EINHEIT] ADD CONSTRAINT [PK_BW_ALCIM_EINHEIT] PRIMARY KEY CLUSTERED ([EINHEIT]);
ALTER TABLE SYSADM.[BW_ALCIM_RECEIVE] ADD CONSTRAINT [PK_BW_ALCIM_RECEIVE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_ATT_KOPF] ADD CONSTRAINT [PK_BW_ANFR_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANFR_ATT_POS] ADD CONSTRAINT [PK_BW_ANFR_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANFR_AUFTR] ADD CONSTRAINT [PK_BW_ANFR_AUFTR] PRIMARY KEY CLUSTERED ([ANFR_ID], [ANFR_POS], [AUFTR_ID], [AUFTR_POS]);
ALTER TABLE SYSADM.[BW_ANFR_AUFTR_STKL] ADD CONSTRAINT [PK_BW_ANFR_AUFTR_STKL] PRIMARY KEY CLUSTERED ([ANFR_ID], [ANFR_POS], [AUFTR_ID], [AUFTR_POS], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_BEARB] ADD CONSTRAINT [PK_BW_ANFR_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_GEBOGEN] ADD CONSTRAINT [PK_BW_ANFR_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_GGMOD_POS] ADD CONSTRAINT [PK_BW_ANFR_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANFR_GGMOD_STKL] ADD CONSTRAINT [PK_BW_ANFR_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BW_ANFR_KOPF] ADD CONSTRAINT [PK_BW_ANFR_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_ANFR_KOPF_EX] ADD CONSTRAINT [PK_BW_ANFR_KOPF_EX] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_ANFR_KTXT] ADD CONSTRAINT [PK_BW_ANFR_KTXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANFR_MODELL] ADD CONSTRAINT [PK_BW_ANFR_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_POS] ADD CONSTRAINT [PK_BW_ANFR_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANFR_POS_EX] ADD CONSTRAINT [PK_BW_ANFR_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANFR_POS_TI] ADD CONSTRAINT [PK_BW_ANFR_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BW_ANFR_POS_ZEIT] ADD CONSTRAINT [PK_BW_ANFR_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BW_ANFR_SPROSSEN] ADD CONSTRAINT [PK_BW_ANFR_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_SPROSSENPREISE] ADD CONSTRAINT [PK_BW_ANFR_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BW_ANFR_STKL] ADD CONSTRAINT [PK_BW_ANFR_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANFR_STL_ZEIT] ADD CONSTRAINT [PK_BW_ANFR_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BW_ANFR_TXT] ADD CONSTRAINT [PK_BW_ANFR_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_ATT_KOPF] ADD CONSTRAINT [PK_BW_ANGEB_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_ATT_POS] ADD CONSTRAINT [PK_BW_ANGEB_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_BEARB] ADD CONSTRAINT [PK_BW_ANGEB_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_GEBOGEN] ADD CONSTRAINT [PK_BW_ANGEB_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_GGMOD_POS] ADD CONSTRAINT [PK_BW_ANGEB_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_GGMOD_STKL] ADD CONSTRAINT [PK_BW_ANGEB_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BW_ANGEB_KOPF] ADD CONSTRAINT [PK_BW_ANGEB_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_ANGEB_KOPF_EX] ADD CONSTRAINT [PK_BW_ANGEB_KOPF_EX] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_ANGEB_KTXT] ADD CONSTRAINT [PK_BW_ANGEB_KTXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_MODELL] ADD CONSTRAINT [PK_BW_ANGEB_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_OPTI] ADD CONSTRAINT [PK_BW_ANGEB_OPTI] PRIMARY KEY CLUSTERED ([OPTI_ID], [ID], [GLASSTYPE]);
ALTER TABLE SYSADM.[BW_ANGEB_POS] ADD CONSTRAINT [PK_BW_ANGEB_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_POS_EX] ADD CONSTRAINT [PK_BW_ANGEB_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_POS_TI] ADD CONSTRAINT [PK_BW_ANGEB_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BW_ANGEB_POS_ZEIT] ADD CONSTRAINT [PK_BW_ANGEB_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BW_ANGEB_SPROSSEN] ADD CONSTRAINT [PK_BW_ANGEB_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_SPROSSENPREISE] ADD CONSTRAINT [PK_BW_ANGEB_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_STKL] ADD CONSTRAINT [PK_BW_ANGEB_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_ANGEB_STL_ZEIT] ADD CONSTRAINT [PK_BW_ANGEB_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BW_ANGEB_TXT] ADD CONSTRAINT [PK_BW_ANGEB_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_ANLIEF] ADD CONSTRAINT [PK_BW_ANLIEF] PRIMARY KEY CLUSTERED ([KUNDE], [DATUM], [TOUR]);
ALTER TABLE SYSADM.[BW_AUFTR_ANZAHLUNG] ADD CONSTRAINT [PK_BW_AUFTR_ANZAHLUNG] PRIMARY KEY CLUSTERED ([ID], [PAYMENT_NO]);
ALTER TABLE SYSADM.[BW_AUFTR_ATT_KOPF] ADD CONSTRAINT [PK_BW_AUFTR_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_ATT_POS] ADD CONSTRAINT [PK_BW_AUFTR_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_AUFTR] ADD CONSTRAINT [PK_BW_AUFTR_AUFTR] PRIMARY KEY CLUSTERED ([AUFTR_ID], [AUFTR_POS], [INT_AUFTR_ID], [INT_AUFTR_POS]);
ALTER TABLE SYSADM.[BW_AUFTR_AUFTR_STL] ADD CONSTRAINT [PK_BW_AUFTR_AUFTR_STL] PRIMARY KEY CLUSTERED ([AUFTR_ID], [AUFTR_POS], [BOM_ID], [INT_AUFTR_ID], [INT_AUFTR_POS]);
ALTER TABLE SYSADM.[BW_AUFTR_BEARB] ADD CONSTRAINT [PK_BW_AUFTR_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_EXPORT] ADD CONSTRAINT [PK_BW_AUFTR_EXPORT] PRIMARY KEY CLUSTERED ([EMPFAENGER], [ID], [DOK_TYP]);
ALTER TABLE SYSADM.[BW_AUFTR_GEBOGEN] ADD CONSTRAINT [PK_BW_AUFTR_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_GGMOD_POS] ADD CONSTRAINT [PK_BW_AUFTR_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_GGMOD_STKL] ADD CONSTRAINT [PK_BW_AUFTR_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BW_AUFTR_INFO] ADD CONSTRAINT [PK_BW_AUFTR_INFO] PRIMARY KEY CLUSTERED ([DATUM], [WGR_KMB_STAT]);
ALTER TABLE SYSADM.[BW_AUFTR_KOPF] ADD CONSTRAINT [PK_BW_AUFTR_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_AUFTR_KOPF_EX] ADD CONSTRAINT [PK_BW_AUFTR_KOPF_EX] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_AUFTR_KTXT] ADD CONSTRAINT [PK_BW_AUFTR_KTXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_LWBEW_POS] ADD CONSTRAINT [PK_BW_AUFTR_LWBEW_POS] PRIMARY KEY CLUSTERED ([TYP], [ID], [POS_NR], [IDENT], [LAGERORT]);
ALTER TABLE SYSADM.[BW_AUFTR_LWBEW_ST] ADD CONSTRAINT [PK_BW_AUFTR_LWBEW_ST] PRIMARY KEY CLUSTERED ([TYP], [ID], [POS_NR], [BOM_ID], [IDENT], [LAGERORT]);
ALTER TABLE SYSADM.[BW_AUFTR_MODELL] ADD CONSTRAINT [PK_BW_AUFTR_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_MTS] ADD CONSTRAINT [PK_BW_AUFTR_MTS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_OPTI] ADD CONSTRAINT [PK_BW_AUFTR_OPTI] PRIMARY KEY CLUSTERED ([OPTI_ID], [ID], [GLASSTYPE]);
ALTER TABLE SYSADM.[BW_AUFTR_PARA_KGP] ADD CONSTRAINT [PK_BW_AUFTR_PARA_KGP] PRIMARY KEY CLUSTERED ([GRUPPE], [PRODUKT]);
ALTER TABLE SYSADM.[BW_AUFTR_PARA_KGW] ADD CONSTRAINT [PK_BW_AUFTR_PARA_KGW] PRIMARY KEY CLUSTERED ([GRUPPE], [WGR]);
ALTER TABLE SYSADM.[BW_AUFTR_PARA_KUP] ADD CONSTRAINT [PK_BW_AUFTR_PARA_KUP] PRIMARY KEY CLUSTERED ([ID], [PRODUKT]);
ALTER TABLE SYSADM.[BW_AUFTR_PARA_KUW] ADD CONSTRAINT [PK_BW_AUFTR_PARA_KUW] PRIMARY KEY CLUSTERED ([ID], [WGR]);
ALTER TABLE SYSADM.[BW_AUFTR_PINFO] ADD CONSTRAINT [PK_BW_AUFTR_PINFO] PRIMARY KEY CLUSTERED ([DATUM], [WGR_KMB_STAT]);
ALTER TABLE SYSADM.[BW_AUFTR_POS] ADD CONSTRAINT [PK_BW_AUFTR_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_POS_EX] ADD CONSTRAINT [PK_BW_AUFTR_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_POS_TI] ADD CONSTRAINT [PK_BW_AUFTR_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BW_AUFTR_POS_ZEIT] ADD CONSTRAINT [PK_BW_AUFTR_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BW_AUFTR_RELIEF_NR] ADD CONSTRAINT [PK_BW_AUFTR_RELIEF_NR] PRIMARY KEY CLUSTERED ([TYP], [AUFTRAG], [PRAEFIX], [NUMMER]);
ALTER TABLE SYSADM.[BW_AUFTR_RINFO] ADD CONSTRAINT [PK_BW_AUFTR_RINFO] PRIMARY KEY CLUSTERED ([DATUM], [WGR_KMB_STAT]);
ALTER TABLE SYSADM.[BW_AUFTR_SERIAL] ADD CONSTRAINT [PK_BW_AUFTR_SERIAL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [IDENT], [IDENTKOMP]);
ALTER TABLE SYSADM.[BW_AUFTR_SPROSSEN] ADD CONSTRAINT [PK_BW_AUFTR_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_SPROSSENPREISE] ADD CONSTRAINT [PK_BW_AUFTR_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_STKL] ADD CONSTRAINT [PK_BW_AUFTR_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_AUFTR_STL_ZEIT] ADD CONSTRAINT [PK_BW_AUFTR_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BW_AUFTR_TXT] ADD CONSTRAINT [PK_BW_AUFTR_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_BEST_ANZAHLUNG] ADD CONSTRAINT [PK_BW_BEST_ANZAHLUNG] PRIMARY KEY CLUSTERED ([ID], [PAYMENT_NO]);
ALTER TABLE SYSADM.[BW_BEST_ATT_KOPF] ADD CONSTRAINT [PK_BW_BEST_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_BEST_ATT_POS] ADD CONSTRAINT [PK_BW_BEST_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_BEST_AUFTR] ADD CONSTRAINT [PK_BW_BEST_AUFTR] PRIMARY KEY CLUSTERED ([BEST_ID], [BEST_POS], [AUFTR_ID], [AUFTR_POS]);
ALTER TABLE SYSADM.[BW_BEST_AUFTR_STKL] ADD CONSTRAINT [PK_BW_BEST_AUFTR_STKL] PRIMARY KEY CLUSTERED ([BEST_ID], [BEST_POS], [AUFTR_ID], [AUFTR_POS], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_BEARB] ADD CONSTRAINT [PK_BW_BEST_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_GEBOGEN] ADD CONSTRAINT [PK_BW_BEST_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_GGMOD_POS] ADD CONSTRAINT [PK_BW_BEST_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_GGMOD_STKL] ADD CONSTRAINT [PK_BW_BEST_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_AB] ADD CONSTRAINT [PK_BW_BEST_IMPORT_AB] PRIMARY KEY CLUSTERED ([LIEFERANT], [AB_NR]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_AB_POS] ADD CONSTRAINT [PK_BW_BEST_IMPORT_AB_POS] PRIMARY KEY CLUSTERED ([LIEFERANT], [AB_NR], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_LIEF] ADD CONSTRAINT [PK_BW_BEST_IMPORT_LIEF] PRIMARY KEY CLUSTERED ([LIEFERANT], [LIEFSCH_NR]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_LIEF_POS] ADD CONSTRAINT [PK_BW_BEST_IMPORT_LIEF_POS] PRIMARY KEY CLUSTERED ([LIEFERANT], [LIEFSCH_NR], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_RECHN] ADD CONSTRAINT [PK_BW_BEST_IMPORT_RECHN] PRIMARY KEY CLUSTERED ([LIEFERANT], [RECHNUNGS_NR]);
ALTER TABLE SYSADM.[BW_BEST_IMPORT_RECHN_POS] ADD CONSTRAINT [PK_BW_BEST_IMPORT_RECHN_POS] PRIMARY KEY CLUSTERED ([LIEFERANT], [RECHNUNGS_NR], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_KOPF] ADD CONSTRAINT [PK_BW_BEST_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_BEST_KOPF_EX] ADD CONSTRAINT [PK_BW_BEST_KOPF_EX] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_BEST_KTXT] ADD CONSTRAINT [PK_BW_BEST_KTXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_BEST_LWBEW_POS] ADD CONSTRAINT [PK_BW_BEST_LWBEW_POS] PRIMARY KEY CLUSTERED ([TYP], [ID], [POS_NR], [IDENT], [LAGERORT]);
ALTER TABLE SYSADM.[BW_BEST_LWBEW_ST] ADD CONSTRAINT [PK_BW_BEST_LWBEW_ST] PRIMARY KEY CLUSTERED ([TYP], [ID], [POS_NR], [BOM_ID], [IDENT], [LAGERORT]);
ALTER TABLE SYSADM.[BW_BEST_MAN] ADD CONSTRAINT [PK_BW_BEST_MAN] PRIMARY KEY CLUSTERED ([BEST_ID], [BEST_POS]);
ALTER TABLE SYSADM.[BW_BEST_MODELL] ADD CONSTRAINT [PK_BW_BEST_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_OPENTRANS] ADD CONSTRAINT [PK_BW_BEST_OPENTRANS] PRIMARY KEY CLUSTERED ([EMPFAENGER], [ID]);
ALTER TABLE SYSADM.[BW_BEST_PARA_KGP] ADD CONSTRAINT [PK_BW_BEST_PARA_KGP] PRIMARY KEY CLUSTERED ([GRUPPE], [PRODUKT]);
ALTER TABLE SYSADM.[BW_BEST_PARA_KGW] ADD CONSTRAINT [PK_BW_BEST_PARA_KGW] PRIMARY KEY CLUSTERED ([GRUPPE], [WGR]);
ALTER TABLE SYSADM.[BW_BEST_PARA_KUP] ADD CONSTRAINT [PK_BW_BEST_PARA_KUP] PRIMARY KEY CLUSTERED ([ID], [PRODUKT]);
ALTER TABLE SYSADM.[BW_BEST_PARA_KUW] ADD CONSTRAINT [PK_BW_BEST_PARA_KUW] PRIMARY KEY CLUSTERED ([ID], [WGR]);
ALTER TABLE SYSADM.[BW_BEST_POOLPOS] ADD CONSTRAINT [PK_BW_BEST_POOLPOS] PRIMARY KEY CLUSTERED ([AUFTR_ID], [AUFTR_POS]);
ALTER TABLE SYSADM.[BW_BEST_POOLSTKL] ADD CONSTRAINT [PK_BW_BEST_POOLSTKL] PRIMARY KEY CLUSTERED ([AUFTR_ID], [AUFTR_POS], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_POS] ADD CONSTRAINT [PK_BW_BEST_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_POS_EX] ADD CONSTRAINT [PK_BW_BEST_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_BEST_POS_TI] ADD CONSTRAINT [PK_BW_BEST_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BW_BEST_POS_ZEIT] ADD CONSTRAINT [PK_BW_BEST_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BW_BEST_SPROSSEN] ADD CONSTRAINT [PK_BW_BEST_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_SPROSSENPREISE] ADD CONSTRAINT [PK_BW_BEST_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BW_BEST_STKL] ADD CONSTRAINT [PK_BW_BEST_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_BEST_STL_ZEIT] ADD CONSTRAINT [PK_BW_BEST_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BW_BEST_TXT] ADD CONSTRAINT [PK_BW_BEST_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_BEST_WE] ADD CONSTRAINT [PK_BW_BEST_WE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_CONT_EST_PROD] ADD CONSTRAINT [PK_BW_CONT_EST_PROD] PRIMARY KEY CLUSTERED ([ID], [EST_PROD]);
ALTER TABLE SYSADM.[BW_GESTELL_ZUORD] ADD CONSTRAINT [PK_BW_GESTELL_ZUORD] PRIMARY KEY CLUSTERED ([GESTELL], [EINHEIT], [DATUM]);
ALTER TABLE SYSADM.[BW_GUT_ANZAHLUNG] ADD CONSTRAINT [PK_BW_GUT_ANZAHLUNG] PRIMARY KEY CLUSTERED ([ID], [PAYMENT_NO]);
ALTER TABLE SYSADM.[BW_GUTSCH_ATT_KOPF] ADD CONSTRAINT [PK_BW_GUTSCH_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_ATT_POS] ADD CONSTRAINT [PK_BW_GUTSCH_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_BEARB] ADD CONSTRAINT [PK_BW_GUTSCH_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_GEBOGEN] ADD CONSTRAINT [PK_BW_GUTSCH_GEBOGEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_GGMOD_POS] ADD CONSTRAINT [PK_BW_GUTSCH_GGMOD_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_GGMOD_STKL] ADD CONSTRAINT [PK_BW_GUTSCH_GGMOD_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POS_STL]);
ALTER TABLE SYSADM.[BW_GUTSCH_KOPF] ADD CONSTRAINT [PK_BW_GUTSCH_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_KOPF_EX] ADD CONSTRAINT [PK_BW_GUTSCH_KOPF_EX] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_KTXT] ADD CONSTRAINT [PK_BW_GUTSCH_KTXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_MODELL] ADD CONSTRAINT [PK_BW_GUTSCH_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_POS] ADD CONSTRAINT [PK_BW_GUTSCH_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_POS_EX] ADD CONSTRAINT [PK_BW_GUTSCH_POS_EX] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_POS_TI] ADD CONSTRAINT [PK_BW_GUTSCH_POS_TI] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [TI_NR]);
ALTER TABLE SYSADM.[BW_GUTSCH_POS_ZEIT] ADD CONSTRAINT [PK_BW_GUTSCH_POS_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [ARBART]);
ALTER TABLE SYSADM.[BW_GUTSCH_SPROSSEN] ADD CONSTRAINT [PK_BW_GUTSCH_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_SPROSSENPREISE] ADD CONSTRAINT [PK_BW_GUTSCH_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ELEMENT_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_STKL] ADD CONSTRAINT [PK_BW_GUTSCH_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID]);
ALTER TABLE SYSADM.[BW_GUTSCH_STL_ZEIT] ADD CONSTRAINT [PK_BW_GUTSCH_STL_ZEIT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [BOM_ID], [ARBART]);
ALTER TABLE SYSADM.[BW_GUTSCH_TXT] ADD CONSTRAINT [PK_BW_GUTSCH_TXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[BW_INV_BLOCK] ADD CONSTRAINT [PK_BW_INV_BLOCK] PRIMARY KEY CLUSTERED ([BLOCK_NR], [POS_NR]);
ALTER TABLE SYSADM.[BW_INV_GLOBAL] ADD CONSTRAINT [PK_BW_INV_GLOBAL] PRIMARY KEY CLUSTERED ([AUFN_DATUM]);
ALTER TABLE SYSADM.[BW_INV_LAGERORT] ADD CONSTRAINT [PK_BW_INV_LAGERORT] PRIMARY KEY CLUSTERED ([AUFN_DATUM], [LAGERORT_ID]);
ALTER TABLE SYSADM.[BW_INV_LISTE] ADD CONSTRAINT [PK_BW_INV_LISTE] PRIMARY KEY CLUSTERED ([BELEG_NR], [AUFN_DATUM]);
ALTER TABLE SYSADM.[BW_INV_PR_BER] ADD CONSTRAINT [PK_BW_INV_PR_BER] PRIMARY KEY CLUSTERED ([LAGER_ID], [LAGERORT]);
ALTER TABLE SYSADM.[BW_LADELISTE] ADD CONSTRAINT [PK_BW_LADELISTE] PRIMARY KEY CLUSTERED ([NR]);
ALTER TABLE SYSADM.[BW_LAGER_STAT] ADD CONSTRAINT [PK_BW_LAGER_STAT] PRIMARY KEY CLUSTERED ([DATUM], [LAGER_ID], [LAGERORT], [LAGER_IDENT]);
ALTER TABLE SYSADM.[BW_LAUF_KOPF] ADD CONSTRAINT [PK_BW_LAUF_KOPF] PRIMARY KEY CLUSTERED ([LAUF_NR]);
ALTER TABLE SYSADM.[BW_LOCK_KAPA] ADD CONSTRAINT [PK_BW_LOCK_KAPA] PRIMARY KEY CLUSTERED ([DOK_TYP], [DOK_NUMMER]);
ALTER TABLE SYSADM.[BW_MAIL] ADD CONSTRAINT [PK_BW_MAIL] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_ORDERXML] ADD CONSTRAINT [BW_ORDERXML_STATE] PRIMARY KEY CLUSTERED ([ID], [DOK_TYP], [DATUM_SAVED]);
ALTER TABLE SYSADM.[BW_PRINT_JOBS] ADD CONSTRAINT [PK_BW_PRINT_JOBS] PRIMARY KEY CLUSTERED ([JOB_ID]);
ALTER TABLE SYSADM.[BW_PROD_AUFBAU] ADD CONSTRAINT [PK_BW_PROD_AUFBAU] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_PRODUKT_AUFBAU_LENR] ADD CONSTRAINT [PK_BW_PRODUKT_AUFBAU_LENR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[BW_TEILBEST] ADD CONSTRAINT [PK_BW_TEILBEST] PRIMARY KEY CLUSTERED ([BEST_ID], [BEST_POS]);
ALTER TABLE SYSADM.[BW_TEMP_DATA] ADD CONSTRAINT [PK_BW_TEMP_DATA] PRIMARY KEY CLUSTERED ([MITARB_ID], [LFD_NR], [PARTNER]);
ALTER TABLE SYSADM.[BW_TRANSFER] ADD CONSTRAINT [PK_BW_TRANSFER] PRIMARY KEY CLUSTERED ([DOK_ID], [DATUM_ERFASST], [DOK_TYP]);
ALTER TABLE SYSADM.[BW_VORLAGE] ADD CONSTRAINT [PK_BW_VORLAGE] PRIMARY KEY CLUSTERED ([ID], [DOC_TYPE], [BEARBEITER]);
ALTER TABLE SYSADM.[BW_ZOLL] ADD CONSTRAINT [PK_BW_ZOLL] PRIMARY KEY CLUSTERED ([ID], [SUBID]);
ALTER TABLE SYSADM.[BW_ZOLL_POS] ADD CONSTRAINT [PK_BW_ZOLL_POS] PRIMARY KEY CLUSTERED ([ID], [SUBID], [AUFTRAG_ID], [POS_NR], [STKL_POS]);
ALTER TABLE SYSADM.[CU_SGG_CEDEFAULT] ADD CONSTRAINT [PK_CU_SGG_CEDEFAULT] PRIMARY KEY CLUSTERED ([TYP]);
ALTER TABLE SYSADM.[CU_SGG_CEDIMREST] ADD CONSTRAINT [PK_CU_SGG_CEDIMREST] PRIMARY KEY CLUSTERED ([CECODE]);
ALTER TABLE SYSADM.[CU_SGG_CEHS] ADD CONSTRAINT [PK_CU_SGG_CEHS] PRIMARY KEY CLUSTERED ([CETEMPERED]);
ALTER TABLE SYSADM.[CU_SGG_CEIGU] ADD CONSTRAINT [PK_CU_SGG_CEIGU] PRIMARY KEY CLUSTERED ([CEGLAS1], [COATPOSGLAS1], [CEGLAS2], [COATPOSGLAS2], [CEGLAS3], [COATPOSGLAS3], [SZR1], [GAS1], [SEALANT1], [FRAME1], [GORBAR1], [SZR2], [GAS2], [SEALANT2], [FRAME2], [GORBAR2]);
ALTER TABLE SYSADM.[CU_SGG_CELAM] ADD CONSTRAINT [PK_CU_SGG_CELAM] PRIMARY KEY CLUSTERED ([ELEMENT1], [ELEMENT2], [ELEMENT3], [ELEMENT4], [ELEMENT5], [ELEMENT6], [ELEMENT7], [ELEMENT8], [ELEMENT9], [ELEMENT10]);
ALTER TABLE SYSADM.[CU_SGG_CEPRODLIST] ADD CONSTRAINT [PK_CU_SGG_CEPRODLIST] PRIMARY KEY CLUSTERED ([CECODE]);
ALTER TABLE SYSADM.[CU_SGG_CREDITLIMIT] ADD CONSTRAINT [PK_CU_SGG_CREDITLIMIT] PRIMARY KEY CLUSTERED ([CUST_ID], [EULER_ID]);
ALTER TABLE SYSADM.[CU_SGG_DELIVERY] ADD CONSTRAINT [PK_CU_SGG_DELIVERY] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[CU_SGG_KU_WGR_GRU] ADD CONSTRAINT [PK_CU_SGG_KU_WGR_GRU] PRIMARY KEY CLUSTERED ([ID], [WGR]);
ALTER TABLE SYSADM.[CU_SGG_LI_WGR_GRU] ADD CONSTRAINT [PK_CU_SGG_LI_WGR_GRU] PRIMARY KEY CLUSTERED ([ID], [WGR]);
ALTER TABLE SYSADM.[DR_AUFTR_FORM] ADD CONSTRAINT [PK_DR_AUFTR_FORM] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[DR_DRUCK] ADD CONSTRAINT [PK_DR_DRUCK] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[DR_DRUCKPUNKTE] ADD CONSTRAINT [PK_DR_DRUCKPUNKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[DR_KUNDEN_LENR] ADD CONSTRAINT [PK_DR_KUNDEN_LENR] PRIMARY KEY CLUSTERED ([KUNDE], [LENR]);
ALTER TABLE SYSADM.[DR_REPORTE] ADD CONSTRAINT [PK_DR_REPORTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FAN_ST_TEMP] ADD CONSTRAINT [PK_FAN_ST_TEMP] PRIMARY KEY CLUSTERED ([ZUSATZ], [VERTRETER], [KUNDE], [DATUM], [AUFBAU]);
ALTER TABLE SYSADM.[FAN_ST_TEMP_F] ADD CONSTRAINT [PK_FAN_ST_TEMP_F] PRIMARY KEY CLUSTERED ([ZUSATZ], [DATUM], [AUFBAU]);
ALTER TABLE SYSADM.[FAN_STATI] ADD CONSTRAINT [PK_FAN_STATI] PRIMARY KEY CLUSTERED ([STAT_TYP], [VERTRETER], [KUNDE], [DATUM], [ZUSATZ], [AUFBAU]);
ALTER TABLE SYSADM.[FS_ATT_KOPF] ADD CONSTRAINT [PK_FS_ATT_KOPF] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[FS_ATT_POS] ADD CONSTRAINT [PK_FS_ATT_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[FS_BEARB] ADD CONSTRAINT [PK_FS_BEARB] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [STKL_NR]);
ALTER TABLE SYSADM.[FS_BESTELLINFO] ADD CONSTRAINT [PK_FS_BESTELLINFO] PRIMARY KEY CLUSTERED ([BESTELLUNG], [POSITION], [AUFTRAG], [A_POSITION], [A_BOMID]);
ALTER TABLE SYSADM.[FS_DOWNPAYMENT] ADD CONSTRAINT [PK_FS_DOWNPAYMENT] PRIMARY KEY CLUSTERED ([ID], [PAYMENT_NO]);
ALTER TABLE SYSADM.[FS_KF_TEXT] ADD CONSTRAINT [PK_FS_KF_TEXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[FS_KOPF] ADD CONSTRAINT [PK_FS_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FS_KU_BEARB] ADD CONSTRAINT [PK_FS_KU_BEARB] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_KU_GAS] ADD CONSTRAINT [PK_FS_KU_GAS] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TYP]);
ALTER TABLE SYSADM.[FS_KU_MODELL] ADD CONSTRAINT [PK_FS_KU_MODELL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_KU_PRODUKTE] ADD CONSTRAINT [PK_FS_KU_PRODUKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FS_KU_PRODUKTE_ATTACH] ADD CONSTRAINT [PK_FS_KU_PROD_ATT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[FS_KU_SPROSSEN] ADD CONSTRAINT [PK_FS_KU_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_KU_STRUKTE] ADD CONSTRAINT [PK_FS_KU_STRUKTE] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_ID]);
ALTER TABLE SYSADM.[FS_KU_STRUKTV] ADD CONSTRAINT [PK_FS_KU_STRUKTV] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_ID]);
ALTER TABLE SYSADM.[FS_KU_STUKL] ADD CONSTRAINT [PK_FS_KU_STUKL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_KU_SZR] ADD CONSTRAINT [PK_FS_KU_SZR] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TYP], [FREMD_DICKE]);
ALTER TABLE SYSADM.[FS_KU_TOUREN] ADD CONSTRAINT [PK_FS_KU_TOUREN] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TOUR]);
ALTER TABLE SYSADM.[FS_LI_BEARB] ADD CONSTRAINT [PK_FS_LI_BEARB] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_LI_GAS] ADD CONSTRAINT [PK_FS_LI_GAS] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TYP]);
ALTER TABLE SYSADM.[FS_LI_MODELL] ADD CONSTRAINT [PK_FS_LI_MODELL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_LI_PRODUKTE] ADD CONSTRAINT [PK_FS_LI_PRODUKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FS_LI_PRODUKTE_ATTACH] ADD CONSTRAINT [PK_FS_LI_PROD_ATT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[FS_LI_SPROSSEN] ADD CONSTRAINT [PK_FS_LI_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_LI_STRUKTE] ADD CONSTRAINT [PK_FS_LI_STRUKTE] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_ID]);
ALTER TABLE SYSADM.[FS_LI_STRUKTV] ADD CONSTRAINT [PK_FS_LI_STRUKTV] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_ID]);
ALTER TABLE SYSADM.[FS_LI_STUKL] ADD CONSTRAINT [PK_FS_LI_STUKL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[FS_LI_SZR] ADD CONSTRAINT [PK_FS_LI_SZR] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TYP], [FREMD_DICKE]);
ALTER TABLE SYSADM.[FS_LI_TOUREN] ADD CONSTRAINT [PK_FS_LI_TOUREN] PRIMARY KEY CLUSTERED ([KDNR], [FREMD_TOUR]);
ALTER TABLE SYSADM.[FS_MODELL] ADD CONSTRAINT [PK_FS_MODELL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [STKL_NR]);
ALTER TABLE SYSADM.[FS_POOL] ADD CONSTRAINT [PK_FS_POOL] PRIMARY KEY CLUSTERED ([ID], [SEQUENZ_NR]);
ALTER TABLE SYSADM.[FS_POOL_KOPF] ADD CONSTRAINT [PK_FS_POOL_KOPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FS_POOL_LOG] ADD CONSTRAINT [PK_FS_POOL_LOG_ID] PRIMARY KEY CLUSTERED ([ID], [SEQUENZ_NR]);
ALTER TABLE SYSADM.[FS_POS] ADD CONSTRAINT [PK_FS_POS] PRIMARY KEY CLUSTERED ([ID], [POS_NR]);
ALTER TABLE SYSADM.[FS_SPROSSEN] ADD CONSTRAINT [PK_FS_SPROSSEN] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [STKL_NR]);
ALTER TABLE SYSADM.[FS_STATUS] ADD CONSTRAINT [PK_FS_STATUS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[FS_STKL] ADD CONSTRAINT [PK_FS_STKL] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [STKL_NR]);
ALTER TABLE SYSADM.[FS_TEXT] ADD CONSTRAINT [PK_FS_TEXT] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [LFD_NR]);
ALTER TABLE SYSADM.[FS_TRANSFER] ADD CONSTRAINT [PK_FS_TRANSFER] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_ADIENST] ADD CONSTRAINT [PK_KA_ADIENST] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_ANREDEN] ADD CONSTRAINT [PK_KA_ANREDEN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_ARBGANG] ADD CONSTRAINT [PK_KA_ARBGANG] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_AREA] ADD CONSTRAINT [PK_KA_AREA] PRIMARY KEY CLUSTERED ([AREA]);
ALTER TABLE SYSADM.[KA_ATT_REMARK] ADD CONSTRAINT [PK_KA_ATT_REMARK] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_ATT_TYPE] ADD CONSTRAINT [PK_KA_ATT_TYPE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_AUFBAU] ADD CONSTRAINT [PK_KA_AUFBAU] PRIMARY KEY CLUSTERED ([DOKTYP]);
ALTER TABLE SYSADM.[KA_AUFBAU_LENR] ADD CONSTRAINT [PK_KA_AUFBAU_LENR] PRIMARY KEY CLUSTERED ([PRODUKTART], [PRODUKTGRP], [ABW_PRODUKTART]);
ALTER TABLE SYSADM.[KA_AUTO_PROCESS] ADD CONSTRAINT [PK_KA_AUTO_PROCESS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_AVBEREICH] ADD CONSTRAINT [PK_KA_AVBEREICH] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_BANKEN] ADD CONSTRAINT [PK_KA_BANKEN] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[KA_BESCHAFFUNGSART] ADD CONSTRAINT [PK_KA_BESCHAFFUNGSART] PRIMARY KEY CLUSTERED ([TYP], [ID]);
ALTER TABLE SYSADM.[KA_BESCHICHTART] ADD CONSTRAINT [PK_KA_BESCHICHTART] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_BESCHICHTSEITE] ADD CONSTRAINT [PK_KA_BESCHICHTSEITE] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_BONITAET] ADD CONSTRAINT [PK_KA_BONITAET] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_BRANCHEN] ADD CONSTRAINT [PK_KA_BRANCHEN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_BUCH_PERIODE] ADD CONSTRAINT [PK_KA_BUCH_PERIODE] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[KA_CALENDAR_STATE] ADD CONSTRAINT [PK_KA_CALENDAR_STATE] PRIMARY KEY CLUSTERED ([PROVINZ], [LAND_KZ], [VON], [BIS], [JAHR], [MODUS]);
ALTER TABLE SYSADM.[KA_CEKAL_CLASS] ADD CONSTRAINT [PK_KA_CEKAL_CLASS] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_CRYSTAL] ADD CONSTRAINT [PK_KA_CRYSTAL] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_CRYSTAL_GRP] ADD CONSTRAINT [PK_KA_CRYSTAL_GRP] PRIMARY KEY CLUSTERED ([GRUPPE_ID], [REPORT_ID]);
ALTER TABLE SYSADM.[KA_CRYSTAL_MIT] ADD CONSTRAINT [PK_KA_CRYSTAL_MIT] PRIMARY KEY CLUSTERED ([MITARB_ID], [REPORT_ID]);
ALTER TABLE SYSADM.[KA_CTRL_FORMEL] ADD CONSTRAINT [PK_KA_CTRL_FORMEL] PRIMARY KEY CLUSTERED ([CTRL_NAME], [EVENT], [QBE_MODE]);
ALTER TABLE SYSADM.[KA_CUSTOMIZINGDATA] ADD CONSTRAINT [PK_KA_CUSTOMIZINGDATA] PRIMARY KEY CLUSTERED ([CUSTOMIZING_TYPE], [BORDER1], [BORDER2], [BORDER3]);
ALTER TABLE SYSADM.[KA_DOK_TYP] ADD CONSTRAINT [PK_KA_DOK_TYP] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_EBENEN] ADD CONSTRAINT [PK_KA_EBENEN] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_ERLKO] ADD CONSTRAINT [PK_KA_ERLKO] PRIMARY KEY CLUSTERED ([ID], [ID1], [ID2], [ID3], [ID4], [ID5], [GESCHART]);
ALTER TABLE SYSADM.[KA_ERLKO_PROD] ADD CONSTRAINT [PK_KA_ERLKO_PROD] PRIMARY KEY CLUSTERED ([PROD_ID], [ID1], [ID2], [ID3], [ID4], [ID5], [GESCHART]);
ALTER TABLE SYSADM.[KA_FAHRER] ADD CONSTRAINT [PK_KA_FAHRER] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_FAMILY] ADD CONSTRAINT [PK_KA_FAMILY] PRIMARY KEY CLUSTERED ([PRODUKT]);
ALTER TABLE SYSADM.[KA_FARBE] ADD CONSTRAINT [PK_KA_FARBE] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_FAVORITEN] ADD CONSTRAINT [PK_KA_FAVORITEN] PRIMARY KEY CLUSTERED ([ID], [USER_ID]);
ALTER TABLE SYSADM.[KA_FB_TYP] ADD CONSTRAINT [PK_KA_FB_TYP] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FERTSCHLS] ADD CONSTRAINT [PK_KA_FERTSCHLS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FIRMA] ADD CONSTRAINT [PK_KA_FIRMA] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FIRMA_BANK] ADD CONSTRAINT [PK_KA_FIRMA_BANK] PRIMARY KEY CLUSTERED ([MANDANT], [BANK_ID], [KTN]);
ALTER TABLE SYSADM.[KA_FIRMA_DIGITALE_SIGNATUR] ADD CONSTRAINT [PK_KA_FIRMA_DIGITALE_SIGNATUR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FIRMA_KAPA] ADD CONSTRAINT [PK_KA_FIRMA_KAPA] PRIMARY KEY CLUSTERED ([MANDANT], [TAG], [PRDKTART]);
ALTER TABLE SYSADM.[KA_FIRMA_LIEFERADR] ADD CONSTRAINT [PK_KA_FIRMA_LIEFERADR] PRIMARY KEY CLUSTERED ([MANDANT_ID], [ID]);
ALTER TABLE SYSADM.[KA_FIRMA_MITARB] ADD CONSTRAINT [PK_KA_FIRMA_MITARB] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FIRMA_MITARB_FRM] ADD CONSTRAINT [PK_KA_FIRMA_MITARB_FRM] PRIMARY KEY CLUSTERED ([MITARB_ID], [FRM_ID]);
ALTER TABLE SYSADM.[KA_FIRMA_MITARB_P] ADD CONSTRAINT [PK_KA_FIRMA_MITARB_P] PRIMARY KEY CLUSTERED ([MITARB_ID], [ID], [TYP]);
ALTER TABLE SYSADM.[KA_FIRMA_MITARB_TP] ADD CONSTRAINT [PK_KA_FIRMA_MITARB_TP] PRIMARY KEY CLUSTERED ([MITARB_ID], [TABLE_ID]);
ALTER TABLE SYSADM.[KA_FIRMA_PARAMS] ADD CONSTRAINT [PK_KA_FIRMA_PARAMS] PRIMARY KEY CLUSTERED ([ID], [PARAM]);
ALTER TABLE SYSADM.[KA_FIRMA_RND] ADD CONSTRAINT [PK_KA_FIRMA_RND] PRIMARY KEY CLUSTERED ([MANDANT], [RND_PUNKT], [PROD_ART], [PROD_GRUPPE], [PREIS_EINHEIT], [KZ_NETTO_PREISE]);
ALTER TABLE SYSADM.[KA_FORMEL] ADD CONSTRAINT [PK_KA_FORMEL] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FORMEL_PUNKT] ADD CONSTRAINT [PK_KA_FORMEL_PUNKT] PRIMARY KEY CLUSTERED ([PUNKT]);
ALTER TABLE SYSADM.[KA_FORMULARE] ADD CONSTRAINT [PK_KA_FORMULARE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_FUNKTION] ADD CONSTRAINT [PK_KA_FUNKTION] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_GESCHART] ADD CONSTRAINT [PK_KA_GESCHART] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_GRENZTYP] ADD CONSTRAINT [PK_KA_GRENZTYP] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_ISO_LANG] ADD CONSTRAINT [PK_KA_ISO_LANG] PRIMARY KEY CLUSTERED ([CODE]);
ALTER TABLE SYSADM.[KA_KALENDER] ADD CONSTRAINT [PK_KA_KALENDER] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_KATEGORIE] ADD CONSTRAINT [PK_KA_KATEGORIE] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[KA_KLASSI_DEF] ADD CONSTRAINT [PK_KA_KLASSI_DEF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_KLASSI_STRING] ADD CONSTRAINT [PK_KA_KLASSI_STRING] PRIMARY KEY CLUSTERED ([K_ID], [K_WERT]);
ALTER TABLE SYSADM.[KA_KOSTENART] ADD CONSTRAINT [PK_KA_KOSTENART] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_KOSTENSTELLE] ADD CONSTRAINT [PK_KA_KOSTENSTELLE] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_KUNDENGRPN] ADD CONSTRAINT [PK_KA_KUNDENGRPN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAG_LEVEL1] ADD CONSTRAINT [PK_KA_LAG_LEVEL1] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAG_LEVEL2] ADD CONSTRAINT [PK_KA_LAG_LEVEL2] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAG_LEVEL3] ADD CONSTRAINT [PK_KA_LAG_LEVEL3] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAG_LEVEL4] ADD CONSTRAINT [PK_KA_LAG_LEVEL4] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAGBEWTYP] ADD CONSTRAINT [PK_KA_LAGBEWTYP] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[KA_LAGER_DEF] ADD CONSTRAINT [PK_KA_LAGER_DEF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_LAGER_TEXTE] ADD CONSTRAINT [PK_KA_LAGER_TEXTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_LAGERORTE] ADD CONSTRAINT [PK_KA_LAGERORTE] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LAND] ADD CONSTRAINT [PK_KA_LAND] PRIMARY KEY CLUSTERED ([LAND_KZ]);
ALTER TABLE SYSADM.[KA_LAND_KENNZ] ADD CONSTRAINT [PK_KA_LAND_KENNZ] PRIMARY KEY CLUSTERED ([KENNZ]);
ALTER TABLE SYSADM.[KA_LIEFERBED] ADD CONSTRAINT [PK_KA_LIEFERBED] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LIEFGRPN] ADD CONSTRAINT [PK_KA_LIEFGRPN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LIEFTERM_GRND] ADD CONSTRAINT [PK_KA_LIEFTERM_GRND] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_LIEFTERM_GRND_SGG] ADD CONSTRAINT [PK_KA_LIEFTERM_GRND_SGG] PRIMARY KEY CLUSTERED ([BEZ], [BEZ2]);
ALTER TABLE SYSADM.[KA_LKW] ADD CONSTRAINT [PK_KA_LKW] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_MAHNTEXT] ADD CONSTRAINT [PK_KA_MAHNTEXT] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_MENU] ADD CONSTRAINT [PK_KA_MENU] PRIMARY KEY CLUSTERED ([ID], [LANGUAGE]);
ALTER TABLE SYSADM.[KA_MITARB_GRUPPE] ADD CONSTRAINT [PK_KA_MITARB_GRUPPE] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_MONATE] ADD CONSTRAINT [PK_KA_MONATE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_MWST] ADD CONSTRAINT [PK_KA_MWST] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_MWST_MATRIX] ADD CONSTRAINT [PK_KA_MWST_MATRIX] PRIMARY KEY CLUSTERED ([STEUER], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[KA_OBJEKT] ADD CONSTRAINT [PK_KA_OBJEKT] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_PARTNERGRPN] ADD CONSTRAINT [PK_KA_PARTNERGRPN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_POOL_GLASARTGRP] ADD CONSTRAINT [PK_KA_POOL_GLASARTGRP] PRIMARY KEY CLUSTERED ([GRP_ID]);
ALTER TABLE SYSADM.[KA_POSTAL] ADD CONSTRAINT [PK_KA_POSTAL] PRIMARY KEY CLUSTERED ([POSTAL_CODE], [TOWN]);
ALTER TABLE SYSADM.[KA_PRINT_DEF] ADD CONSTRAINT [PK_KA_PRINT_DEF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_PRINT_DETAIL] ADD CONSTRAINT [PK_KA_PRINT_DETAIL] PRIMARY KEY CLUSTERED ([ID], [SEQ_NR]);
ALTER TABLE SYSADM.[KA_PRIORITAET] ADD CONSTRAINT [PK_KA_PRIORITAET] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_PRMEEINH] ADD CONSTRAINT [PK_KA_PRMEEINH] PRIMARY KEY CLUSTERED ([BEZ], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_PROD_BRUCH] ADD CONSTRAINT [PK_KA_PROD_BRUCH] PRIMARY KEY CLUSTERED ([STATUS_ID], [KZ_LOGISTIC]);
ALTER TABLE SYSADM.[KA_PRODPOINT_PRODART] ADD CONSTRAINT [PK_KA_PRODPOINT_PRODART] PRIMARY KEY CLUSTERED ([ERF_NR], [PRD_BEZ]);
ALTER TABLE SYSADM.[KA_PRODUKTART] ADD CONSTRAINT [PK_KA_PRODUKTART] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_PRODUKTGRP] ADD CONSTRAINT [PK_KA_PRODUKTGRP] PRIMARY KEY CLUSTERED ([PRDKTART_ID], [PRDKTGRP_ID]);
ALTER TABLE SYSADM.[KA_PROG_PUNKTE] ADD CONSTRAINT [PK_KA_PROG_PUNKTE] PRIMARY KEY CLUSTERED ([PROG_ID], [PROG_PKT]);
ALTER TABLE SYSADM.[KA_PROGRAMME] ADD CONSTRAINT [PK_KA_PROGRAMME] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_PROJEKT] ADD CONSTRAINT [PK_KA_PROJEKT] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_PROVINZ] ADD CONSTRAINT [PK_KA_PROVINZ] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_RECHTE_GRUPPE] ADD CONSTRAINT [PK_KA_RECHTE_GRUPPE] PRIMARY KEY CLUSTERED ([PROG_ID], [GRUPPE]);
ALTER TABLE SYSADM.[KA_RECHTE_MITARB] ADD CONSTRAINT [PK_KA_RECHTE_MITARB] PRIMARY KEY CLUSTERED ([PROG_ID], [MITARB]);
ALTER TABLE SYSADM.[KA_REKLA_GRND] ADD CONSTRAINT [PK_KA_REKLA_GRND] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_REKLA_ORT] ADD CONSTRAINT [PK_KA_REKLA_ORT] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_REP_KREISE] ADD CONSTRAINT [PK_KA_REP_KREISE] PRIMARY KEY CLUSTERED ([TABELLE]);
ALTER TABLE SYSADM.[KA_RND_PUNKTE] ADD CONSTRAINT [PK_KA_RND_PUNKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_RND_TAB] ADD CONSTRAINT [PK_KA_RND_TAB] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_RND_WERTE] ADD CONSTRAINT [PK_KA_RND_WERTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_SORT_SCHLS] ADD CONSTRAINT [PK_KA_SORT_SCHLS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_SPERRKZ] ADD CONSTRAINT [PK_KA_SPERRKZ] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_SPRACHEN] ADD CONSTRAINT [PK_KA_SPRACHEN] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_SPROSSTYP] ADD CONSTRAINT [PK_KA_SPROSSTYP] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_STATUS] ADD CONSTRAINT [PK_KA_STATUS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_STATUS_GRUPPE] ADD CONSTRAINT [PK_KA_STATUS_GRUPPE] PRIMARY KEY CLUSTERED ([GRUPPE], [DOK_TYP]);
ALTER TABLE SYSADM.[KA_STATUS_MITARB] ADD CONSTRAINT [PK_KA_STATUS_MITARB] PRIMARY KEY CLUSTERED ([MITARB], [DOK_TYP]);
ALTER TABLE SYSADM.[KA_STATUS_ZUORD] ADD CONSTRAINT [PK_KA_STATUS_ZUORD] PRIMARY KEY CLUSTERED ([PUNKT_ID]);
ALTER TABLE SYSADM.[KA_STATUSPUNKTE] ADD CONSTRAINT [PK_KA_STATUSPUNKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_STRUKTVERLAUF] ADD CONSTRAINT [PK_KA_STRUKTVERLAUF] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_SUBFAMILY] ADD CONSTRAINT [PK_KA_SUBFAMILY] PRIMARY KEY CLUSTERED ([PRODUKT]);
ALTER TABLE SYSADM.[KA_SYSTEXTE] ADD CONSTRAINT [PK_KA_SYSTEXTE] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_TAGE] ADD CONSTRAINT [PK_KA_TAGE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX] ADD CONSTRAINT [PK_KA_TAX] PRIMARY KEY CLUSTERED ([PLZ_ID], [STEUER_ID]);
ALTER TABLE SYSADM.[KA_TAX_BUSINESS] ADD CONSTRAINT [PK_KA_TAX_BUSINESS] PRIMARY KEY CLUSTERED ([TYPE], [ID]);
ALTER TABLE SYSADM.[KA_TAX_BUSINESS_LINK] ADD CONSTRAINT [PK_KA_TAX_BUSINESS_LINK] PRIMARY KEY CLUSTERED ([TYPE], [ID], [TARGET_ID]);
ALTER TABLE SYSADM.[KA_TAX_CFOP] ADD CONSTRAINT [PK_KA_TAX_CFOP] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_CST] ADD CONSTRAINT [PK_KA_TAX_CST] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_GENERATOR] ADD CONSTRAINT [PK_KA_TAX_GENERATOR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_ICMS_MODE] ADD CONSTRAINT [PK_KA_TAX_ICMS_MODE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_OPERATION] ADD CONSTRAINT [PK_KA_TAX_OPERATION] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_PRODUCT_TYPE] ADD CONSTRAINT [PK_KA_TAX_PRODUCT_TYPE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TAX_REDUCTION] ADD CONSTRAINT [PK_KA_TAX_REDUCTION] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TEXT_KZ] ADD CONSTRAINT [PK_KA_TEXT_KZ] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_TEXTE] ADD CONSTRAINT [PK_KA_TEXTE] PRIMARY KEY CLUSTERED ([ID], [SPRACH_ID]);
ALTER TABLE SYSADM.[KA_TIPI] ADD CONSTRAINT [PK_KA_TIPI] PRIMARY KEY CLUSTERED ([NCM]);
ALTER TABLE SYSADM.[KA_TIPI_IVA] ADD CONSTRAINT [PK_KA_TIPI_IVA] PRIMARY KEY CLUSTERED ([NCM], [PROVINZ]);
ALTER TABLE SYSADM.[KA_TIPI_PGR] ADD CONSTRAINT [PK_KA_TIPI_PGR] PRIMARY KEY CLUSTERED ([WGR]);
ALTER TABLE SYSADM.[KA_TOUREN] ADD CONSTRAINT [PK_KA_TOUREN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_USER_MENU] ADD CONSTRAINT [PK_KA_USER_MENU] PRIMARY KEY CLUSTERED ([PROG_ID], [USER_ID]);
ALTER TABLE SYSADM.[KA_VERPACKUNGEN] ADD CONSTRAINT [PK_KA_VERPACKUNGEN] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_VORGANG] ADD CONSTRAINT [PK_KA_VORGANG] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_WAEHRUNGEN] ADD CONSTRAINT [PK_KA_WAEHRUNGEN] PRIMARY KEY CLUSTERED ([WAEHRUNG]);
ALTER TABLE SYSADM.[KA_WAEHRUNGEN_KENNZ] ADD CONSTRAINT [PK_KA_WAEHRUNGEN_KENNZ] PRIMARY KEY CLUSTERED ([KENNZ]);
ALTER TABLE SYSADM.[KA_WERTBER] ADD CONSTRAINT [PK_KA_WERTBER] PRIMARY KEY CLUSTERED ([KATEGORIE]);
ALTER TABLE SYSADM.[KA_WGR] ADD CONSTRAINT [PK_KA_WGR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_WGR_GRPBEZ] ADD CONSTRAINT [PK_KA_WGR_GRPBEZ] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_WGR_GRUPPEN] ADD CONSTRAINT [PK_KA_WGR_GRUPPEN] PRIMARY KEY CLUSTERED ([GRP_ID], [WGR_ID]);
ALTER TABLE SYSADM.[KA_WGRKMB] ADD CONSTRAINT [PK_KA_WGRKMB] PRIMARY KEY CLUSTERED ([ID], [ID1]);
ALTER TABLE SYSADM.[KA_WORKFLOW] ADD CONSTRAINT [PK_KA_WORKFLOW] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KA_XOPT_TISCH] ADD CONSTRAINT [PK_KA_XOPT_TISCH] PRIMARY KEY CLUSTERED ([TISCH_ID]);
ALTER TABLE SYSADM.[KA_ZAHLBED] ADD CONSTRAINT [PK_KA_ZAHLBED] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_ZAHLWEG] ADD CONSTRAINT [PK_KA_ZAHLWEG] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KA_ZOLLNR_PROD] ADD CONSTRAINT [PK_KA_ZOLLNR_PROD] PRIMARY KEY CLUSTERED ([PROD_ID], [FW_CODE]);
ALTER TABLE SYSADM.[KA_ZOLLNR_WGR] ADD CONSTRAINT [PK_KA_ZOLLNR_WGR] PRIMARY KEY CLUSTERED ([WGR], [FW_CODE]);
ALTER TABLE SYSADM.[KA_ZOLLTOUR] ADD CONSTRAINT [PK_KA_ZOLLTOUR] PRIMARY KEY CLUSTERED ([BEZ]);
ALTER TABLE SYSADM.[KAMA_KLASSI_DEF] ADD CONSTRAINT [PK_KAMA_KLASSI_DEF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KAMA_KLASSI_STRING] ADD CONSTRAINT [PK_KAMA_KLASSI_STRING] PRIMARY KEY CLUSTERED ([K_ID], [K_WERT]);
ALTER TABLE SYSADM.[KU_ATTACH] ADD CONSTRAINT [PK_KU_ATTACH] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[KU_DOCK] ADD CONSTRAINT [PK_KU_DOCK] PRIMARY KEY CLUSTERED ([DOCK_ID], [ID]);
ALTER TABLE SYSADM.[KU_KALENDER] ADD CONSTRAINT [PK_KU_KALENDER] PRIMARY KEY CLUSTERED ([ID], [VON], [BIS], [JAHR], [MODUS]);
ALTER TABLE SYSADM.[KU_KG_DECKUNG] ADD CONSTRAINT [PK_KU_KG_DECKUNG] PRIMARY KEY CLUSTERED ([GRUPPE], [WGR]);
ALTER TABLE SYSADM.[KU_KG_RND] ADD CONSTRAINT [PK_KU_KG_RND] PRIMARY KEY CLUSTERED ([KUNDEN_GRP], [RND_PUNKT], [PROD_ART], [PROD_GRUPPE], [PREIS_EINHEIT], [KZ_NETTO_PREISE]);
ALTER TABLE SYSADM.[KU_KLASSI_WERTE] ADD CONSTRAINT [PK_KU_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([ID], [K_ID]);
ALTER TABLE SYSADM.[KU_KOMM] ADD CONSTRAINT [PK_KU_KOMM] PRIMARY KEY CLUSTERED ([KOMM_ID], [ID]);
ALTER TABLE SYSADM.[KU_KREDITLIMIT] ADD CONSTRAINT [PK_KU_KREDITLIMIT] PRIMARY KEY CLUSTERED ([ID], [DATUM_ERSTELLUNG]);
ALTER TABLE SYSADM.[KU_KU_DECKUNG] ADD CONSTRAINT [PK_KU_KU_DECKUNG] PRIMARY KEY CLUSTERED ([ID], [WGR]);
ALTER TABLE SYSADM.[KU_KU_OBJEKTE] ADD CONSTRAINT [PK_KU_KU_OBJEKTE] PRIMARY KEY CLUSTERED ([KUNDEN_NR], [OBJEKT_ID]);
ALTER TABLE SYSADM.[KU_KU_RND] ADD CONSTRAINT [PK_KU_KU_RND] PRIMARY KEY CLUSTERED ([KUNDEN_NR], [RND_PUNKT], [PROD_ART], [PROD_GRUPPE], [PREIS_EINHEIT], [KZ_NETTO_PREISE]);
ALTER TABLE SYSADM.[KU_KUNDEN] ADD CONSTRAINT [PK_KU_KUNDEN] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KU_KUNDEN_FORM] ADD CONSTRAINT [PK_KU_KUNDEN_FORM] PRIMARY KEY CLUSTERED ([KUNDEN_ID], [ID]);
ALTER TABLE SYSADM.[KU_KUNDEN_KAPA] ADD CONSTRAINT [PK_KU_KUNDEN_KAPA] PRIMARY KEY CLUSTERED ([ID], [TAG], [PRDKTART]);
ALTER TABLE SYSADM.[KU_KUNDEN_MIN] ADD CONSTRAINT [PK_KU_KUNDEN_MIN] PRIMARY KEY CLUSTERED ([ID], [AV_BEREICH]);
ALTER TABLE SYSADM.[KU_KUNDEN_TXT] ADD CONSTRAINT [PK_KU_KUNDEN_TXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[KU_KUNDEN_VOR] ADD CONSTRAINT [PK_KU_KUNDEN_VOR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[KU_LGESTELLE] ADD CONSTRAINT [PK_KU_LGESTELLE] PRIMARY KEY CLUSTERED ([KUNDE], [ZUSCHLARTIKEL]);
ALTER TABLE SYSADM.[KU_LIEF_DAUER] ADD CONSTRAINT [PK_KU_LIEF_DAUER] PRIMARY KEY CLUSTERED ([PARTNER_ID], [TOUR]);
ALTER TABLE SYSADM.[KU_LIMITREQUEST] ADD CONSTRAINT [PK_KU_LIMITREQUEST] PRIMARY KEY CLUSTERED ([ID], [REQUEST]);
ALTER TABLE SYSADM.[KU_LOGO] ADD CONSTRAINT [PK_KU_LOGO] PRIMARY KEY CLUSTERED ([ID], [WGR], [BREITE], [HOEHE], [MOD_NR], [TYP]);
ALTER TABLE SYSADM.[KU_MITARB] ADD CONSTRAINT [PK_KU_MITARB] PRIMARY KEY CLUSTERED ([ID], [PARENT_ID]);
ALTER TABLE SYSADM.[KU_OBJ_MAX_PROD] ADD CONSTRAINT [PK_KU_OBJ_MAX_PROD] PRIMARY KEY CLUSTERED ([PARTNER_ID], [OBJEKT_ID], [PROD_ID]);
ALTER TABLE SYSADM.[KU_OBJ_MAX_WGR] ADD CONSTRAINT [PK_KU_OBJ_MAX_WGR] PRIMARY KEY CLUSTERED ([PARTNER_ID], [OBJEKT_ID], [WGR_ID]);
ALTER TABLE SYSADM.[KU_OBJ_SUM] ADD CONSTRAINT [PK_KU_OBJ_SUM] PRIMARY KEY CLUSTERED ([DOC_ID]);
ALTER TABLE SYSADM.[KU_OBJ_SUM_PROD] ADD CONSTRAINT [PK_KU_OBJ_SUM_PROD] PRIMARY KEY CLUSTERED ([DOC_ID], [PROD_ID]);
ALTER TABLE SYSADM.[KU_OBJ_SUM_WGR] ADD CONSTRAINT [PK_KU_OBJ_SUM_WGR] PRIMARY KEY CLUSTERED ([DOC_ID], [WGR_ID]);
ALTER TABLE SYSADM.[KUMA_KLASSI_WERTE] ADD CONSTRAINT [PK_KUMA_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([MA_ID], [ID], [K_ID]);
ALTER TABLE SYSADM.[LG_ASN_DETAIL] ADD CONSTRAINT [PK_LG_ASN_DETAIL] PRIMARY KEY CLUSTERED ([HEADER_NR], [DETAIL_NR]);
ALTER TABLE SYSADM.[LG_ASN_ERROR] ADD CONSTRAINT [PK_LG_ASN_ERROR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[LG_ASN_HEADER] ADD CONSTRAINT [PK_LG_ASN_HEADER] PRIMARY KEY CLUSTERED ([HEADER_NR]);
ALTER TABLE SYSADM.[LG_ASN_MISC] ADD CONSTRAINT [PK_LG_ASN_MISC] PRIMARY KEY CLUSTERED ([HEADER_NR], [MISC_NR]);
ALTER TABLE SYSADM.[LG_BEARB] ADD CONSTRAINT [PK_LG_BEARB] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[LG_KATEGORIE] ADD CONSTRAINT [PK_LG_KATEGORIE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[LG_LAGERORTE] ADD CONSTRAINT [PK_LG_LAGERORTE] PRIMARY KEY CLUSTERED ([ID], [IDENT], [LAGERORT_ID]);
ALTER TABLE SYSADM.[LG_LASTBOOKED] ADD CONSTRAINT [PK_LG_LASTBOOKED] PRIMARY KEY CLUSTERED ([STOCK_ID], [KEY1], [KEY2], [KEY3]);
ALTER TABLE SYSADM.[LG_MODELL] ADD CONSTRAINT [PK_LG_MODELL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[LG_PRODUKTE] ADD CONSTRAINT [PK_LG_PRODUKTE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[LG_SNAPSHOT] ADD CONSTRAINT [PK_LG_SNAPSHOT] PRIMARY KEY CLUSTERED ([ID], [IDENT], [LAGERORT_ID]);
ALTER TABLE SYSADM.[LG_STUKL] ADD CONSTRAINT [PK_LG_STUKL] PRIMARY KEY CLUSTERED ([ID], [BOM_ID]);
ALTER TABLE SYSADM.[LG_TAG] ADD CONSTRAINT [PK_LG_TAG] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[LI_ATTACH] ADD CONSTRAINT [PK_LI_ATTACH] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[LI_KALENDER] ADD CONSTRAINT [PK_LI_KALENDER] PRIMARY KEY CLUSTERED ([ID], [VON], [BIS], [JAHR], [MODUS]);
ALTER TABLE SYSADM.[LI_KLASSI_WERTE] ADD CONSTRAINT [PK_LI_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([ID], [K_ID]);
ALTER TABLE SYSADM.[LI_LG_RND] ADD CONSTRAINT [PK_LI_LG_RND] PRIMARY KEY CLUSTERED ([LIEFERANTEN_GRP], [RND_PUNKT], [PROD_ART], [PROD_GRUPPE], [PREIS_EINHEIT], [KZ_NETTO_PREISE]);
ALTER TABLE SYSADM.[LI_LI_OBJEKTE] ADD CONSTRAINT [PK_LI_LI_OBJEKTE] PRIMARY KEY CLUSTERED ([LIEFERANT], [OBJEKT_ID]);
ALTER TABLE SYSADM.[LI_LI_RND] ADD CONSTRAINT [PK_LI_LI_RND] PRIMARY KEY CLUSTERED ([LIEFERANTEN_NR], [RND_PUNKT], [PROD_ART], [PROD_GRUPPE], [PREIS_EINHEIT], [KZ_NETTO_PREISE]);
ALTER TABLE SYSADM.[LI_LIEF_DAUER] ADD CONSTRAINT [PK_LI_LIEF_DAUER] PRIMARY KEY CLUSTERED ([PARTNER_ID], [TOUR]);
ALTER TABLE SYSADM.[LI_LIEF_FORM] ADD CONSTRAINT [PK_LI_LIEF_FORM] PRIMARY KEY CLUSTERED ([LIEFERANTEN_ID], [ID]);
ALTER TABLE SYSADM.[LI_LIEFERANTEN] ADD CONSTRAINT [PK_LI_LIEFERANTEN] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[LI_LIEFERANTEN_KAPA] ADD CONSTRAINT [PK_LI_LIEFERANTEN_KAPA] PRIMARY KEY CLUSTERED ([ID], [TAG], [PRDKTART]);
ALTER TABLE SYSADM.[LI_LIEFERANTEN_TXT] ADD CONSTRAINT [PK_LI_LIEFERANTEN_TXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[LI_LIEFERANTEN_VOR] ADD CONSTRAINT [PK_LI_LIEFERANTEN_VOR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[LI_LIEFKARTEI_PROD] ADD CONSTRAINT [PK_LI_LIEFKARTEI_PROD] PRIMARY KEY CLUSTERED ([PRODUKT], [ID], [DOKTYP], [FARBE], [AV_BEREICH2], [MANDANT2]);
ALTER TABLE SYSADM.[LI_LIEFKARTEI_WGR] ADD CONSTRAINT [PK_LI_LIEFKARTEI_WGR] PRIMARY KEY CLUSTERED ([WGR], [ID], [DOKTYP], [AV_BEREICH2], [MANDANT2]);
ALTER TABLE SYSADM.[LI_MITARB] ADD CONSTRAINT [PK_LI_MITARB] PRIMARY KEY CLUSTERED ([ID], [PARENT_ID]);
ALTER TABLE SYSADM.[LI_OBJ_MAX_PROD] ADD CONSTRAINT [PK_LI_OBJ_MAX_PROD] PRIMARY KEY CLUSTERED ([PARTNER_ID], [OBJEKT_ID], [PROD_ID]);
ALTER TABLE SYSADM.[LI_OBJ_MAX_WGR] ADD CONSTRAINT [PK_LI_OBJ_MAX_WGR] PRIMARY KEY CLUSTERED ([PARTNER_ID], [OBJEKT_ID], [WGR_ID]);
ALTER TABLE SYSADM.[LI_OBJ_SUM] ADD CONSTRAINT [PK_LI_OBJ_SUM] PRIMARY KEY CLUSTERED ([DOC_ID]);
ALTER TABLE SYSADM.[LI_OBJ_SUM_PROD] ADD CONSTRAINT [PK_LI_OBJ_SUM_PROD] PRIMARY KEY CLUSTERED ([DOC_ID], [PROD_ID]);
ALTER TABLE SYSADM.[LI_OBJ_SUM_WGR] ADD CONSTRAINT [PK_LI_OBJ_SUM_WGR] PRIMARY KEY CLUSTERED ([DOC_ID], [WGR_ID]);
ALTER TABLE SYSADM.[LIMA_KLASSI_WERTE] ADD CONSTRAINT [PK_LIMA_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([MA_ID], [ID], [K_ID]);
ALTER TABLE SYSADM.[OP_MAHNSTUFE] ADD CONSTRAINT [PK_OP_MAHNSTUFE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[OP_MAHNUNG] ADD CONSTRAINT [PK_OP_MAHNUNG] PRIMARY KEY CLUSTERED ([NR_RECHNUNG], [OP_ID]);
ALTER TABLE SYSADM.[PA_ATTACH] ADD CONSTRAINT [PK_PA_ATTACH] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[PA_KALENDER] ADD CONSTRAINT [PK_PA_KALENDER] PRIMARY KEY CLUSTERED ([ID], [VON], [BIS], [JAHR], [MODUS]);
ALTER TABLE SYSADM.[PA_KLASSI_WERTE] ADD CONSTRAINT [PK_PA_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([ID], [K_ID]);
ALTER TABLE SYSADM.[PA_MITARB] ADD CONSTRAINT [PK_PA_MITARB] PRIMARY KEY CLUSTERED ([ID], [PARENT_ID]);
ALTER TABLE SYSADM.[PA_PARTNER] ADD CONSTRAINT [PK_PA_PARTNER] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PA_PARTNER_KAPA] ADD CONSTRAINT [PK_PA_PARTNER_KAPA] PRIMARY KEY CLUSTERED ([ID], [TAG], [PRDKTART]);
ALTER TABLE SYSADM.[PA_PARTNER_TXT] ADD CONSTRAINT [PK_PA_PARTNER_TXT] PRIMARY KEY CLUSTERED ([ID], [LFD_NR]);
ALTER TABLE SYSADM.[PA_PARTNER_VOR] ADD CONSTRAINT [PK_PA_PARTNER_VOR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PAMA_KLASSI_WERTE] ADD CONSTRAINT [PK_PAMA_KLASSI_WERTE] PRIMARY KEY CLUSTERED ([MA_ID], [ID], [K_ID]);
ALTER TABLE SYSADM.[PD_AWBAR] ADD CONSTRAINT [PK_PD_AWBAR] PRIMARY KEY CLUSTERED ([ID], [POS_NR], [POINT]);
ALTER TABLE SYSADM.[PD_PROD_DEF] ADD CONSTRAINT [PK_PD_PROD_DEF] PRIMARY KEY CLUSTERED ([PRODUKT], [POINT]);
ALTER TABLE SYSADM.[PD_PROD_POINT] ADD CONSTRAINT [PK_PD_PROD_POINT] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[PD_PRODGRP_DEF] ADD CONSTRAINT [PK_PD_PRODGRP_DEF] PRIMARY KEY CLUSTERED ([PROD_ART], [PROD_GRP], [POINT]);
ALTER TABLE SYSADM.[PD_PROTOKOLL] ADD CONSTRAINT [PK_PD_PROTOKOLL] PRIMARY KEY CLUSTERED ([DATUM], [MITARB_ID]);
ALTER TABLE SYSADM.[PM_KOMM_DOCK] ADD CONSTRAINT [PK_PM_KOMM_DOCK] PRIMARY KEY CLUSTERED ([KU_ID], [KOMM], [DOCK]);
ALTER TABLE SYSADM.[PM_PACK_SORT] ADD CONSTRAINT [PK_PM_PACK_SORT] PRIMARY KEY CLUSTERED ([PACK_ID], [SORT_SCHLS]);
ALTER TABLE SYSADM.[PM_PACKREGEL] ADD CONSTRAINT [PK_PM_PACKREGEL] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[PM_PMG] ADD CONSTRAINT [PK_PM_PMG] PRIMARY KEY CLUSTERED ([LIEF_ID], [LIEF_DATUM], [PACKREGEL], [KOMM], [DOCK], [KZ_MODELL]);
ALTER TABLE SYSADM.[PM_PMG_DEF] ADD CONSTRAINT [PK_PM_PMG_DEF] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[PR_GRU] ADD CONSTRAINT [PK_PR_GRU] PRIMARY KEY CLUSTERED ([TXT]);
ALTER TABLE SYSADM.[PR_ISO_AUFBAU] ADD CONSTRAINT [PK_PR_ISO_AUFBAU] PRIMARY KEY CLUSTERED ([GLAS1], [ABST1], [GAS1], [GLAS2], [ABST2], [GAS2], [GLAS3]);
ALTER TABLE SYSADM.[PR_KG_PREIS] ADD CONSTRAINT [PK_PR_KG_PREIS] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDEN_GRP], [ID], [LISTE_ID], [SCHLS_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[PR_KG_PREISKPF] ADD CONSTRAINT [PK_PR_KG_PREISKPF] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDEN_GRP], [ID], [LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_KU_PREIS] ADD CONSTRAINT [PK_PR_KU_PREIS] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDE], [ID], [LISTE_ID], [SCHLS_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[PR_KU_PREISKPF] ADD CONSTRAINT [PK_PR_KU_PREISKPF] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDE], [ID], [LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_LG_PREIS] ADD CONSTRAINT [PK_PR_LG_PREIS] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANTEN_GRP], [ID], [LISTE_ID], [SCHLS_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[PR_LG_PREISKPF] ADD CONSTRAINT [PK_PR_LG_PREISKPF] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANTEN_GRP], [ID], [LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_LI_PREIS] ADD CONSTRAINT [PK_PR_LI_PREIS] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANT], [ID], [LISTE_ID], [SCHLS_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[PR_LI_PREISKPF] ADD CONSTRAINT [PK_PR_LI_PREISKPF] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANT], [ID], [LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_LISTEN] ADD CONSTRAINT [PK_PR_LISTEN] PRIMARY KEY CLUSTERED ([LISTE_ID]);
ALTER TABLE SYSADM.[PR_MISCH_KMB] ADD CONSTRAINT [PK_PR_MISCH_KMB] PRIMARY KEY CLUSTERED ([ID1], [ID2], [ID3]);
ALTER TABLE SYSADM.[PR_MOD_ZUSCH] ADD CONSTRAINT [PK_PR_MOD_ZUSCH] PRIMARY KEY CLUSTERED ([ID], [LISTE_ID], [SCHLS_ID], [MOD_NR]);
ALTER TABLE SYSADM.[PR_PREIS] ADD CONSTRAINT [PK_PR_PREIS] PRIMARY KEY CLUSTERED ([ID], [LISTE_ID], [SCHLS_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[PR_PREISKPF] ADD CONSTRAINT [PK_PR_PREISKPF] PRIMARY KEY CLUSTERED ([ID], [LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_PRGRU_GLAS] ADD CONSTRAINT [PK_PR_PRGRU_GLAS] PRIMARY KEY CLUSTERED ([LISTE_ID], [SCHLS_ID], [PRDKT_ID]);
ALTER TABLE SYSADM.[PR_SCHLUESSEL] ADD CONSTRAINT [PK_PR_SCHLUESSEL] PRIMARY KEY CLUSTERED ([SCHLS_ID]);
ALTER TABLE SYSADM.[PR_SPROSSENPREISE] ADD CONSTRAINT [PK_PR_SPROSSENPREISE] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PR_VERSICH] ADD CONSTRAINT [PK_PR_VERSICH] PRIMARY KEY CLUSTERED ([LISTE_ID], [SCHLS_ID], [KALKART]);
ALTER TABLE SYSADM.[PR_VK_EK_LISTEN] ADD CONSTRAINT [PK_PR_VK_EK_LISTEN] PRIMARY KEY CLUSTERED ([LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[PR_ZUSCH_ART] ADD CONSTRAINT [PK_PR_ZUSCH_ART] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PR_ZUSCHLAEGE] ADD CONSTRAINT [PK_PR_ZUSCHLAEGE] PRIMARY KEY CLUSTERED ([MANDANT], [KUNDE], [ID], [ID2]);
ALTER TABLE SYSADM.[PROD_BREAKAGE] ADD CONSTRAINT [PK_PROD_BREAKAGE] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_CUTCORRECT] ADD CONSTRAINT [PK_PROD_CUTCORRECT] PRIMARY KEY CLUSTERED ([PRODUKT_BEARB], [AGG], [PRODUKT], [DICKE]);
ALTER TABLE SYSADM.[PROD_FORMULA] ADD CONSTRAINT [PK_PROD_FORMULA] PRIMARY KEY CLUSTERED ([FORMULA]);
ALTER TABLE SYSADM.[PROD_JOB] ADD CONSTRAINT [PK_PROD_JOB] PRIMARY KEY CLUSTERED ([JOBNUMBER]);
ALTER TABLE SYSADM.[PROD_JOBITEM] ADD CONSTRAINT [PK_PROD_JOBITEM] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBITEMEDGESHIFT] ADD CONSTRAINT [PK_PROD_JOBITEMEDGESHIFT] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBITEMFRAME] ADD CONSTRAINT [PK_PROD_JOBITEMFRAME] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBITEMSHAPE] ADD CONSTRAINT [PK_PROD_JOBITEMSHAPE] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [TYPE], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBITEMSHAPEINFO] ADD CONSTRAINT [PK_PROD_JOBITEMSHAPEINFO] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBITEMTEMP] ADD CONSTRAINT [PK_PROD_JOBITEMTEMP] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [VARIANTNUMBER], [JOBNUMBER], [KEYINDEX]);
ALTER TABLE SYSADM.[PROD_JOBPRODSEQ] ADD CONSTRAINT [PK_PROD_JOBPRODSEQ] PRIMARY KEY CLUSTERED ([JOBNUMBER], [LOTTYPE], [PRODUCTIONSEQUENCE]);
ALTER TABLE SYSADM.[PROD_JOBTEMP] ADD CONSTRAINT [PK_PROD_JOBTEMP] PRIMARY KEY CLUSTERED ([JOBNUMBER], [VARIANTNUMBER]);
ALTER TABLE SYSADM.[PROD_LISTSTATE] ADD CONSTRAINT [PK_PROD_LISTSTATE] PRIMARY KEY CLUSTERED ([LISTSTATE]);
ALTER TABLE SYSADM.[PROD_LOGRACK] ADD CONSTRAINT [PK_PROD_LOGRACK] PRIMARY KEY CLUSTERED ([LOGICALRACK]);
ALTER TABLE SYSADM.[PROD_LOGRACKORGA] ADD CONSTRAINT [PK_PROD_LOGRACKORGA] PRIMARY KEY CLUSTERED ([RACKORGA], [LOGICALRACK]);
ALTER TABLE SYSADM.[PROD_MACHINECODE] ADD CONSTRAINT [PK_PROD_MACHINECODE] PRIMARY KEY CLUSTERED ([MACHINECODE], [MACHINETYPE]);
ALTER TABLE SYSADM.[PROD_OPTI_PARAMETERS] ADD CONSTRAINT [PK_PROD_OPTI_PARAMETERS] PRIMARY KEY CLUSTERED ([OPTIMIZATION]);
ALTER TABLE SYSADM.[PROD_OPTI_PLATES] ADD CONSTRAINT [PK_PROD_OPTI_PLATES] PRIMARY KEY CLUSTERED ([OPTIMIZATION], [PLATENR]);
ALTER TABLE SYSADM.[PROD_OPTI_RESIDUE_PLATES] ADD CONSTRAINT [PK_PROD_OPTI_RESIDUE_PLATES] PRIMARY KEY CLUSTERED ([OPTIMIZATION], [PLATENR], [SEQUENCE]);
ALTER TABLE SYSADM.[PROD_OPTI_SEQUENCE] ADD CONSTRAINT [PK_PROD_OPTI_SEQUENCE] PRIMARY KEY CLUSTERED ([OPTIMIZATION], [SEQUENCE]);
ALTER TABLE SYSADM.[PROD_OPTI_STATISTICS] ADD CONSTRAINT [PK_PROD_OPTI_STATISTICS] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PROD_OPTIMIZATION] ADD CONSTRAINT [PK_PROD_OPTIMIZATION] PRIMARY KEY CLUSTERED ([OPTIMIZATION]);
ALTER TABLE SYSADM.[PROD_ORGA] ADD CONSTRAINT [PK_PROD_ORGA] PRIMARY KEY CLUSTERED ([RACKORGA]);
ALTER TABLE SYSADM.[PROD_PARAMETER] ADD CONSTRAINT [PK_PROD_PARAMETER] PRIMARY KEY CLUSTERED ([PARAMETERID]);
ALTER TABLE SYSADM.[PROD_PT_AGGREGATE] ADD CONSTRAINT [PK_PROD_PT_AGGREGATE] PRIMARY KEY CLUSTERED ([AGG_ID]);
ALTER TABLE SYSADM.[PROD_RACKGROUP] ADD CONSTRAINT [PK_PROD_RACKGROUP] PRIMARY KEY CLUSTERED ([RACKGROUP]);
ALTER TABLE SYSADM.[PROD_REPORT_TEXT] ADD CONSTRAINT [PK_PROD_REPORT_TEXT] PRIMARY KEY CLUSTERED ([TEXTNUMBER], [LANGUAGE]);
ALTER TABLE SYSADM.[PROD_RESIDUE_PLATES] ADD CONSTRAINT [PK_PROD_RESIDUE_PLATES] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[PROD_SHAPETRIM] ADD CONSTRAINT [PK_PROD_SHAPETRIM] PRIMARY KEY CLUSTERED ([MODKATALOG], [MODNR]);
ALTER TABLE SYSADM.[PROD_SORTKEY] ADD CONSTRAINT [PK_PROD_SORTKEY] PRIMARY KEY CLUSTERED ([SORTKEY]);
ALTER TABLE SYSADM.[PROD_SORTKEYFORMULA] ADD CONSTRAINT [PK_PROD_SORTKEYFORMULA] PRIMARY KEY CLUSTERED ([SORTKEY], [SEQUENCENR]);
ALTER TABLE SYSADM.[RB_AU_PROD] ADD CONSTRAINT [PK_RB_AU_PROD] PRIMARY KEY CLUSTERED ([ID], [PRODUKT]);
ALTER TABLE SYSADM.[RB_AU_PRODGRP] ADD CONSTRAINT [PK_RB_AU_PRODGRP] PRIMARY KEY CLUSTERED ([ID], [PRODART], [PRODGRP]);
ALTER TABLE SYSADM.[RB_KG_PROD] ADD CONSTRAINT [PK_RB_KG_PROD] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_KG_WGR] ADD CONSTRAINT [PK_RB_KG_WGR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_KU_PROD] ADD CONSTRAINT [PK_RB_KU_PROD] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_KU_WGR] ADD CONSTRAINT [PK_RB_KU_WGR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_LG_PROD] ADD CONSTRAINT [PK_RB_LG_PROD] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_LG_WGR] ADD CONSTRAINT [PK_RB_LG_WGR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_LI_PROD] ADD CONSTRAINT [PK_RB_LI_PROD] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_LI_WGR] ADD CONSTRAINT [PK_RB_LI_WGR] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_RABATT] ADD CONSTRAINT [PK_RB_RABATT] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[RB_STAFFEL] ADD CONSTRAINT [PK_RB_STAFFEL] PRIMARY KEY CLUSTERED ([ID], [GRENZE]);
ALTER TABLE SYSADM.[RR_DESCR_FIELD] ADD CONSTRAINT [PK_RR_DESCR_FIELD] PRIMARY KEY CLUSTERED ([TABID], [FLDID], [LANGUAGE], [SEQNR]);
ALTER TABLE SYSADM.[RR_DESCR_TABLE] ADD CONSTRAINT [PK_RR_DESCR_TABLE] PRIMARY KEY CLUSTERED ([TABID], [LANGUAGE], [SEQNR]);
ALTER TABLE SYSADM.[RR_FIELD] ADD CONSTRAINT [PK_RR_FIELD] PRIMARY KEY CLUSTERED ([TABID], [FLDID]);
ALTER TABLE SYSADM.[RR_FIELD_CON] ADD CONSTRAINT [PK_RR_FIELD_CON] PRIMARY KEY CLUSTERED ([TABID], [FLDID]);
ALTER TABLE SYSADM.[RR_FIELDVIEW] ADD CONSTRAINT [PK_RR_FIELDVIEW] PRIMARY KEY CLUSTERED ([TABID], [FLDID], [LANGUAGE]);
ALTER TABLE SYSADM.[RR_INDEXES] ADD CONSTRAINT [PK_RR_INDEXES] PRIMARY KEY CLUSTERED ([IDXID], [TABID], [FLDID]);
ALTER TABLE SYSADM.[RR_JOB] ADD CONSTRAINT [PK_RR_JOB] PRIMARY KEY CLUSTERED ([JOBNR], [TABID]);
ALTER TABLE SYSADM.[RR_JOBHEAD] ADD CONSTRAINT [PK_RR_JOBHEAD] PRIMARY KEY CLUSTERED ([JOBNR]);
ALTER TABLE SYSADM.[RR_LANGUAGE] ADD CONSTRAINT [PK_RR_LANGUAGE] PRIMARY KEY CLUSTERED ([LANGUAGE]);
ALTER TABLE SYSADM.[RR_MSG] ADD CONSTRAINT [PK_RR_MSG] PRIMARY KEY CLUSTERED ([MSGNR], [LANGUAGE]);
ALTER TABLE SYSADM.[RR_NUM] ADD CONSTRAINT [PK_RR_NUM] PRIMARY KEY CLUSTERED ([CODE]);
ALTER TABLE SYSADM.[RR_REF] ADD CONSTRAINT [PK_RR_REF] PRIMARY KEY CLUSTERED ([REFNR], [FLDID]);
ALTER TABLE SYSADM.[RR_REFHEAD] ADD CONSTRAINT [PK_RR_REFHEAD] PRIMARY KEY CLUSTERED ([REFNR]);
ALTER TABLE SYSADM.[RR_SITE] ADD CONSTRAINT [PK_RR_SITE] PRIMARY KEY CLUSTERED ([SITENR]);
ALTER TABLE SYSADM.[RR_TABLE] ADD CONSTRAINT [PK_RR_TABLE] PRIMARY KEY CLUSTERED ([TABID]);
ALTER TABLE SYSADM.[RR_TABLE_HEAD_DETAIL_RELATIONS] ADD CONSTRAINT [PK_RR_TABLE_HEAD_DETAIL_RELATIONS] PRIMARY KEY CLUSTERED ([TABID_HEAD], [TABID_DETAIL]);
ALTER TABLE SYSADM.[RR_TABLEVIEW] ADD CONSTRAINT [PK_RR_TABLEVIEW] PRIMARY KEY CLUSTERED ([TABID], [LANGUAGE]);
ALTER TABLE SYSADM.[RR_USER_DESCR_FIELD] ADD CONSTRAINT [PK_RR_USER_DESCR_FIELD] PRIMARY KEY CLUSTERED ([TABID], [FLDID], [LANGUAGE]);
ALTER TABLE SYSADM.[RR_USER_DESCR_TABLE] ADD CONSTRAINT [PK_RR_USER_DESCR_TABLE] PRIMARY KEY CLUSTERED ([TABID], [LANGUAGE]);
ALTER TABLE SYSADM.[SST_KU_REKLA] ADD CONSTRAINT [PK_SST_KU_REKLA] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [KUNDE], [KMB_WGR], [PRODUKT], [ADIENST], [REKLA_ORT], [REKLA_GRND]);
ALTER TABLE SYSADM.[SST_KUNDEN] ADD CONSTRAINT [PK_SST_KUNDEN] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [KUNDE], [KMB_WGR], [PRODUKT], [ADIENST], [GESCHART], [AV_BEREICH]);
ALTER TABLE SYSADM.[SST_LI_REKLA] ADD CONSTRAINT [PK_SST_LI_REKLA] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [LIEFERANT], [KMB_WGR], [PRODUKT], [ADIENST], [REKLA_ORT], [REKLA_GRND]);
ALTER TABLE SYSADM.[SST_LIEFERANTEN] ADD CONSTRAINT [PK_SST_LIEFERANTEN] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [LIEFERANT], [KMB_WGR], [PRODUKT], [ADIENST], [GESCHART], [AV_BEREICH]);
ALTER TABLE SYSADM.[SST_PARAM] ADD CONSTRAINT [PK_SST_PARAM] PRIMARY KEY CLUSTERED ([MANDANT], [STAT_TYPE]);
ALTER TABLE SYSADM.[ST_ARD_TEMP] ADD CONSTRAINT [PK_ST_ARD_TEMP] PRIMARY KEY CLUSTERED ([ABNR], [PRODNR1], [PRODNR2], [PRODNR3], [RPGR]);
ALTER TABLE SYSADM.[ST_INFO] ADD CONSTRAINT [PK_ST_INFO] PRIMARY KEY CLUSTERED ([AV_BEREICH], [AUFTR], [WGR_KMB_STAT], [ERF_DATUM]);
ALTER TABLE SYSADM.[ST_INFO_PROD] ADD CONSTRAINT [PK_ST_INFO_PROD] PRIMARY KEY CLUSTERED ([AV_BEREICH], [AUFTR], [WGR_KMB_STAT], [PROD_DATUM]);
ALTER TABLE SYSADM.[ST_KU_REKLA] ADD CONSTRAINT [PK_ST_KU_REKLA] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [KUNDE], [KMB_WGR], [PRODUKT], [ADIENST], [REKLA_ORT], [REKLA_GRND]);
ALTER TABLE SYSADM.[ST_KUNDEN] ADD CONSTRAINT [PK_ST_KUNDEN] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [KUNDE], [KMB_WGR], [PRODUKT], [ADIENST], [GESCHART], [AV_BEREICH]);
ALTER TABLE SYSADM.[ST_LI_REKLA] ADD CONSTRAINT [PK_ST_LI_REKLA] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [LIEFERANT], [KMB_WGR], [PRODUKT], [ADIENST], [REKLA_ORT], [REKLA_GRND]);
ALTER TABLE SYSADM.[ST_LIEFERANTEN] ADD CONSTRAINT [PK_ST_LIEFERANTEN] PRIMARY KEY CLUSTERED ([DATUM], [MANDANT], [LIEFERANT], [KMB_WGR], [PRODUKT], [ADIENST], [GESCHART], [AV_BEREICH]);
ALTER TABLE SYSADM.[ST_PROV] ADD CONSTRAINT [PK_ST_PROV] PRIMARY KEY CLUSTERED ([ADIENST], [DATUM], [KUNDE], [AUFTRAG]);
ALTER TABLE SYSADM.[ST_STATISTIK] ADD CONSTRAINT [PK_ST_STATISTIK] PRIMARY KEY CLUSTERED ([ST_NAME]);
ALTER TABLE SYSADM.[VP_PROVSATZ] ADD CONSTRAINT [PK_VP_PROVSATZ] PRIMARY KEY CLUSTERED ([VP_ADIENST], [VP_KUNDE], [VP_LISTE_ID], [VP_SCHLS_ID], [VP_KALKTYPE], [VP_WGR], [VP_PRODUKT], [VP_STAFFEL], [VP_DATUM_VALID]);
ALTER TABLE SYSADM.[ZU_ISO_AUF1] ADD CONSTRAINT [PK_ZU_ISO_AUF1] PRIMARY KEY CLUSTERED ([TABELLE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [GLASDICKE]);
ALTER TABLE SYSADM.[ZU_ISO_AUF2] ADD CONSTRAINT [PK_ZU_ISO_AUF2] PRIMARY KEY CLUSTERED ([TABELLE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [AUS_ID]);
ALTER TABLE SYSADM.[ZU_ISO_AUF3] ADD CONSTRAINT [PK_ZU_ISO_AUF3] PRIMARY KEY CLUSTERED ([TABELLE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID]);
ALTER TABLE SYSADM.[ZU_KG_ISO_AUF1] ADD CONSTRAINT [PK_ZU_KG_ISO_AUF1] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [GLASDICKE]);
ALTER TABLE SYSADM.[ZU_KG_ISO_AUF2] ADD CONSTRAINT [PK_ZU_KG_ISO_AUF2] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [AUS_ID]);
ALTER TABLE SYSADM.[ZU_KG_ISO_AUF3] ADD CONSTRAINT [PK_ZU_KG_ISO_AUF3] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID]);
ALTER TABLE SYSADM.[ZU_KG_PRGRU] ADD CONSTRAINT [PK_ZU_KG_PRGRU] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDEN_GRP], [LISTE_ID], [SCHLS_ID], [TYP], [DICKE], [PR_GRU]);
ALTER TABLE SYSADM.[ZU_KU_ISO_AUF1] ADD CONSTRAINT [PK_ZU_KU_ISO_AUF1] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [GLASDICKE]);
ALTER TABLE SYSADM.[ZU_KU_ISO_AUF2] ADD CONSTRAINT [PK_ZU_KU_ISO_AUF2] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [AUS_ID]);
ALTER TABLE SYSADM.[ZU_KU_ISO_AUF3] ADD CONSTRAINT [PK_ZU_KU_ISO_AUF3] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [KUNDE], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID]);
ALTER TABLE SYSADM.[ZU_KU_PRGRU] ADD CONSTRAINT [PK_ZU_KU_PRGRU] PRIMARY KEY CLUSTERED ([OBJEKT], [KUNDE], [LISTE_ID], [SCHLS_ID], [TYP], [DICKE], [PR_GRU]);
ALTER TABLE SYSADM.[ZU_LG_ISO_AUF1] ADD CONSTRAINT [PK_ZU_LG_ISO_AUF1] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANTEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [GLASDICKE]);
ALTER TABLE SYSADM.[ZU_LG_ISO_AUF2] ADD CONSTRAINT [PK_ZU_LG_ISO_AUF2] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANTEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [AUS_ID]);
ALTER TABLE SYSADM.[ZU_LG_ISO_AUF3] ADD CONSTRAINT [PK_ZU_LG_ISO_AUF3] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANTEN_GRP], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID]);
ALTER TABLE SYSADM.[ZU_LG_PRGRU] ADD CONSTRAINT [PK_ZU_LG_PRGRU] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANTEN_GRP], [LISTE_ID], [SCHLS_ID], [TYP], [DICKE], [PR_GRU]);
ALTER TABLE SYSADM.[ZU_LI_ISO_AUF1] ADD CONSTRAINT [PK_ZU_LI_ISO_AUF1] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANT], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [GLASDICKE]);
ALTER TABLE SYSADM.[ZU_LI_ISO_AUF2] ADD CONSTRAINT [PK_ZU_LI_ISO_AUF2] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANT], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID], [AUS_ID]);
ALTER TABLE SYSADM.[ZU_LI_ISO_AUF3] ADD CONSTRAINT [PK_ZU_LI_ISO_AUF3] PRIMARY KEY CLUSTERED ([TABELLE], [OBJEKT], [LIEFERANT], [TYP], [LISTE_ID], [SCHLS_ID], [SON_ID]);
ALTER TABLE SYSADM.[ZU_LI_PRGRU] ADD CONSTRAINT [PK_ZU_LI_PRGRU] PRIMARY KEY CLUSTERED ([OBJEKT], [LIEFERANT], [LISTE_ID], [SCHLS_ID], [TYP], [DICKE], [PR_GRU]);
ALTER TABLE SYSADM.[ZU_PRGRU] ADD CONSTRAINT [PK_ZU_PRGRU] PRIMARY KEY CLUSTERED ([LISTE_ID], [SCHLS_ID], [TYP], [DICKE], [PR_GRU]);
ALTER TABLE SYSADM.[ZU_SONST] ADD CONSTRAINT [PK_ZU_SONST] PRIMARY KEY CLUSTERED ([ID], [GTYP1], [GTYP2], [GRENZE1], [GRENZE2]);
ALTER TABLE SYSADM.[ZU_SONST_KPF] ADD CONSTRAINT [PK_ZU_SONST_KPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[ZU_STRUKTUR] ADD CONSTRAINT [PK_ZU_STRUKTUR] PRIMARY KEY CLUSTERED ([LISTE_ID], [SCHLS_ID]);
ALTER TABLE SYSADM.[ZW_AGGREGATE] ADD CONSTRAINT [PK_ZW_AGGREGATE] PRIMARY KEY CLUSTERED ([AGG_ID]);
ALTER TABLE SYSADM.[ZW_AGGTYPEN] ADD CONSTRAINT [PK_ZW_AGGTYPEN] PRIMARY KEY CLUSTERED ([AGG_TYP]);
ALTER TABLE SYSADM.[ZW_ANGEB_BEST_TEILE] ADD CONSTRAINT [PK_ZW_ANGEB_BEST_TEILE] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [SUB_POS], [BOM_ID]);
ALTER TABLE SYSADM.[ZW_ANGEB_ZEIT] ADD CONSTRAINT [PK_ZW_ANGEB_ZEIT] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [SUB_POS], [BOM_ID], [AGG], [ARBART], [ARBFOLGE]);
ALTER TABLE SYSADM.[ZW_ARTSPERRE] ADD CONSTRAINT [PK_ZW_ARTSPERRE] PRIMARY KEY CLUSTERED ([PRODUKT], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_ARTZUO] ADD CONSTRAINT [PK_ZW_ARTZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_AUFTR_ZEIT] ADD CONSTRAINT [PK_ZW_AUFTR_ZEIT] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [SUB_POS], [BOM_ID], [AGG], [ARBART], [ARBFOLGE]);
ALTER TABLE SYSADM.[ZW_BEATYPEN] ADD CONSTRAINT [PK_ZW_BEATYPEN] PRIMARY KEY CLUSTERED ([BEA_TYP]);
ALTER TABLE SYSADM.[ZW_BEST_TEILE] ADD CONSTRAINT [PK_ZW_BEST_TEILE] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [SUB_POS], [BOM_ID]);
ALTER TABLE SYSADM.[ZW_FERTIG] ADD CONSTRAINT [PK_ZW_FERTIG] PRIMARY KEY CLUSTERED ([F_AUFNR], [F_POSNR], [F_BOM_ID], [F_AGG], [F_ARBART], [F_PROD_ZEIT]);
ALTER TABLE SYSADM.[ZW_FOARBSPERRE] ADD CONSTRAINT [PK_ZW_FOARBSPERRE] PRIMARY KEY CLUSTERED ([AGG], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_FOLGEZUO] ADD CONSTRAINT [PK_ZW_FOLGEZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [FOLGEARB], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_FOMASPERRE] ADD CONSTRAINT [PK_ZW_FOMASPERRE] PRIMARY KEY CLUSTERED ([AGG], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_FORMELZUO] ADD CONSTRAINT [PK_ZW_FORMELZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [MODELL], [FOLGE_NR], [BISANZ]);
ALTER TABLE SYSADM.[ZW_IGLTYPEN] ADD CONSTRAINT [PK_ZW_IGLTYPEN] PRIMARY KEY CLUSTERED ([IGL_TYP]);
ALTER TABLE SYSADM.[ZW_KALENDER] ADD CONSTRAINT [PK_ZW_KALENDER] PRIMARY KEY CLUSTERED ([ARBID], [AGG_ID], [ID]);
ALTER TABLE SYSADM.[ZW_KALFI] ADD CONSTRAINT [PK_ZW_KALFI] PRIMARY KEY CLUSTERED ([ARBID], [AGG_ID], [ID]);
ALTER TABLE SYSADM.[ZW_KANTENZUO] ADD CONSTRAINT [PK_ZW_KANTENZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [KANTEN_KZ], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_KOMBIGRP] ADD CONSTRAINT [PK_ZW_KOMBIGRP] PRIMARY KEY CLUSTERED ([PRODUKT], [PRG], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_KOMBIZUO] ADD CONSTRAINT [PK_ZW_KOMBIZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [PRA], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_KUSPERRE] ADD CONSTRAINT [PK_ZW_KUSPERRE] PRIMARY KEY CLUSTERED ([KUNDE], [AGG_NR], [PRODUKT]);
ALTER TABLE SYSADM.[ZW_LAG_PRGZUO] ADD CONSTRAINT [PK_ZW_LAG_PRGZUO] PRIMARY KEY CLUSTERED ([PRODUKTGRP_NR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_LAGER_ZUORD] ADD CONSTRAINT [PK_ZW_LAGER_ZUORD] PRIMARY KEY CLUSTERED ([ID], [PRODUKTBER], [BA_PRODUKT], [PRODART], [PRODGRP]);
ALTER TABLE SYSADM.[ZW_LEITARBART] ADD CONSTRAINT [PK_ZW_LEITARBART] PRIMARY KEY CLUSTERED ([AGG_TYP]);
ALTER TABLE SYSADM.[ZW_MAUSFALL] ADD CONSTRAINT [PK_ZW_MAUSFALL] PRIMARY KEY CLUSTERED ([AGG_NR]);
ALTER TABLE SYSADM.[ZW_MODGRU] ADD CONSTRAINT [PK_ZW_MODGRU] PRIMARY KEY CLUSTERED ([TXT]);
ALTER TABLE SYSADM.[ZW_POOLPOS] ADD CONSTRAINT [PK_ZW_POOLPOS] PRIMARY KEY CLUSTERED ([PP_AUFNR], [PP_POS], [PP_UPOS]);
ALTER TABLE SYSADM.[ZW_PRAZUO] ADD CONSTRAINT [PK_ZW_PRAZUO] PRIMARY KEY CLUSTERED ([PRD_NR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_PRGSPERRE] ADD CONSTRAINT [PK_ZW_PRGSPERRE] PRIMARY KEY CLUSTERED ([PRODUKTGRP_NR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_PRGTYPEN] ADD CONSTRAINT [PK_ZW_PRGTYPEN] PRIMARY KEY CLUSTERED ([PRG_TYP]);
ALTER TABLE SYSADM.[ZW_PRGZUO] ADD CONSTRAINT [PK_ZW_PRGZUO] PRIMARY KEY CLUSTERED ([PRODUKTGRP_NR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_PRIORITY] ADD CONSTRAINT [PK_ZW_PRIORITY] PRIMARY KEY CLUSTERED ([AGG], [ARBART], [KDNR], [WGR], [K_LAENGE], [DICKE], [ANZAHL], [DDIAM]);
ALTER TABLE SYSADM.[ZW_PROBER] ADD CONSTRAINT [PK_ZW_PROBER] PRIMARY KEY CLUSTERED ([NUMMER]);
ALTER TABLE SYSADM.[ZW_SERIEN_WERT] ADD CONSTRAINT [PK_ZW_SERIEN_WERT] PRIMARY KEY CLUSTERED ([ID], [STCK]);
ALTER TABLE SYSADM.[ZW_SERIENBEZ] ADD CONSTRAINT [PK_ZW_SERIENBEZ] PRIMARY KEY CLUSTERED ([SER_NR]);
ALTER TABLE SYSADM.[ZW_SL_TEMP] ADD CONSTRAINT [PK_ZW_SL_TEMP] PRIMARY KEY CLUSTERED ([SL_AUFNR], [SL_LEVEL], [SL_BEA], [SL_RFOZ], [SL_ID], [SL_FOLGE]);
ALTER TABLE SYSADM.[ZW_SL_TEMPS] ADD CONSTRAINT [PK_ZW_SL_TEMPS] PRIMARY KEY CLUSTERED ([SL_LEVEL], [SL_BEA], [SL_RFOZ], [SL_ID], [SL_FOLGE]);
ALTER TABLE SYSADM.[ZW_SOZU] ADD CONSTRAINT [PK_ZW_SOZU] PRIMARY KEY CLUSTERED ([ID], [GTYP1], [GTYP2], [GRENZE1], [GRENZE2]);
ALTER TABLE SYSADM.[ZW_SOZU_KPF] ADD CONSTRAINT [PK_ZW_SOZU_KPF] PRIMARY KEY CLUSTERED ([ID]);
ALTER TABLE SYSADM.[ZW_STAT_TEMP] ADD CONSTRAINT [PK_ZW_STAT_TEMP] PRIMARY KEY CLUSTERED ([AGG], [AART]);
ALTER TABLE SYSADM.[ZW_STATI_GRP] ADD CONSTRAINT [PK_ZW_STATI_GRP] PRIMARY KEY CLUSTERED ([GRP_NR]);
ALTER TABLE SYSADM.[ZW_STATISTIK] ADD CONSTRAINT [PK_ZW_STATISTIK] PRIMARY KEY CLUSTERED ([AGG], [AART], [DATUM]);
ALTER TABLE SYSADM.[ZW_STGRP_TEMP] ADD CONSTRAINT [PK_ZW_STGRP_TEMP] PRIMARY KEY CLUSTERED ([AVB], [GRP_NR], [AUFNR], [POSNR], [BOM]);
ALTER TABLE SYSADM.[ZW_TEMP_ANGEB_ZEIT] ADD CONSTRAINT [PK_ZW_TEMP_ANGEB_ZEIT] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [AGG], [ARBART]);
ALTER TABLE SYSADM.[ZW_TEMP_ZEIT] ADD CONSTRAINT [PK_ZW_TEMP_ZEIT] PRIMARY KEY CLUSTERED ([AUFNR], [POSNR], [BOM_ID], [AGG], [ARBART]);
ALTER TABLE SYSADM.[ZW_TR_SONDER] ADD CONSTRAINT [PK_ZW_TR_SONDER] PRIMARY KEY CLUSTERED ([ARBART], [AGG]);
ALTER TABLE SYSADM.[ZW_UEZMATRIX] ADD CONSTRAINT [PK_ZW_UEZMATRIX] PRIMARY KEY CLUSTERED ([RASTER], [ARB1], [ARB2]);
ALTER TABLE SYSADM.[ZW_UNIVZUO] ADD CONSTRAINT [PK_ZW_UNIVZUO] PRIMARY KEY CLUSTERED ([PRODUKT], [FOLGEARB], [MODELL], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_VOMASPERRE] ADD CONSTRAINT [PK_ZW_VOMASPERRE] PRIMARY KEY CLUSTERED ([AGG], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_VWTMATRIX] ADD CONSTRAINT [PK_ZW_VWTMATRIX] PRIMARY KEY CLUSTERED ([RASTER], [PRB1], [PRB2]);
ALTER TABLE SYSADM.[ZW_WGRSPERRE] ADD CONSTRAINT [PK_ZW_WGRSPERRE] PRIMARY KEY CLUSTERED ([WGR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_WGRZUO] ADD CONSTRAINT [PK_ZW_WGRZUO] PRIMARY KEY CLUSTERED ([WGR], [FOLGE_NR]);
ALTER TABLE SYSADM.[ZW_WML_SP] ADD CONSTRAINT [PK_ZW_WML_SP] PRIMARY KEY CLUSTERED ([SP_NR]);
ALTER TABLE SYSADM.[ZW_WML_VIEW] ADD CONSTRAINT [PK_ZW_WML_VIEW] PRIMARY KEY CLUSTERED ([DATUM], [SPALTE]);
ALTER TABLE SYSADM.[ZW_ZEIT] ADD CONSTRAINT [PK_ZW_ZEIT] PRIMARY KEY CLUSTERED ([ID], [AGG_ID], [GRENZE1], [GRENZE2], [GRENZE3]);
ALTER TABLE SYSADM.[ZW_ZEITKPF] ADD CONSTRAINT [PK_ZW_ZEITKPF] PRIMARY KEY CLUSTERED ([ID], [AGG_ID]);
ALTER TABLE SYSADM.[ZW_ZGLTYPEN] ADD CONSTRAINT [PK_ZW_ZGLTYPEN] PRIMARY KEY CLUSTERED ([ZGL_TYP]);
GO
