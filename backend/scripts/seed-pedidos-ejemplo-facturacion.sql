-- ============================================================================
-- Seed de pedidos facturables de ejemplo (dev) — módulo Facturación.
--
-- Crea 4 PedidoFacturable en estado Importado (listos para facturar desde el
-- frontend / POST /facturas con pedidoFacturableId). Idempotente: si los pedidos
-- de ejemplo ya existen (y no han sido facturados) los regenera.
--
-- Resuelve empresa_id y sucursal_id automáticamente de la BD dev (creados por el
-- BootstrapSuperAdminHostedService al primer arranque del backend). NO hardcodea
-- IDs.
--
-- Uso (PostgreSQL local, p.ej. instalación nativa en :5432):
--   $env:PGPASSWORD="pgadmin"
--   & "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h localhost -p 5432 -U pgadmin `
--     -d millet_dev -f backend/scripts/seed-pedidos-ejemplo-facturacion.sql
--   (ajusta la ruta de psql.exe a tu versión de PostgreSQL)
--
-- Enums (short): origen Aw=1; canal_venta TiendaCancun=1, TiendaCircuito=2,
--   ProyectosYObras=7; comportamiento_fiscal MostradorInmediato=1, ConAnticipo=2;
--   estado Importado=1.
-- ============================================================================
DO $$
DECLARE
  v_empresa  uuid;
  v_sucursal uuid;
  v_now      timestamptz := now();
  v_pedido   uuid;
  v_cliente  uuid;
