-- Origen de DEMO de A+W: en la demo el ERP lee de aquí en vez de la BD de A+W (SQL Server on-prem).
-- Lector: backend/src/Integraciones.Aw/Infrastructure/OrigenPg. Idempotente (CREATE ... IF NOT EXISTS).
CREATE SCHEMA IF NOT EXISTS aw_origen;

-- Catálogo de condiciones de pago (A+W: SYSADM.KA_ZAHLBED). BEZ es PK y distingue mayúsculas, como en A+W.
CREATE TABLE IF NOT EXISTS aw_origen.ka_zahlbed (
    bez        text PRIMARY KEY,
    bruttotage integer,
    nummer     integer NOT NULL
);

-- Clientes (A+W: SYSADM.KU_KUNDEN; mismas columnas). El ID 0 es el registro nulo de A+W: el lector pagina con id > 0.
CREATE TABLE IF NOT EXISTS aw_origen.ku_kunden (
    id               integer PRIMARY KEY,
    mandant          integer NOT NULL DEFAULT 1,
    name1            text,
    name2            text,
    name3            text,
    strasse          text,
    ort              text,
    plz              text,
    provinz          text,
    land             text,
    ust_id           text,
    steuernummer     text,
    tlf1             text,
    tlf2             text,
    mail             text,
    zahlbed          text,
    waehrung         text,
    kredit_limit     numeric,
    kredit_limit1    numeric,
    kredit_limit_net double precision,
    kz_status        integer,
    kz_gesperrt      integer NOT NULL DEFAULT 0,
    datum            date,
    transaction_time timestamp
);

-- Productos con la forma de vw_erp_articulo (docs/integration/06 §3): una fila por producto; variantes y árbol de
-- piezas ya armados en jsonb, con las llaves de AwProductoOrigenVariante / AwProductoOrigenComponente (camelCase).
-- Nulo != 0: una medida o espesor que no se informa va como null, nunca 0.
CREATE TABLE IF NOT EXISTS aw_origen.erp_articulo (
    producto_ref     integer PRIMARY KEY CHECK (producto_ref > 0),
    descripcion      text,
    unidad_medida    text,
    baja             boolean NOT NULL DEFAULT false,
    transaction_time timestamp,
    codigo_modelo    text,
    grupo            text,
    tipo             text,
    wgr              text,
    wgr_descripcion  text,
    variantes        jsonb NOT NULL DEFAULT '[]',
    componentes      jsonb NOT NULL DEFAULT '[]'
);
