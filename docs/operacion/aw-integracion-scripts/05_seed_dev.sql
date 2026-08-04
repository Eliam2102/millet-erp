/* =============================================================================
   05_seed_dev.sql — Solicitudes de prueba para el E2E "nuestro lado" (ADR-0048)
   Ejecutar SOLO en: MILLET_INTEGRACION_DEV. Idempotente (borra seeds previos).

   Decisión del owner: dev prueba DIRECTO CON DATOS REALES — las vistas de
   _DEV apuntan a MILMAIN real, así que los numero_pedido de abajo deben
   ser PEDIDOS REALES. Antes de ejecutar:

     1. Elegir 3-5 pedidos representativos (mostrador, exportación, obra,
        multi-línea, con descuento) consultando:
          SELECT TOP 20 numero_pedido, cliente_nombre, divisa, clase
            FROM dbo.vw_erp_pedido_cabecera ORDER BY fecha_transaccion DESC;
     2. Reemplazar los placeholders @PedidoN de la sección CONFIG.

   Escenarios que arma el seed (contra la matriz operación×estado del ERP):
     A. Alta simple                    → Aplicada, crea PedidoFacturable
     B. Alta + Modificación (v2)       → refresh de cabecera/líneas
     C. Alta duplicada (misma version) → idempotente, no reprocesa
     D. Modificación sin Alta previa   → excepción (bandeja)
     E. Alta + Cancelación (v2)        → pedido Cancelado
     F. Versiones fuera de orden       → el worker procesa en orden (v1→v2)
   ========================================================================== */

USE MILLET_INTEGRACION_DEV;
GO

/* ============================ CONFIG ===================================== */
DECLARE @Pedido1 nvarchar(50) = N'<<PEDIDO_REAL_1>>';  -- Alta simple (mostrador)
DECLARE @Pedido2 nvarchar(50) = N'<<PEDIDO_REAL_2>>';  -- Alta + Modificación
DECLARE @Pedido3 nvarchar(50) = N'<<PEDIDO_REAL_3>>';  -- Alta duplicada
DECLARE @Pedido4 nvarchar(50) = N'<<PEDIDO_REAL_4>>';  -- Modificación huérfana
DECLARE @Pedido5 nvarchar(50) = N'<<PEDIDO_REAL_5>>';  -- Alta + Cancelación
/* ========================================================================= */

IF EXISTS (SELECT 1 FROM (VALUES (@Pedido1),(@Pedido2),(@Pedido3),(@Pedido4),(@Pedido5)) v(p)
           WHERE v.p LIKE N'<<%')
BEGIN
    RAISERROR('Reemplaza los placeholders <<PEDIDO_REAL_N>> con pedidos reales antes de ejecutar.', 16, 1);
    RETURN;
END;

/* Idempotencia: limpiar seeds previos de estos pedidos. */
DELETE FROM dbo.aw_solicitud_pedido
 WHERE numero_pedido IN (@Pedido1, @Pedido2, @Pedido3, @Pedido4, @Pedido5);

/* A. Alta simple */
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido1, 1, 1);

/* B. Alta + Modificación — F: insertadas fuera de orden a propósito
   (v2 primero); el worker debe procesarlas en orden v1→v2. */
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido2, 2, 2);
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido2, 1, 1);

/* C. Alta duplicada: dos filas no se pueden con UNIQUE(pedido,version);
   la idempotencia real se prueba RE-EJECUTANDO este script tras el primer
   tick (misma version ya aplicada → el ERP responde Aplicada sin reprocesar).
   Aquí solo la Alta base: */
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido3, 1, 1);

/* D. Modificación sin Alta previa → excepción en bandeja. */
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido4, 2, 1);

/* E. Alta + Cancelación. */
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido5, 1, 1);
INSERT INTO dbo.aw_solicitud_pedido (numero_pedido, operacion, [version])
VALUES (@Pedido5, 3, 2);

SELECT numero_pedido, operacion, [version], resultado, creada_at
  FROM dbo.aw_solicitud_pedido
 WHERE numero_pedido IN (@Pedido1, @Pedido2, @Pedido3, @Pedido4, @Pedido5)
 ORDER BY numero_pedido, [version];
GO

/* Verificación post-tick (tras correr AwSolicitudesWorker en dev):
   SELECT numero_pedido, operacion, [version], erp_pedido_id,
          estado_facturacion, resultado, motivo, procesada_at
     FROM dbo.aw_solicitud_pedido ORDER BY numero_pedido, [version];
   Esperado: A/B/C/E → resultado=1 (Aplicada) con claim; D → resultado=2
   (Rechazada) + motivo; E v2 → estado_facturacion='Cancelado'. */
