-- DEMO sintético · Pinturas: catálogos publicados por el ERP y pedidos del portal (dialecto Postgres del script
-- docs/operacion/aw-integracion-scripts/16_create_pinturas.sql). Idempotente: repetir la carga es inocuo.
-- psql -f pinturas.sql
BEGIN;
CREATE TABLE IF NOT EXISTS dbo.pinturas_cliente (
    clave varchar(50) PRIMARY KEY,
    razon_social varchar(200) NOT NULL,
    nombre_comercial varchar(200),
    telefono varchar(50),
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
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS dbo.pinturas_empleado (
    clave varchar(50) PRIMARY KEY,
    nombre varchar(200) NOT NULL,
    activo boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS dbo.pinturas_pedido (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    folio_orden varchar(50) NOT NULL UNIQUE,
    cliente_clave varchar(50) NOT NULL,
    cliente_nombre varchar(200) NOT NULL,
    obra_clave varchar(50),
    obra_nombre varchar(200),
    comentarios varchar(1000),
    servicio_clave varchar(50) NOT NULL,
    m2 numeric(18,6) NOT NULL CHECK (m2 > 0),
    precio_m2 numeric(18,6) NOT NULL CHECK (precio_m2 >= 0),
    iva_porcentaje numeric(5,2) NOT NULL CHECK (iva_porcentaje >= 0),
    moneda char(3) NOT NULL DEFAULT 'MXN',
    creado_at timestamptz NOT NULL DEFAULT now(),
    erp_resultado smallint CHECK (erp_resultado IN (1,2)),
    erp_pedido_id uuid,
    erp_error varchar(500),
    erp_procesado_at timestamptz
);
CREATE INDEX IF NOT EXISTS ix_pinturas_pedido_pendiente ON dbo.pinturas_pedido (creado_at, id) WHERE erp_resultado IS NULL;

INSERT INTO dbo.pinturas_cliente (clave,razon_social,nombre_comercial,telefono,email) VALUES
  ('DEMO-PIN-C1','DEMO · Aluminios del Sureste SA de CV','DEMO Aluminios','9990000001','demo1@example.com'),
  ('DEMO-PIN-C2','DEMO · Fachadas Peninsulares SA de CV',NULL,NULL,NULL)
ON CONFLICT (clave) DO NOTHING;
INSERT INTO dbo.pinturas_servicio (clave,nombre) VALUES
  ('DEMO-PIN-S1','DEMO · Pintura blanco mate'),
  ('DEMO-PIN-S2','DEMO · Pintura negro satinado'),
  ('DEMO-PIN-S3','DEMO · Pintura madera nogal')
ON CONFLICT (clave) DO NOTHING;
INSERT INTO dbo.pinturas_obra (clave,nombre) VALUES ('DEMO-OBRA-1','DEMO · Torre ficticia') ON CONFLICT (clave) DO NOTHING;
INSERT INTO dbo.pinturas_empleado (clave,nombre) VALUES ('DEMO-EMP-1','DEMO · Capturista de Pinturas') ON CONFLICT (clave) DO NOTHING;
-- Pedido pendiente (sin acuse del ERP).
INSERT INTO dbo.pinturas_pedido (folio_orden,cliente_clave,cliente_nombre,obra_clave,obra_nombre,comentarios,servicio_clave,m2,precio_m2,iva_porcentaje)
VALUES ('DEMO-PIN-OT-1','DEMO-PIN-C1','DEMO · Aluminios del Sureste SA de CV','DEMO-OBRA-1','DEMO · Torre ficticia','DEMO · Pedido pendiente','DEMO-PIN-S1',125.5,85.25,16)
ON CONFLICT (folio_orden) DO NOTHING;
COMMIT;
