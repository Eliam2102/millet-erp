/* =============================================================================
   14b_seed_aw_retorno_demo.sql — Semilla de pedidos DEMO para el servicio de TI
   en MILLET_INTEGRACION_DEV (aw_retorno v4). Idempotente.

   Propósito:
     Poblar en dbo.aw_retorno (MILLET_INTEGRACION_DEV) pedidos reales de DEMO
     (MIL_DEMO_ERP.SYSADM.BW_AUFTR_KOPF) para que el servicio de Windows de TI
     (que corre cada 30 segundos) pruebe los tres escenarios de retorno a A+W:
       1) Pedido 80273726 (2,140.90 MXN):
          - FacturaFinal (sec 1)
          - Pago parcial 1 de 1,000.00 MXN (sec 2, TRANSFERENCIA, PESOSMX)
          - Pago parcial 2 acumulado de 2,140.90 MXN (sec 3, TRANSFERENCIA, PESOSMX)
          - Liberación automática estatus 70 por saldo $0 (sec 4, origen 1)
          - Fila de saldo de control en $0.00 (sec 5)
       2) Pedido 80273725 (429.97 MXN):
          - Anticipo aplicado de 200.00 MXN y saldo de 229.97 MXN (sec 1)
       3) Pedido 80273722 (4,518.87 MXN):
          - FacturaFinal (sec 1)
          - Cancelación informativa de la factura (sec 2)

   Ejecución:
     Ejecutar en MILLET_INTEGRACION_DEV con usuario con permisos de INSERT o SA.
     Limpieza / rollback al final del archivo.
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;
GO

SET NOCOUNT ON;

-- Validar que la tabla exista
IF OBJECT_ID('dbo.aw_retorno', 'U') IS NULL
BEGIN
    RAISERROR('La tabla dbo.aw_retorno no existe. Ejecuta primero 14_create_aw_retorno.sql.', 16, 1);
    RETURN;
END;
GO

BEGIN TRANSACTION;

-- Limpieza previa de los pedidos demo para asegurar idempotencia
DELETE FROM dbo.aw_retorno
WHERE numero_pedido IN ('80273726', '80273725', '80273722');

/* -----------------------------------------------------------------------------
   Caso 1: Pedido 80273726 (Total 2,140.90 MXN)
   Escenario: Factura con dos pagos hasta liquidar saldo y liberación automática 70.
   ----------------------------------------------------------------------------- */

-- 1. Factura final
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('80273726', 1, 3, 1, 'FacturaFinal', 'FTR-80273726', 'B8E30001-8027-3726-A001-000000000001', 2140.90);

-- 2. Primer pago parcial (1,000.00 MXN)
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('80273726', 2, 4, 1, 'FTR-80273726', 1000.00, 'TRANSFERENCIA', 'PESOSMX');

-- 3. Segundo pago parcial acumulado (2,140.90 MXN - liquida el total)
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('80273726', 3, 4, 1, 'FTR-80273726', 2140.90, 'TRANSFERENCIA', 'PESOSMX');

-- 4. Liberación 70 por saldo $0 (origen 1 = automático)
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, estatus_destino, origen)
VALUES ('80273726', 4, 1, 1, 70, 1);

-- 5. Saldo de control en cero
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, anticipos_aplicados, saldo)
VALUES ('80273726', 5, 2, 1, 0.00, 0.00);

/* -----------------------------------------------------------------------------
   Caso 2: Pedido 80273725 (Total 429.97 MXN)
   Escenario: Saldo y anticipo aplicado (control de saldo).
   ----------------------------------------------------------------------------- */
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, anticipos_aplicados, saldo)
VALUES ('80273725', 1, 2, 1, 200.00, 229.97);

/* -----------------------------------------------------------------------------
   Caso 3: Pedido 80273722 (Total 4,518.87 MXN)
   Escenario: Factura timbrada y posteriormente cancelada (fila informativa nueva).
   ----------------------------------------------------------------------------- */

-- 1. Factura emitida
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('80273722', 1, 3, 1, 'FacturaFinal', 'FTR-80273722', 'B8E30003-8027-3722-C001-000000000003', 4518.87);

-- 2. Cancelación informativa
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('80273722', 2, 3, 1, 'Cancelacion', 'FTR-80273722', 'B8E30003-8027-3722-C001-000000000003', 4518.87);

COMMIT TRANSACTION;
GO

/* =============================================================================
   CONSULTAS DE SEGUIMIENTO PARA TI / OPERACIÓN:
   El servicio de Windows de TI toma las filas no aplicadas (resultado IS NULL
   o diferente de 1 y 3) ordenadas por (numero_pedido, secuencia) y actualiza
   aplicada_at, resultado (1=aplicada, 2=rechazada, 3=desfasada), motivo e intentos.
   ========================================================================== */

-- Ver estado actual de procesamiento de las filas demo:
SELECT 
    retorno_id,
    numero_pedido,
    secuencia,
    tipo,
    tipo_documento,
    folio,
    importe,
    forma_pago,
    moneda,
    estatus_destino,
    origen,
    saldo,
    aplicada_at,
    resultado,
    intentos,
    motivo
FROM dbo.aw_retorno
WHERE numero_pedido IN ('80273726', '80273725', '80273722')
ORDER BY numero_pedido, secuencia;

/* =============================================================================
   ROLLBACK / LIMPIEZA (Opcional):
   DELETE FROM dbo.aw_retorno WHERE numero_pedido IN ('80273726', '80273725', '80273722');
   ========================================================================== */
