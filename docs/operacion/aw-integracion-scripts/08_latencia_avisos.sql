/* =============================================================================
   08_latencia_avisos.sql — Medición de latencia del aviso de pedidos (U1.4)
   Ejecutar en: MILLET_INTEGRACION (o _DEV). Solo lectura. SQL Server 2016+.

   Meta (Módulo 13, R14): de que el pedido pasa a 15/42 en A+W a que el ERP
   lo procesa, MENOS DE 1 MINUTO (hoy con SAP: ciclos de 6–7 s + 3 s).

   Tramos que mide, por aviso:
     A+W → cola   evento en BW_AUFTR_HIST  → creada_at   (job o customizing)
     cola → ERP   creada_at                → procesada_at (AwSolicitudesWorker)
     total        evento en BW_AUFTR_HIST  → procesada_at

   El tramo A+W → cola solo existe para avisos del job (07), que guarda el
   evento en dbo.aw_aviso_job_pedido; con customizing el total ≈ cola → ERP.
   El worker además deja en el log, por aviso, «latencia cola→ERP N ms».

   Parámetros: ventana de medición. Para el cierre de U1.4: 20 pedidos.
   ========================================================================== */

USE MILLET_INTEGRACION;  -- o MILLET_INTEGRACION_DEV
GO

DECLARE @desde datetimeoffset(3) = DATEADD(hour, -24, SYSDATETIMEOFFSET());
DECLARE @ultimos int = 20;

WITH avisos AS (
    SELECT TOP (@ultimos)
           S.numero_pedido, S.operacion, S.[version], S.resultado,
           S.creada_at, S.procesada_at,
           -- el evento solo es exacto para la última versión avisada por el job
           CASE WHEN J.ultima_version = S.[version] THEN J.ultimo_evento END AS evento_aw
      FROM dbo.aw_solicitud_pedido AS S
      LEFT JOIN dbo.aw_aviso_job_pedido AS J ON J.numero_pedido = S.numero_pedido
     WHERE S.creada_at >= @desde
       AND S.procesada_at IS NOT NULL
     ORDER BY S.creada_at DESC
)
SELECT numero_pedido, operacion, [version], resultado,
       DATEDIFF(millisecond,
                TODATETIMEOFFSET(evento_aw, DATEPART(tz, creada_at)), creada_at) / 1000.0 AS aw_a_cola_s,
       DATEDIFF(millisecond, creada_at, procesada_at) / 1000.0                     AS cola_a_erp_s,
       DATEDIFF(millisecond,
                COALESCE(TODATETIMEOFFSET(evento_aw, DATEPART(tz, creada_at)), creada_at),
                procesada_at) / 1000.0                                             AS total_s
  INTO #lat
  FROM avisos;

SELECT * FROM #lat ORDER BY total_s DESC;

SELECT COUNT(*)                     AS avisos,
       CAST(AVG(total_s) AS decimal(9, 1)) AS promedio_total_s,
       MAX(total_s)                 AS maximo_total_s,
       CAST(AVG(cola_a_erp_s) AS decimal(9, 1)) AS promedio_cola_a_erp_s,
       MAX(cola_a_erp_s)            AS maximo_cola_a_erp_s,
       SUM(CASE WHEN total_s >= 60 THEN 1 ELSE 0 END) AS fuera_de_meta
  FROM #lat;

DROP TABLE #lat;
GO
