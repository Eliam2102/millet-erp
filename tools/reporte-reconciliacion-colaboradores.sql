-- F1-ADM-01.4.1 F8 — reporte de solo lectura; no vincula ni elimina nada.
-- Ejecutar primero en QA y guardar conteos. F y G deben ser cero antes de
-- aplicar el índice único o dar por cerrada la conciliación.
BEGIN TRANSACTION READ ONLY;

WITH repeticiones AS (
    SELECT usuario_id, count(*) AS total
    FROM compartido.empleados
    WHERE usuario_id IS NOT NULL
    GROUP BY usuario_id
),
empleados_clasificados AS (
    SELECT e.id, e.clave, e.email, e.usuario_id,
        CASE
            WHEN e.usuario_id IS NOT NULL AND u.id IS NULL THEN 'F'
            WHEN e.usuario_id IS NOT NULL AND r.total > 1 THEN 'G'
            WHEN e.usuario_id IS NOT NULL THEN 'A'
            WHEN EXISTS (
                SELECT 1 FROM identidad.usuarios candidato
                WHERE lower(candidato.email) = lower(e.email)
                  AND candidato.es_cuenta_tecnica = false
                  AND NOT EXISTS (
                      SELECT 1 FROM compartido.empleados vinculado
                      WHERE vinculado.usuario_id = candidato.id
                  )
            ) THEN 'B'
            ELSE 'C'
        END AS categoria
    FROM compartido.empleados e
    LEFT JOIN identidad.usuarios u ON u.id = e.usuario_id
    LEFT JOIN repeticiones r ON r.usuario_id = e.usuario_id
),
usuarios_sin_empleado AS (
    SELECT u.id, CASE WHEN u.es_cuenta_tecnica THEN 'D' ELSE 'E' END AS categoria
    FROM identidad.usuarios u
    WHERE NOT EXISTS (
        SELECT 1 FROM compartido.empleados e WHERE e.usuario_id = u.id
    )
      AND (u.es_cuenta_tecnica OR NOT EXISTS (
        SELECT 1 FROM compartido.empleados e
        WHERE e.usuario_id IS NULL AND lower(e.email) = lower(u.email)
      ))
),
todos AS (
    SELECT categoria FROM empleados_clasificados
    UNION ALL
    SELECT categoria FROM usuarios_sin_empleado
)
SELECT categoria, count(*) AS total
FROM todos
GROUP BY categoria
ORDER BY categoria;

-- Detalle accionable de los casos que bloquean la reconciliación.
SELECT e.id AS empleado_id, e.clave, e.usuario_id,
       CASE WHEN u.id IS NULL THEN 'F' ELSE 'G' END AS categoria
FROM compartido.empleados e
LEFT JOIN identidad.usuarios u ON u.id = e.usuario_id
WHERE e.usuario_id IS NOT NULL
  AND (u.id IS NULL OR (
      SELECT count(*) FROM compartido.empleados e2
      WHERE e2.usuario_id = e.usuario_id
  ) > 1)
ORDER BY categoria, e.usuario_id, e.id;

COMMIT;
