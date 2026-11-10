/* =============================================================================
   14b_seed_aw_retorno_demo.sql — Filas de prueba para el servicio de TI en
   MILLET_INTEGRACION_DEV (aw_retorno v4). Versión 3 (10-nov-2026).

   SOLO AGREGA, NUNCA BORRA. Se puede correr con el servicio encendido y
   repetir sin efecto: un pedido que ya tiene cualquier fila en aw_retorno
   (de esta semilla, de la anterior o escrita por el ERP) no se toca. Así no
   se reaplican pagos ni estatus que el servicio ya aplicó (advertencia de
   Jorge Toache, 9-nov).

   Pedidos: los 18 que eligió TI el 9-nov (dos por serie, los últimos de cada
   una en MIL_DEMO_ERP). Importes de BW_AUFTR_KOPF.FI_BETR_BRUTTO al 10-nov.
   Forma de pago y moneda con los valores de A+W (KA_ZAHLWEG.BEZ y
   KA_WAEHRUNGEN.WAEHRUNG). Importe de pago = acumulado por folio + forma +
   moneda, como lo manda el ERP.

   Qué cubre (lo que pidió TI: todos los tipos de fila, NotaCredito y desfasada):
     Serie 10  10445018  Factura + pago EFECTIVO total + saldo 0
               10445017  Factura con version_pedido = 0 -> se espera resultado 3 (desfasada)
     Serie 19  19000809  (EDI, USD) Factura + pago parcial TRANSFERENCIA USD + saldo
               19000808  (EDI, USD) Factura a crédito + saldo
     Serie 20  20429659  Factura + pago TARJETA CREDITO + Cancelacion de la factura
               20429655  Factura + pago CHEQUE total + saldo 0
     Serie 40  40273706  Factura + pago EFECTIVO + estatus 115
               40273705  Factura + pago POR DEFINIR
     Serie 50  50035840  Factura + dos pagos TRANSFERENCIA acumulados + saldo 0 + estatus 115
               50035838  Factura + NotaCredito 986.00 (ranura) + saldo
     Serie 55  55000092  (ya en 70 en A+W) pago TRANSFERENCIA USD + liberación 70 manual
               55000086  (ya en 70 en A+W) Factura USD + estatus 115
     Serie 56  56000606  (REPARTO, USD) Factura + saldo
               56000605  (REPARTO, USD) Factura + NotaCredito USD + saldo
     Serie 79  79013570  (importe 0.00) liberación 70 manual
               79013569  (importe 0.00) estatus 115
     Serie 80  80273727  (estatus 10) liberación 70 manual
               80273726  ya tiene filas de la semilla v1: no se toca
   No va la liberación 70 automática (origen 1): la decide el servicio de TI.

   Ejecución: en MILLET_INTEGRACION_DEV con un usuario con INSERT (el del ERP
   sirve). Consultas de seguimiento al final.
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

DECLARE @u nvarchar(100) = N'cobranza.prueba@millet.com.mx';

DECLARE @f TABLE (
    numero_pedido nvarchar(50), secuencia int, tipo tinyint, version_pedido bigint,
    estatus_destino smallint NULL, origen tinyint NULL, usuario_libera nvarchar(100) NULL,
    anticipos_aplicados decimal(18,2) NULL, saldo decimal(18,2) NULL,
    tipo_documento nvarchar(20) NULL, folio nvarchar(30) NULL, [uuid] nvarchar(36) NULL, importe decimal(18,2) NULL,
    forma_pago nvarchar(40) NULL, moneda nvarchar(8) NULL);

--          pedido      sec tipo ver  estatus orig usuario  antic  saldo       tipo_doc        folio            uuid                                    importe     forma               moneda
INSERT INTO @f VALUES
-- Serie 10
 (N'10445018', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCNK-10445018', N'7E5B0001-1044-5018-A001-000000000001', 5537.22,    NULL,               NULL),
 (N'10445018', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCNK-10445018', NULL,                                    5537.22,    N'EFECTIVO',        N'PESOSMX'),
 (N'10445018', 3, 2, 1, NULL, NULL, NULL, 0.00, 0.00,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'10445017', 1, 3, 0, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCNK-10445017', N'7E5B0002-1044-5017-A001-000000000002', 602.60,     NULL,               NULL),
-- Serie 19 (EDI, USD)
 (N'19000809', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FEXP-19000809', N'7E5B0003-1900-0809-A001-000000000003', 4398.74,    NULL,               NULL),
 (N'19000809', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FEXP-19000809', NULL,                                    2000.00,    N'TRANSFERENCIA',   N'USD'),
 (N'19000809', 3, 2, 1, NULL, NULL, NULL, 0.00, 2398.74,    NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'19000808', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FEXP-19000808', N'7E5B0004-1900-0808-A001-000000000004', 33859.92,   NULL,               NULL),
 (N'19000808', 2, 2, 1, NULL, NULL, NULL, 0.00, 33859.92,   NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 20
 (N'20429659', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCIR-20429659', N'7E5B0005-2042-9659-A001-000000000005', 534.71,     NULL,               NULL),
 (N'20429659', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCIR-20429659', NULL,                                    534.71,     N'TARJETA CREDITO', N'PESOSMX'),
 (N'20429659', 3, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'Cancelacion',  N'FCIR-20429659', N'7E5B0005-2042-9659-A001-000000000005', 534.71,     NULL,               NULL),
 (N'20429655', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCIR-20429655', N'7E5B0006-2042-9655-A001-000000000006', 2214.94,    NULL,               NULL),
 (N'20429655', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCIR-20429655', NULL,                                    2214.94,    N'CHEQUE',          N'PESOSMX'),
 (N'20429655', 3, 2, 1, NULL, NULL, NULL, 0.00, 0.00,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 40
 (N'40273706', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCUN-40273706', N'7E5B0007-4027-3706-A001-000000000007', 285.56,     NULL,               NULL),
 (N'40273706', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCUN-40273706', NULL,                                    285.56,     N'EFECTIVO',        N'PESOSMX'),
 (N'40273706', 3, 1, 1, 115,  NULL, NULL, NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'40273705', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCUN-40273705', N'7E5B0008-4027-3705-A001-000000000008', 3359.90,    NULL,               NULL),
 (N'40273705', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCUN-40273705', NULL,                                    3359.90,    N'POR DEFINIR',     N'PESOSMX'),
-- Serie 50
 (N'50035840', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCNK-50035840', N'7E5B0009-5003-5840-A001-000000000009', 9758.34,    NULL,               NULL),
 (N'50035840', 2, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCNK-50035840', NULL,                                    4000.00,    N'TRANSFERENCIA',   N'PESOSMX'),
 (N'50035840', 3, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FCNK-50035840', NULL,                                    9758.34,    N'TRANSFERENCIA',   N'PESOSMX'),
 (N'50035840', 4, 2, 1, NULL, NULL, NULL, 0.00, 0.00,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'50035840', 5, 1, 1, 115,  NULL, NULL, NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'50035838', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FCNK-50035838', N'7E5B0010-5003-5838-A001-000000000010', 165169.67,  NULL,               NULL),
 (N'50035838', 2, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'NotaCredito',  N'NCRED-50035838',N'7E5B0010-5003-5838-B001-000000000010', 986.00,     NULL,               NULL),
 (N'50035838', 3, 2, 1, NULL, NULL, NULL, 0.00, 164183.67,  NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 55 (USD, ya en 70 en A+W)
 (N'55000092', 1, 4, 1, NULL, NULL, NULL, NULL, NULL,       NULL,            N'FEXP-55000092', NULL,                                    53304.76,   N'TRANSFERENCIA',   N'USD'),
 (N'55000092', 2, 1, 1, 70,   2,    @u,   NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'55000086', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FEXP-55000086', N'7E5B0011-5500-0086-A001-000000000011', 14385.72,   NULL,               NULL),
 (N'55000086', 2, 1, 1, 115,  NULL, NULL, NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 56 (REPARTO, USD)
 (N'56000606', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FEXP-56000606', N'7E5B0012-5600-0606-A001-000000000012', 1624.74,    NULL,               NULL),
 (N'56000606', 2, 2, 1, NULL, NULL, NULL, 0.00, 1624.74,    NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'56000605', 1, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'FacturaFinal', N'FEXP-56000605', N'7E5B0013-5600-0605-A001-000000000013', 454.23,     NULL,               NULL),
 (N'56000605', 2, 3, 1, NULL, NULL, NULL, NULL, NULL,       N'NotaCredito',  N'NCRED-56000605',N'7E5B0013-5600-0605-B001-000000000013', 54.23,      NULL,               NULL),
 (N'56000605', 3, 2, 1, NULL, NULL, NULL, 0.00, 400.00,     NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 79 (importe 0.00)
 (N'79013570', 1, 1, 1, 70,   2,    @u,   NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
 (N'79013569', 1, 1, 1, 115,  NULL, NULL, NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL),
-- Serie 80
 (N'80273727', 1, 1, 1, 70,   2,    @u,   NULL, NULL,       NULL,            NULL,             NULL,                                    NULL,       NULL,               NULL);

-- Solo pedidos que todavía no tienen ninguna fila: nunca se pisa ni se reinserta lo que el servicio ya vio.
INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, estatus_destino, origen, usuario_libera,
                            anticipos_aplicados, saldo, tipo_documento, folio, [uuid], importe, forma_pago, moneda)
SELECT f.numero_pedido, f.secuencia, f.tipo, f.version_pedido, f.estatus_destino, f.origen, f.usuario_libera,
       f.anticipos_aplicados, f.saldo, f.tipo_documento, f.folio, f.[uuid], f.importe, f.forma_pago, f.moneda
  FROM @f f
 WHERE NOT EXISTS (SELECT 1 FROM dbo.aw_retorno r WHERE r.numero_pedido = f.numero_pedido);

SELECT @@ROWCOUNT AS filas_agregadas;
GO

/* Seguimiento: el servicio de TI llena aplicada_at, resultado (1 aplicada,
   2 rechazada, 3 desfasada), motivo e intentos. */
SELECT numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, importe,
       forma_pago, moneda, estatus_destino, origen, usuario_libera, saldo,
       aplicada_at, resultado, intentos, motivo
  FROM dbo.aw_retorno
 WHERE numero_pedido IN ('10445018','10445017','19000809','19000808','20429659','20429655','40273706','40273705',
                         '50035840','50035838','55000092','55000086','56000606','56000605','79013570','79013569',
                         '80273727','80273726')
 ORDER BY numero_pedido, secuencia;
