/* =============================================================================
   14b_seed_aw_retorno_demo.sql — Semilla de filas de prueba para el servicio de TI
   en MILLET_INTEGRACION_DEV (aw_retorno v4). Versión 2 (10-nov-2026).

   Cambios contra la versión 1 (respuesta de Jorge Toache, 9-nov):
     - Pedidos reales de MIL_DEMO_ERP que sí se facturan (la serie 90 no se factura).
       Se usan los que eligió TI: últimos creados por serie.
     - Se agrega una fila NotaCredito (tipo_documento = 'NotaCredito').
     - Se agrega el camino de desfasada: una fila con version_pedido = 0, menor que
       cualquier versión que el pedido tenga en A+W (empiezan en 1), para que el
       servicio la marque resultado = 3.
     - Pedido 55000092 ya está en estatus 70 en A+W: se manda su liberación 70
       manual para ver qué contesta el servicio cuando la fila llega tarde.
     - La liberación 70 automática (origen = 1) NO va: la decide el servicio de TI.

   Casos (importes de BW_AUFTR_KOPF.FI_BETR_BRUTTO en MIL_DEMO_ERP, 10-nov):
     1) 10445018 · CONTADO · 5,537.22 PESOSMX
          FacturaFinal · Pago EFECTIVO por el total · Saldo 0
     2) 50035840 · 30 DIAS · 9,758.34 PESOSMX
          FacturaFinal · Pago parcial TRANSFERENCIA 4,000.00 · Pago acumulado 9,758.34 ·
          Saldo 0 · Estatus 115
     3) 50035838 · 30 DIAS · 165,169.67 PESOSMX
          FacturaFinal · NotaCredito 986.00 (ranura) · Saldo 164,183.67
     4) 20429659 · CONTADO · 534.71 PESOSMX
          FacturaFinal · Pago TARJETA CREDITO 534.71 · Cancelacion de la factura
     5) 55000092 · CONTADO · 53,304.76 USD · ya en 70 en A+W
          Pago TRANSFERENCIA en USD por el total · Estatus 70 manual (origen 2, con usuario)
     6) 80273727 · CONTADO · 2,198.17 PESOSMX · estatus 10
          Estatus 70 manual (origen 2, con usuario)
     7) 10445017 · CONTADO · 602.60 PESOSMX
          FacturaFinal con version_pedido = 0  -> se espera resultado = 3 (desfasada)

   Forma de pago y moneda con los valores de A+W (KA_ZAHLWEG.BEZ y
   KA_WAEHRUNGEN.WAEHRUNG). Importe de pago = acumulado del grupo folio +
   forma_pago + moneda, como lo manda el ERP.

   IMPORTANTE (Jorge, 9-nov): NO volver a correr este script con el servicio
   encendido. Al repetirlo se borran filas ya aplicadas y se reinsertan como
   pendientes: el servicio las aplicaría otra vez (pagos duplicados y estatus
   reaplicados en A+W). Para resembrar, primero se detiene el servicio.

   Ejecución: en MILLET_INTEGRACION_DEV con un usuario que pueda borrar (el del
   ERP no puede: solo inserta). Consultas de seguimiento al final.
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.aw_retorno', 'U') IS NULL
BEGIN
    RAISERROR('La tabla dbo.aw_retorno no existe. Ejecuta primero 14_create_aw_retorno.sql.', 16, 1);
    RETURN;
END;
GO

BEGIN TRANSACTION;

-- Limpieza: pedidos de la versión 1 de esta semilla y los de esta versión.
DELETE FROM dbo.aw_retorno
WHERE numero_pedido IN (
    '80273726', '80273725', '80273722', '90047568', '90046743', '90046283',
    '10445018', '50035840', '50035838', '20429659', '55000092', '80273727', '10445017');

-- 1) 10445018 · contado pagado en efectivo
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('10445018', 1, 3, 1, N'FacturaFinal', N'FCNK-10445018', N'7E5B0001-1044-5018-A001-000000000001', 5537.22);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('10445018', 2, 4, 1, N'FCNK-10445018', 5537.22, N'EFECTIVO', N'PESOSMX');
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, anticipos_aplicados, saldo)
VALUES ('10445018', 3, 2, 1, 0.00, 0.00);

-- 2) 50035840 · crédito con dos pagos acumulados y estatus 115
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('50035840', 1, 3, 1, N'FacturaFinal', N'FCNK-50035840', N'7E5B0002-5003-5840-A001-000000000002', 9758.34);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('50035840', 2, 4, 1, N'FCNK-50035840', 4000.00, N'TRANSFERENCIA', N'PESOSMX');
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('50035840', 3, 4, 1, N'FCNK-50035840', 9758.34, N'TRANSFERENCIA', N'PESOSMX');
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, anticipos_aplicados, saldo)
VALUES ('50035840', 4, 2, 1, 0.00, 0.00);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, estatus_destino)
VALUES ('50035840', 5, 1, 1, 115);

-- 3) 50035838 · crédito con nota de crédito por ranura
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('50035838', 1, 3, 1, N'FacturaFinal', N'FCNK-50035838', N'7E5B0003-5003-5838-A001-000000000003', 165169.67);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('50035838', 2, 3, 1, N'NotaCredito', N'NCRED-50035838', N'7E5B0003-5003-5838-B001-000000000003', 986.00);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, anticipos_aplicados, saldo)
VALUES ('50035838', 3, 2, 1, 0.00, 164183.67);

-- 4) 20429659 · contado con tarjeta y factura cancelada después
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('20429659', 1, 3, 1, N'FacturaFinal', N'FCIR-20429659', N'7E5B0004-2042-9659-A001-000000000004', 534.71);
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('20429659', 2, 4, 1, N'FCIR-20429659', 534.71, N'TARJETA CREDITO', N'PESOSMX');
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('20429659', 3, 3, 1, N'Cancelacion', N'FCIR-20429659', N'7E5B0004-2042-9659-A001-000000000004', 534.71);

-- 5) 55000092 · USD, ya en 70 en A+W: pago y liberación manual que llegan tarde
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, folio, importe, forma_pago, moneda)
VALUES ('55000092', 1, 4, 1, N'FEXP-55000092', 53304.76, N'TRANSFERENCIA', N'USD');
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, estatus_destino, origen, usuario_libera)
VALUES ('55000092', 2, 1, 1, 70, 2, N'cobranza.prueba@millet.com.mx');

-- 6) 80273727 · liberación 70 manual autorizada por Cobranza
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, estatus_destino, origen, usuario_libera)
VALUES ('80273727', 1, 1, 1, 70, 2, N'cobranza.prueba@millet.com.mx');

-- 7) 10445017 · desfasada: versión 0, menor que la que el pedido tiene en A+W
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, [uuid], importe)
VALUES ('10445017', 1, 3, 0, N'FacturaFinal', N'FCNK-10445017', N'7E5B0007-1044-5017-A001-000000000007', 602.60);

COMMIT TRANSACTION;
GO

/* Seguimiento: el servicio de TI llena aplicada_at, resultado (1 aplicada,
   2 rechazada, 3 desfasada), motivo e intentos. */
SELECT numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, importe,
       forma_pago, moneda, estatus_destino, origen, usuario_libera, saldo,
       aplicada_at, resultado, intentos, motivo
FROM dbo.aw_retorno
WHERE numero_pedido IN ('10445018', '50035840', '50035838', '20429659', '55000092', '80273727', '10445017')
ORDER BY numero_pedido, secuencia;

/* Limpieza (solo con el servicio detenido):
   DELETE FROM dbo.aw_retorno
   WHERE numero_pedido IN ('10445018', '50035840', '50035838', '20429659', '55000092', '80273727', '10445017');
*/
