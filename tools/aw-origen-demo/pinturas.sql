-- DEMO sintético · Pinturas: catálogos publicados por el ERP y pedidos del portal (dialecto Postgres del script
-- docs/operacion/aw-integracion-scripts/16_create_pinturas.sql). Idempotente: repetir la carga es inocuo.
-- Sin roles ni GRANT en el demo; permisos reales (portal solo SELECT e INSERT, sin reenvío) en el script 16 v4.
-- v3: el pedido es cabecera (pinturas_pedido, con fecha_pedido) + líneas (pinturas_pedido_linea). Una copia con la forma
-- v2 (sin fecha_pedido) se reemplaza: es demo sintético.
-- v4: la cabecera lleva empleado_captura_clave (obligatoria) y vendedor_clave (opcional), claves de empleado del ERP.
-- psql -f pinturas.sql
BEGIN;
CREATE TABLE IF NOT EXISTS dbo.pinturas_cliente (
    clave integer PRIMARY KEY,
    razon_social varchar(200) NOT NULL,
    nombre_comercial varchar(200),
    contacto varchar(200),
    telefono varchar(50),
    celular varchar(50),
    email varchar(200),
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS dbo.pinturas_servicio (
    clave varchar(50) PRIMARY KEY,
    nombre varchar(200) NOT NULL,
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS dbo.pinturas_obra (
    clave varchar(50) PRIMARY KEY,
    nombre varchar(200) NOT NULL,
    origen varchar(10) NOT NULL DEFAULT 'PINTURAS' CHECK (origen IN ('AW','PINTURAS')),
    referencia_aw varchar(50),
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS dbo.pinturas_empleado (
    clave varchar(50) PRIMARY KEY,
    nombre varchar(200) NOT NULL,
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'dbo' AND table_name = 'pinturas_pedido')
     AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                     WHERE table_schema = 'dbo' AND table_name = 'pinturas_pedido' AND column_name = 'fecha_pedido') THEN
    DROP TABLE dbo.pinturas_pedido;
  END IF;
END $$;
CREATE TABLE IF NOT EXISTS dbo.pinturas_pedido (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    folio_orden varchar(50) NOT NULL UNIQUE,
    fecha_pedido date NOT NULL,
    cliente_clave integer NOT NULL,
    cliente_nombre varchar(200) NOT NULL,
    obra_clave varchar(50),
    obra_nombre varchar(200),
    comentarios varchar(1000),
    iva_porcentaje numeric(5,2) NOT NULL CHECK (iva_porcentaje >= 0),
    moneda char(3) NOT NULL DEFAULT 'MXN',
    empleado_captura_clave varchar(50) NOT NULL,
    vendedor_clave varchar(50),
    creado_at timestamptz NOT NULL DEFAULT now(),
    erp_resultado smallint CHECK (erp_resultado IN (1,2)),
    erp_pedido_id uuid,
    erp_error varchar(500),
    erp_procesado_at timestamptz
);
ALTER TABLE dbo.pinturas_pedido ADD COLUMN IF NOT EXISTS empleado_captura_clave varchar(50) NOT NULL DEFAULT '';
ALTER TABLE dbo.pinturas_pedido ALTER COLUMN empleado_captura_clave DROP DEFAULT;
ALTER TABLE dbo.pinturas_pedido ADD COLUMN IF NOT EXISTS vendedor_clave varchar(50);
CREATE TABLE IF NOT EXISTS dbo.pinturas_pedido_linea (
    pedido_id bigint NOT NULL REFERENCES dbo.pinturas_pedido (id),
    linea integer NOT NULL CHECK (linea > 0),
    servicio_clave varchar(50) NOT NULL,
    piezas integer CHECK (piezas > 0),
    m2 numeric(18,6) NOT NULL CHECK (m2 > 0),
    precio_m2 numeric(18,6) NOT NULL CHECK (precio_m2 >= 0),
    PRIMARY KEY (pedido_id, linea)
);
CREATE INDEX IF NOT EXISTS ix_pinturas_pedido_pendiente ON dbo.pinturas_pedido (creado_at, id) WHERE erp_resultado IS NULL;

INSERT INTO dbo.pinturas_cliente (clave,razon_social,nombre_comercial,contacto,telefono,celular,email) VALUES
  (900001,'DEMO · Aluminios del Sureste SA de CV','DEMO Aluminios','DEMO Contacto','9990000001','9990000002','demo1@example.com'),
  (900002,'DEMO · Fachadas Peninsulares SA de CV',NULL,NULL,NULL,NULL,NULL)
ON CONFLICT (clave) DO NOTHING;
INSERT INTO dbo.pinturas_servicio (clave,nombre) VALUES
  ('DEMO-PIN-S1','DEMO · Pintura blanco mate'),
  ('DEMO-PIN-S2','DEMO · Pintura negro satinado'),
  ('DEMO-PIN-S3','DEMO · Pintura madera nogal')
ON CONFLICT (clave) DO NOTHING;
INSERT INTO dbo.pinturas_obra (clave,nombre) VALUES ('DEMO-OBRA-1','DEMO · Torre ficticia') ON CONFLICT (clave) DO NOTHING;
-- Claves de empleados del seed de demo del ERP (CatalogosTestSeedHostedService): el ERP valida contra su catálogo.
INSERT INTO dbo.pinturas_empleado (clave,nombre) VALUES ('EMP-001','DEMO · Capturista de Pinturas'),('EMP-002','DEMO · Vendedor de Pinturas')
ON CONFLICT (clave) DO NOTHING;
-- Pedido pendiente (sin acuse del ERP): capturado un día antes de transmitirse, con dos líneas.
INSERT INTO dbo.pinturas_pedido (folio_orden,fecha_pedido,cliente_clave,cliente_nombre,obra_clave,obra_nombre,comentarios,iva_porcentaje,empleado_captura_clave,vendedor_clave)
VALUES ('DEMO-PIN-OT-1',current_date - 1,900001,'DEMO · Aluminios del Sureste SA de CV','DEMO-OBRA-1','DEMO · Torre ficticia','DEMO · Pedido pendiente',16,'EMP-001','EMP-002')
ON CONFLICT (folio_orden) DO NOTHING;
INSERT INTO dbo.pinturas_pedido_linea (pedido_id,linea,servicio_clave,piezas,m2,precio_m2)
SELECT p.id, v.linea, v.servicio, v.piezas, v.m2, v.precio
FROM dbo.pinturas_pedido p
CROSS JOIN (VALUES (1,'DEMO-PIN-S1',12,80.5::numeric,85.25::numeric), (2,'DEMO-PIN-S2',6,45::numeric,92::numeric)) v(linea,servicio,piezas,m2,precio)
WHERE p.folio_orden = 'DEMO-PIN-OT-1'
ON CONFLICT (pedido_id, linea) DO NOTHING;
COMMIT;
