-- Origen de DEMO de A+W: en la demo el ERP lee de aquí en vez de la BD de A+W (SQL Server on-prem).
-- Lector: backend/src/Integraciones.Aw/Infrastructure/OrigenPg. Idempotente (CREATE ... IF NOT EXISTS).
CREATE SCHEMA IF NOT EXISTS aw_origen;

-- Catálogo de condiciones de pago (A+W: SYSADM.KA_ZAHLBED). BEZ es PK y distingue mayúsculas, como en A+W.
CREATE TABLE IF NOT EXISTS aw_origen.ka_zahlbed (
    bez        text PRIMARY KEY,
    bruttotage integer,
    nummer     integer NOT NULL
);

-- Calificación crediticia (A+W: SYSADM.KA_BONITAET). Millet la reutiliza como régimen fiscal SAT: FREMD_KEY es la clave
-- c_RegimenFiscal y el cliente se une por KU_KUNDEN.BONITAET = BEZ (Millet, "Documentación funcional de los sistemas
-- satélite" §4.2). BEZ es PK, como en A+W.
CREATE TABLE IF NOT EXISTS aw_origen.ka_bonitaet (
    bez         text PRIMARY KEY,
    fremd_key   text,
    kz_gesperrt integer NOT NULL DEFAULT 0,
    nummer      integer
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
-- U1.8b: columna agregada después; ADD COLUMN IF NOT EXISTS mantiene el script idempotente sobre BD ya creadas.
ALTER TABLE aw_origen.ku_kunden ADD COLUMN IF NOT EXISTS bonitaet text;

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

-- ───────── Flujo 2: ingesta de pedidos para Facturación (MILLET_INTEGRACION en A+W; doc integration/04) ─────────
-- Mismos nombres y columnas que la tabla-puente y las vistas de docs/operacion/aw-integracion-scripts (02 y 03), en el
-- esquema dbo para que el lector de la API use el mismo SQL. Las vistas de pedido son tablas aquí (se siembran en
-- pedidos.sql); las de maestros sí son vistas sobre aw_origen.* para que cliente y producto coincidan con lo sincronizado.
CREATE SCHEMA IF NOT EXISTS dbo;

CREATE TABLE IF NOT EXISTS dbo.aw_solicitud_pedido (
    solicitud_id       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    numero_pedido      text        NOT NULL,
    operacion          smallint    NOT NULL CHECK (operacion IN (1, 2, 3)),  -- 1 Alta | 2 Modificacion | 3 Cancelacion
    version            bigint      NOT NULL,
    creada_at          timestamptz NOT NULL DEFAULT now(),
    erp_pedido_id      uuid,
    estado_facturacion text,
    uuid               text,
    resultado          smallint CHECK (resultado IS NULL OR resultado IN (1, 2, 3, 4)),
    motivo             text,
    procesada_at       timestamptz,
    UNIQUE (numero_pedido, version)
);

-- Retorno ERP -> A+W (script 14 v3, TR-01). Mismas columnas y restricciones que docs/operacion/aw-integracion-scripts/14.
-- resultado, aplicada_at, motivo e intentos los escribe la tarea de TI (en la demo, a mano); el ERP solo inserta y lee.
CREATE TABLE IF NOT EXISTS dbo.aw_retorno (
    retorno_id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    numero_pedido       text        NOT NULL,
    secuencia           integer     NOT NULL,                       -- 1, 2, 3... por pedido, sin importar el tipo
    tipo                smallint    NOT NULL CHECK (tipo IN (1, 2, 3, 4)),  -- 1 Estatus | 2 Saldo | 3 Documento | 4 Pago
    version_pedido      bigint      NOT NULL,
    estatus_destino     smallint,
    origen              smallint,
    usuario_libera      text,
    anticipos_aplicados numeric(18, 2),
    saldo               numeric(18, 2),
    tipo_documento      text,
    folio               text,
    uuid                text,
    importe             numeric(18, 2),
    forma_pago          text,                           -- tipo 4 (v3): clave SAT
    moneda              text,                           -- tipo 4 (v3)
    creada_at           timestamptz NOT NULL DEFAULT now(),
    aplicada_at         timestamptz,
    resultado           smallint CHECK (resultado IS NULL OR resultado IN (1, 2, 3)),  -- NULL pendiente | 1 Aplicada | 2 Rechazada | 3 Desfasada
    motivo              text,
    intentos            integer     NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    UNIQUE (numero_pedido, secuencia),
    CHECK (tipo <> 1 OR COALESCE(estatus_destino, 0) IN (70, 115)),
    CHECK ((COALESCE(estatus_destino, 0) = 70 AND COALESCE(origen, 0) IN (1, 2)) OR (COALESCE(estatus_destino, 0) <> 70 AND origen IS NULL)),
    CHECK (origen IS DISTINCT FROM 2 OR usuario_libera IS NOT NULL),
    CHECK (tipo <> 2 OR saldo IS NOT NULL),
    CHECK (tipo <> 3 OR (uuid IS NOT NULL AND tipo_documento IN ('FacturaFinal', 'NotaCredito'))),
    CHECK (tipo <> 4 OR (folio IS NOT NULL AND forma_pago IS NOT NULL AND moneda IS NOT NULL AND importe IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS dbo.vw_erp_pedido_cabecera (
    numero_pedido            text PRIMARY KEY,
    numero_sucursal          text,
    cliente_ref              text NOT NULL,
    cliente_nombre           text,
    rfc_cliente              text,
    uso_cfdi                 text,
    metodo_pago              text,
    forma_pago               text,
    condicion_pago           text,
    divisa                   text,
    obra_id                  bigint,
    obra_nombre              text,
    notas_pedido             text,
    canal_ventas             text,
    clase                    text,
    fecha_transaccion        date,
    pedido_sustituido_numero bigint,
    estado_origen            text,
    total_cantidad           numeric,
    total_m2                 numeric,
    importe_total            numeric,
    iva_porcentaje           numeric(9, 4),
    ranura                   numeric(28, 8)
);

CREATE TABLE IF NOT EXISTS dbo.vw_erp_pedido_linea (
    numero_pedido        text    NOT NULL,
    numero_posicion      integer NOT NULL,
    producto_ref         text    NOT NULL,
    descripcion          text,
    detalle_procesos     text,
    cantidad             numeric NOT NULL,
    unidad_medida        text,
    importe_pieza        numeric(28, 8) NOT NULL,
    descuento_porcentaje numeric(28, 8),
    descuento            numeric(28, 4),
    almacen_nivel_1      text,
    almacen_nivel_2      text,
    almacen_nivel_3      text,
    almacen_nivel_4      text,
    almacen_id_ubicacion bigint,
    requiere_pedimento   boolean,
    PRIMARY KEY (numero_pedido, numero_posicion)
);

CREATE TABLE IF NOT EXISTS dbo.vw_erp_pedido_componente (
    numero_pedido   text    NOT NULL,
    numero_posicion integer NOT NULL,
    producto_ref    text,
    descripcion     text,
    alto_mm         numeric,
    ancho_mm        numeric,
    m2_por_pieza    numeric,
    importe         numeric
);

-- Limpieza de A+W ('<indf>' y vacíos → NULL), como dbo.fn_limpia / fn_solo_alfanumerico de 03_create_views.sql.
CREATE OR REPLACE VIEW dbo.vw_erp_cliente AS
SELECT id::text                                                                           AS cliente_ref,
       NULLIF(NULLIF(btrim(coalesce(name1, '') || ' ' || coalesce(name2, '')), ''), '<indf>') AS razon_social,
       NULLIF(regexp_replace(coalesce(ust_id, ''), '[^A-Za-z0-9]', '', 'g'), '')           AS rfc,
       NULLIF(NULLIF(btrim(name3), ''), '<indf>')                                         AS calle,
       NULLIF(NULLIF(btrim(strasse), ''), '<indf>')                                       AS colonia,
       NULLIF(NULLIF(btrim(plz), ''), '<indf>')                                           AS cp,
       NULLIF(NULLIF(btrim(ort), ''), '<indf>')                                           AS ciudad,
       NULLIF(NULLIF(btrim(provinz), ''), '<indf>')                                       AS estado,
       NULLIF(NULLIF(btrim(land), ''), '<indf>')                                          AS pais,
       NULLIF(regexp_replace(coalesce(tlf1, ''), '[^A-Za-z0-9]', '', 'g'), '')             AS telefono
  FROM aw_origen.ku_kunden;

CREATE OR REPLACE VIEW dbo.vw_erp_articulo AS
SELECT producto_ref::text                                        AS producto_ref,
       NULLIF(NULLIF(btrim(descripcion), ''), '<indf>')          AS descripcion,
       upper(replace(coalesce(unidad_medida, ''), '²', '2'))     AS unidad_medida
  FROM aw_origen.erp_articulo;

-- Flujo 3 OTR · tablas puente definidas por el ERP; datos sintéticos en otr.sql.
CREATE TABLE IF NOT EXISTS dbo.aw_otr_envio (
    otr_envio_id varchar(100) NOT NULL,
    version bigint NOT NULL CHECK (version > 0),
    cliente_numero_aw varchar(50) NOT NULL,
    sucursal_clave_aw varchar(50) NOT NULL,
    cliente varchar(200) NOT NULL,
    direccion_entrega varchar(1000) NOT NULL,
    producto varchar(500),
    numero_contenedor varchar(200),
    ruta varchar(200),
    bultos integer CHECK (bultos >= 0),
    peso_kg numeric(18,6) CHECK (peso_kg >= 0),
    fecha_carga_prevista date,
    booking varchar(200),
    bl_guia varchar(200),
    operacion varchar(20) NOT NULL CHECK (operacion IN ('Alta','Modificacion','Cancelacion')),
    creada_at timestamptz NOT NULL DEFAULT now(),
    erp_resultado varchar(30),
    erp_motivo varchar(500),
    erp_procesada_at timestamptz,
    PRIMARY KEY (otr_envio_id, version)
);
CREATE TABLE IF NOT EXISTS dbo.aw_otr_envio_linea (
    otr_envio_id varchar(100) NOT NULL,
    version bigint NOT NULL,
    numero_pedido varchar(50) NOT NULL,
    posicion integer NOT NULL CHECK (posicion > 0),
    cantidad numeric(18,6) NOT NULL CHECK (cantidad > 0),
    unidad varchar(20) NOT NULL,
    PRIMARY KEY (otr_envio_id, version, numero_pedido, posicion),
    FOREIGN KEY (otr_envio_id,version) REFERENCES dbo.aw_otr_envio(otr_envio_id,version)
);
CREATE INDEX IF NOT EXISTS ix_aw_otr_pendiente ON dbo.aw_otr_envio (otr_envio_id,version) WHERE erp_resultado IS NULL OR erp_resultado = 'Error';
CREATE TABLE IF NOT EXISTS dbo.erp_otr_estado (
    salida_id uuid PRIMARY KEY,
    otr_envio_id varchar(100) NOT NULL,
    tipo varchar(50) NOT NULL,
    version_erp integer NOT NULL CHECK (version_erp > 0),
    intento int NOT NULL DEFAULT 1 CHECK (intento > 0),
    valor_json text NOT NULL CHECK (jsonb_typeof(valor_json::jsonb) = 'object'),
    creada_at timestamptz NOT NULL DEFAULT now(),
    aw_acuse_at timestamptz,
    aw_resultado varchar(30),
    aw_motivo varchar(500),
    UNIQUE (otr_envio_id,tipo,version_erp,intento)
);
