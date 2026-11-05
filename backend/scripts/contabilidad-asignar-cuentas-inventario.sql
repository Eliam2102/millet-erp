-- F1-ALM-13: asigna la cuenta real de inventario a los roles INVENTARIO_{grupo} que la migración Alm13InventarioPorGrupo dejó
-- sin cuenta porque el catálogo de Contabilidad aún no estaba cargado. La cuenta de cada rol es la que trae su descripción
-- «(115.02.01.00 MATERIA PRIMA - VIDRIO)», salvo aluminio, Alucom y Reynobond: Contabilidad (5-nov) los quiere en Accesorios
-- (115.07.02.00); empaque y embalaje, serigrafía, químicos, insumos y refacciones siguen en 115.02.03.00.
-- Correr DESPUÉS de importar el catálogo (Contabilidad > Catálogo > Importar).
-- Idempotente: toca roles sin cuenta y, solo para esos 3 grupos, los que quedaron en 115.02.03.00; la cuenta debe existir y estar
-- activa; deja renglón en la bitácora del rol. Una cuenta que Contabilidad cambió a mano no se pisa.
-- 115.05 (tránsito) no se asigna a ningún rol y el rol base INVENTARIO no se adivina: sigue en Roles de cuenta.
-- Uso: psql "$CONEXION" -f backend/scripts/contabilidad-asignar-cuentas-inventario.sql
WITH por_asignar AS (
    SELECT r.id, r.empresa_id, r.cuenta_id AS anterior, c.id AS cuenta_nueva
    FROM contabilidad.roles_cuenta r
    JOIN contabilidad.cuentas_contables c
      ON c.empresa_id = r.empresa_id AND c.deleted_at IS NULL
     AND c.codigo = CASE WHEN r.codigo IN ('INVENTARIO_ALUMINIO', 'INVENTARIO_ALUCOM', 'INVENTARIO_REYNOBON') THEN '115.07.02.00'
                         ELSE substring(r.descripcion FROM '\((\d{3}\.\d{2}\.\d{2}\.\d{2}) ') END
    WHERE r.codigo LIKE 'INVENTARIO\_%' AND r.deleted_at IS NULL
      AND (r.cuenta_id IS NULL
           OR (r.codigo IN ('INVENTARIO_ALUMINIO', 'INVENTARIO_ALUCOM', 'INVENTARIO_REYNOBON')
               AND r.cuenta_id IN (SELECT a.id FROM contabilidad.cuentas_contables a
                                   WHERE a.empresa_id = r.empresa_id AND a.codigo = '115.02.03.00')))
),
asignadas AS (
    UPDATE contabilidad.roles_cuenta r
       SET cuenta_id = p.cuenta_nueva, version = r.version + 1,
           updated_at = now() AT TIME ZONE 'UTC', updated_by = 'script-ALM13'
      FROM por_asignar p WHERE r.id = p.id
    RETURNING r.id, r.empresa_id, p.anterior, p.cuenta_nueva
)
INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, 'script-ALM13', now(),
       'F1-ALM-13: cuenta de inventario por tipo de artículo (Contabilidad, 4 y 5-nov)'
FROM asignadas;