BEGIN
  -- Empresa dev del bootstrap (RFC fijo); fallback a la más antigua.
  SELECT id INTO v_empresa FROM compartido.empresas WHERE rfc = 'MID010101AAA' LIMIT 1;
  IF v_empresa IS NULL THEN
    SELECT id INTO v_empresa FROM compartido.empresas ORDER BY created_at LIMIT 1;
  END IF;
  IF v_empresa IS NULL THEN
    RAISE EXCEPTION 'No hay empresa dev. Arranca el backend una vez (BootstrapSuperAdminHostedService la crea) y reintenta.';
  END IF;

  -- Sucursal: catálogo global (sin FK cross-schema desde facturacion); cualquiera sirve.
  SELECT id INTO v_sucursal FROM compartido.sucursales LIMIT 1;
  IF v_sucursal IS NULL THEN
    v_sucursal := gen_random_uuid();
  END IF;

  -- Limpia ejemplos previos NO facturados (idempotencia). Las líneas caen por FK cascade.
  DELETE FROM facturacion.pedido_facturable
   WHERE empresa_id = v_empresa
     AND numero_pedido IN ('PED-1001','PED-1002','PED-1003','PED-1004')
     AND estado <> 3;  -- 3 = Facturado: respeta los ya facturados

  -- ---- Helper inline por pedido: se repite el patrón insert cabecera + líneas ----

  -- 1) PED-1001 — Mostrador inmediato, Tienda Cancún, 2 líneas. Total 4640.
  v_pedido := gen_random_uuid(); v_cliente := gen_random_uuid();
  INSERT INTO facturacion.pedido_facturable
    (id, empresa_id, origen, numero_pedido, sucursal_id, cliente_id, cliente_nombre,
     canal_venta, comportamiento_fiscal, moneda, obra_id, obra_nombre, total, estado,
     comprobante_vigente_id, capturado_por, comentarios, version_origen, estado_origen,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (v_pedido, v_empresa, 1, 'PED-1001', v_sucursal, v_cliente, 'Vidrios del Mayab SA de CV',
     1, 1, 'MXN', NULL, NULL, 4640, 1, NULL, NULL, 'Entregar en mostrador', 1, '15',
     1, v_now, v_now, 'seed', 'seed', NULL);
  INSERT INTO facturacion.pedido_facturable_linea
    (id, pedido_facturable_id, posicion, producto_id, producto_descripcion, clave_prod_serv_sat,
     clave_unidad_sat, cantidad, precio, descuento, importe, requiere_pedimento,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (gen_random_uuid(), v_pedido, 1, NULL, 'Vidrio templado 6mm', '39121600', 'MTK', 8, 450, 0, 3600, false, 1, v_now, v_now, 'seed', 'seed', NULL),
    (gen_random_uuid(), v_pedido, 2, NULL, 'Canto pulido recto',  '39121600', 'MTR', 16, 65, 0, 1040, false, 1, v_now, v_now, 'seed', 'seed', NULL);

  -- 2) PED-1002 — Proyectos y Obras, con obra, 1 línea grande. Total 87000.
  v_pedido := gen_random_uuid(); v_cliente := gen_random_uuid();
  INSERT INTO facturacion.pedido_facturable
    (id, empresa_id, origen, numero_pedido, sucursal_id, cliente_id, cliente_nombre,
     canal_venta, comportamiento_fiscal, moneda, obra_id, obra_nombre, total, estado,
     comprobante_vigente_id, capturado_por, comentarios, version_origen, estado_origen,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (v_pedido, v_empresa, 1, 'PED-1002', v_sucursal, v_cliente, 'Constructora Riviera SA de CV',
     7, 1, 'MXN', 5001, 'Torre Cancún - Fase 1', 87000, 1, NULL, NULL, NULL, 1, '15',
     1, v_now, v_now, 'seed', 'seed', NULL);
  INSERT INTO facturacion.pedido_facturable_linea
    (id, pedido_facturable_id, posicion, producto_id, producto_descripcion, clave_prod_serv_sat,
     clave_unidad_sat, cantidad, precio, descuento, importe, requiere_pedimento,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (gen_random_uuid(), v_pedido, 1, NULL, 'Cancelería de aluminio serie 3', '30151700', 'MTK', 120, 725, 0, 87000, false, 1, v_now, v_now, 'seed', 'seed', NULL);

  -- 3) PED-1003 — Con anticipo (maquila), Proyectos y Obras, 1 línea. Total 150000.
  v_pedido := gen_random_uuid(); v_cliente := gen_random_uuid();
  INSERT INTO facturacion.pedido_facturable
    (id, empresa_id, origen, numero_pedido, sucursal_id, cliente_id, cliente_nombre,
     canal_venta, comportamiento_fiscal, moneda, obra_id, obra_nombre, total, estado,
     comprobante_vigente_id, capturado_por, comentarios, version_origen, estado_origen,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (v_pedido, v_empresa, 1, 'PED-1003', v_sucursal, v_cliente, 'Hotel Maya Resort SA de CV',
     7, 2, 'MXN', 5002, 'Lobby Hotel Maya', 150000, 1, NULL, NULL, 'Requiere anticipo 30%', 1, '15',
     1, v_now, v_now, 'seed', 'seed', NULL);
  INSERT INTO facturacion.pedido_facturable_linea
    (id, pedido_facturable_id, posicion, producto_id, producto_descripcion, clave_prod_serv_sat,
     clave_unidad_sat, cantidad, precio, descuento, importe, requiere_pedimento,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (gen_random_uuid(), v_pedido, 1, NULL, 'Muro cortina templado', '30151700', 'MTK', 200, 750, 0, 150000, false, 1, v_now, v_now, 'seed', 'seed', NULL);

  -- 4) PED-1004 — Mostrador, Tienda Circuito, 3 líneas. Total 5180.
  v_pedido := gen_random_uuid(); v_cliente := gen_random_uuid();
  INSERT INTO facturacion.pedido_facturable
    (id, empresa_id, origen, numero_pedido, sucursal_id, cliente_id, cliente_nombre,
     canal_venta, comportamiento_fiscal, moneda, obra_id, obra_nombre, total, estado,
     comprobante_vigente_id, capturado_por, comentarios, version_origen, estado_origen,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (v_pedido, v_empresa, 1, 'PED-1004', v_sucursal, v_cliente, 'Aluminios del Sureste SA de CV',
     2, 1, 'MXN', NULL, NULL, 5180, 1, NULL, NULL, NULL, 1, '15',
     1, v_now, v_now, 'seed', 'seed', NULL);
  INSERT INTO facturacion.pedido_facturable_linea
    (id, pedido_facturable_id, posicion, producto_id, producto_descripcion, clave_prod_serv_sat,
     clave_unidad_sat, cantidad, precio, descuento, importe, requiere_pedimento,
     version, created_at, updated_at, created_by, updated_by, deleted_at)
  VALUES
    (gen_random_uuid(), v_pedido, 1, NULL, 'Espejo plata 4mm',        '39121600', 'MTK', 5, 380, 0, 1900, false, 1, v_now, v_now, 'seed', 'seed', NULL),
    (gen_random_uuid(), v_pedido, 2, NULL, 'Herrajes acero inox',     '31162800', 'H87', 12, 140, 0, 1680, false, 1, v_now, v_now, 'seed', 'seed', NULL),
    (gen_random_uuid(), v_pedido, 3, NULL, 'Perfil de aluminio natural','30151700','MTR', 20, 80, 0, 1600, false, 1, v_now, v_now, 'seed', 'seed', NULL);

  RAISE NOTICE 'Seed OK — 4 pedidos Importados (PED-1001..1004) para empresa %, sucursal %', v_empresa, v_sucursal;
END $$;

-- Verificación rápida:
SELECT numero_pedido, cliente_nombre, total, estado
  FROM facturacion.pedido_facturable
 WHERE numero_pedido LIKE 'PED-100%'
 ORDER BY numero_pedido;
