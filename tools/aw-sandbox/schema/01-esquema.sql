-- Generado por tools/aw-sandbox/extraer-esquema.sh (solo estructura, sin datos).
-- Columnas limitadas a las que usan los lectores AwClientesSqlOrigen / AwProductosSqlOrigen.
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'SYSADM') EXEC('CREATE SCHEMA SYSADM');
GO

CREATE TABLE SYSADM.KU_KUNDEN (
    [ID] int NOT NULL,
    [MANDANT] int NOT NULL,
    [NAME1] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [NAME2] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [NAME3] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [STRASSE] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [ORT] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [PLZ] nvarchar(11) COLLATE Latin1_General_CS_AS NULL,
    [LAND] nvarchar(6) COLLATE Latin1_General_CS_AS NOT NULL,
    [TLF1] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [TLF2] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [MAIL] nvarchar(254) COLLATE Latin1_General_CS_AS NULL,
    [WAEHRUNG] nvarchar(8) COLLATE Latin1_General_CS_AS NOT NULL,
    [ZAHLBED] nvarchar(100) COLLATE Latin1_General_CS_AS NOT NULL,
    [UST_ID] nvarchar(30) COLLATE Latin1_General_CS_AS NULL,
    [KZ_STATUS] int NULL,
    [KZ_GESPERRT] int NOT NULL,
    [DATUM] date NULL,
    [PROVINZ] nvarchar(20) COLLATE Latin1_General_CS_AS NOT NULL,
    [STEUERNUMMER] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [KREDIT_LIMIT] decimal(28,8) NULL,
    [KREDIT_LIMIT1] decimal(28,8) NULL,
    [TRANSACTION_TIME] datetime NULL,
    [KREDIT_LIMIT_NET] float NULL
);
ALTER TABLE SYSADM.KU_KUNDEN ADD CONSTRAINT [PK_KU_KUNDEN] PRIMARY KEY CLUSTERED ([ID]);
-- omitido (usa columnas fuera del conjunto): IDX_KU_KUNDEN2 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_KU_KUNDEN3 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_KU_KUNDEN4 (IX)
CREATE NONCLUSTERED INDEX [IDX_KU_KUNDEN5] ON SYSADM.KU_KUNDEN ([MANDANT], [ID]);
CREATE NONCLUSTERED INDEX [IDX_KU_KUNDEN6] ON SYSADM.KU_KUNDEN ([PLZ]);
CREATE NONCLUSTERED INDEX [IDX_KU_KUNDEN7] ON SYSADM.KU_KUNDEN ([ORT]);
CREATE NONCLUSTERED INDEX [IDX_KU_KUNDEN8] ON SYSADM.KU_KUNDEN ([NAME1]);
-- omitido (usa columnas fuera del conjunto): IDX_KU_KUNDEN9 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_KU_KUNDEN_ROWID (IX)
GO

CREATE TABLE SYSADM.KA_ZAHLBED (
    [BEZ] nvarchar(100) COLLATE Latin1_General_CS_AS NOT NULL,
    [BRUTTOTAGE] int NULL,
    [NUMMER] int NULL
);
ALTER TABLE SYSADM.KA_ZAHLBED ADD CONSTRAINT [PK_KA_ZAHLBED] PRIMARY KEY CLUSTERED ([BEZ]);
CREATE UNIQUE NONCLUSTERED INDEX [IDX_KA_ZAHLBED2] ON SYSADM.KA_ZAHLBED ([NUMMER]);
-- omitido (usa columnas fuera del conjunto): IDX_KA_ZAHLBED_ROWID (IX)
GO

CREATE TABLE SYSADM.BA_PRODUKTE (
    [BA_PRODUKT] int NOT NULL,
    [BA_MCODE] nvarchar(80) COLLATE Latin1_General_CS_AS NULL,
    [BA_PRODUKTART] nvarchar(40) COLLATE Latin1_General_CS_AS NOT NULL,
    [BA_STD_HOEHE] decimal(28,8) NOT NULL,
    [BA_STD_BREITE] decimal(28,8) NOT NULL,
    [KZ_GESPERRT] int NOT NULL,
    [BA_MASS_DICKE] decimal(28,8) NOT NULL,
    [TRANSACTION_TIME] datetime NULL
);
ALTER TABLE SYSADM.BA_PRODUKTE ADD CONSTRAINT [PK_BA_PRODUKTE] PRIMARY KEY CLUSTERED ([BA_PRODUKT]);
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE10 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE11 (IX)
CREATE NONCLUSTERED INDEX [IDX_BA_PRODUKTE3] ON SYSADM.BA_PRODUKTE ([BA_MCODE]);
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE4 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE5 (IX)
CREATE NONCLUSTERED INDEX [IDX_BA_PRODUKTE6] ON SYSADM.BA_PRODUKTE ([BA_PRODUKTART], [BA_MCODE]);
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE7 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE8 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE9 (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTEEAN (IX)
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE_ROWID (IX)
GO

CREATE TABLE SYSADM.BA_PRODUKTE_BEZ (
    [SPRACH_ID] int NOT NULL,
    [BA_PRODUKT] int NOT NULL,
    [BA_BEZ1] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [BA_BEZ2] nvarchar(40) COLLATE Latin1_General_CS_AS NULL,
    [BA_BEZ3] nvarchar(65) COLLATE Latin1_General_CS_AS NULL,
    [BA_MENGENEINH] nvarchar(20) COLLATE Latin1_General_CS_AS NOT NULL
);
ALTER TABLE SYSADM.BA_PRODUKTE_BEZ ADD CONSTRAINT [PK_BA_PRODUKTE_BEZ] PRIMARY KEY CLUSTERED ([BA_PRODUKT], [SPRACH_ID]);
CREATE UNIQUE NONCLUSTERED INDEX [IDX_BA_PRODUK_BEZ2] ON SYSADM.BA_PRODUKTE_BEZ ([SPRACH_ID], [BA_PRODUKT]);
CREATE NONCLUSTERED INDEX [IDX_BA_PRODUK_BEZ3] ON SYSADM.BA_PRODUKTE_BEZ ([BA_BEZ1]);
-- omitido (usa columnas fuera del conjunto): IDX_BA_PRODUKTE_BEZ_ROWID (IX)
GO

CREATE TABLE SYSADM.BA_STUKL (
    [PRODUKT] int NOT NULL,
    [BOM_PRODUKT] int NOT NULL,
    [BOM_POS] int NOT NULL,
    [BOM_LEVEL] int NOT NULL
);
-- omitido (usa columnas fuera del conjunto): PK_BA_STUKL (PK)
GO
