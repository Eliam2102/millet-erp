using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// Cierre definitivo Sección 3 (86e3za6ck, 86e3zcbjw, 86e3yyy8t):
/// 1) Da de alta formalmente en contabilidad.cuentas_contables todas las cuentas reales confirmadas por Laura Cerón y Fernando Alejos (5 y 6-nov).
/// 2) Asigna las cuentas reales a todos los roles del motor contable (puente recepción-factura 201.05.00.00, cobros tarjeta 101.01.03.00, cheques 101.01.04.00, merma 504.25.02.00).
/// 3) Mapea el 100% de los roles de retenciones (proveedor y cliente) sin dejar ninguna cuenta nula.
/// 4) Asigna las cuentas reales de inventario por grupo (115.02.01.00, 115.02.02.00, 115.02.03.00 y 115.07.02.00).
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261107170000_CuentasOficialesYRolesContabilidad")]
public sealed class CuentasOficialesRolesContabilidad : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Alta de cuentas contables oficiales si no existen
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                r_emp RECORD;
            BEGIN
                FOR r_emp IN SELECT id FROM compartido.empresas LOOP
                    -- Cuentas Nivel 1 y 2 de apoyo
                    INSERT INTO contabilidad.cuentas_contables
                        (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control,
                         no_afectable_manual, codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
                    VALUES
                        (gen_random_uuid(), r_emp.id, '101.00.00.00', 'EFECTIVO Y EQUIVALENTES DE EFECTIVO', NULL, 1, 0, 0, 0, 0, false, '101', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '101.01.00.00', 'CAJA', NULL, 2, 0, 0, 0, 0, false, '101.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.00.00.00', 'IMPUESTOS A FAVOR', NULL, 1, 0, 0, 0, 0, false, '113', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.01.00.00', 'IMPUESTOS RETENIDOS', NULL, 2, 0, 0, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.01.02.00', 'RETENCIONES DE CLIENTES', NULL, 3, 0, 0, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.00.00.00', 'INVENTARIOS', NULL, 1, 0, 0, 0, 0, false, '115', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.02.00.00', 'MATERIAS PRIMAS', NULL, 2, 0, 0, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.07.00.00', 'HERRAJES Y ACCESORIOS', NULL, 2, 0, 0, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '201.00.00.00', 'PROVEEDORES', NULL, 1, 1, 0, 0, 0, false, '201', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.00.00.00', 'IMPUESTOS RETENIDOS', NULL, 1, 1, 0, 0, 0, false, '216', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.01.00.00', 'RETENCIONES DE ISR', NULL, 2, 1, 0, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.00.00', 'RETENCIONES DE IVA', NULL, 2, 1, 0, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '504.00.00.00', 'OTROS COSTOS DE PRODUCCION', NULL, 1, 0, 0, 0, 0, false, '501', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '504.25.00.00', 'FALTANTES Y MERMAS', NULL, 2, 0, 0, 0, 0, false, '501.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '704.00.00.00', 'OTROS PRODUCTOS', NULL, 1, 1, 0, 0, 0, false, '702', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '704.23.00.00', 'REVALUACIONES Y OTROS PRODUCTOS', NULL, 2, 1, 0, 0, 0, false, '702.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3')
                    ON CONFLICT (empresa_id, codigo) DO NOTHING;

                    -- Cuentas Oficiales Afectables
                    INSERT INTO contabilidad.cuentas_contables
                        (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control,
                         no_afectable_manual, codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
                    VALUES
                        -- 86e3za6ck
                        (gen_random_uuid(), r_emp.id, '101.01.03.00', 'COBROS T/C, T/D', NULL, 3, 0, 1, 0, 0, false, '102.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '101.01.04.00', 'CHEQUES SBC EN CAJA COBRANZA', NULL, 3, 0, 1, 0, 0, false, '102.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '201.05.00.00', 'COMPRAS', NULL, 2, 1, 1, 0, 0, false, '201.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '201.06.00.00', 'COMPRAS ACTIVO FIJO', NULL, 2, 1, 1, 0, 0, false, '201.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '504.25.02.00', 'MERMAS', NULL, 3, 0, 1, 0, 0, false, '501.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '704.23.02.00', 'REVALUACION DE ACTIVO FIJO', NULL, 3, 1, 1, 0, 0, false, '702.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),

                        -- 86e3zcbjw: Retenciones Proveedores
                        (gen_random_uuid(), r_emp.id, '216.01.03.00', 'RETENCION ISR ARRENDAMIENTO 10%', NULL, 3, 1, 1, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.01.04.00', 'RETENCION ISR SERVICIOS PROFESIONALES 10%', NULL, 3, 1, 1, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.01.05.00', 'RETENCION ISR RESICO 1.25%', NULL, 3, 1, 1, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.03.00.00', 'RETENCION ISR ARRENDAMIENTO 10%', NULL, 3, 1, 1, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.04.00.00', 'RETENCION ISR SERVICIOS PROFESIONALES 10%', NULL, 3, 1, 1, 0, 0, false, '216.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.01.00', 'RETENCION IVA FLETES PAGADO 4%', NULL, 3, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.02.01', 'RETENCION IVA HONORARIOS 10.6667%', NULL, 4, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.02.02', 'RETENCION IVA RESICO 10.6667%', NULL, 4, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.03.00', 'RETENCION IVA ARRENDAMIENTOS 10.6667%', NULL, 3, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.02.04.00', 'RETENCION IVA SERVICIOS PAGADO 6%', NULL, 3, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.10.01.00', 'RETENCION IVA HONORARIOS', NULL, 3, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '216.10.02.00', 'RETENCION IVA FLETES', NULL, 3, 1, 1, 0, 0, false, '216.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),

                        -- 86e3zcbjw: Retenciones Clientes
                        (gen_random_uuid(), r_emp.id, '113.01.02.01', 'RETENCION IVA FLETES COBRADO 4%', NULL, 4, 0, 1, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.01.02.03', 'RETENCION IVA SERVICIOS COBRADO 6%', NULL, 4, 0, 1, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.01.02.05', 'RETENCION IVA DESPERDICIO COBRADO 16%', NULL, 4, 0, 1, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '113.01.02.07', 'RETENCION IVA IMPORTACION COBRADO 16%', NULL, 4, 0, 1, 0, 0, false, '113.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),

                        -- 86e3yyy8t: Cuentas de Inventario Reales
                        (gen_random_uuid(), r_emp.id, '115.02.01.00', 'MATERIA PRIMA - VIDRIO', NULL, 3, 0, 1, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.02.02.00', 'MATERIA PRIMA - LAMINADO/INSULADO', NULL, 3, 0, 1, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.02.03.00', 'SUMINISTROS, MATERIALES Y REFACCIONES', NULL, 3, 0, 1, 0, 0, false, '115.02', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.02.04.00', 'MP PINTURA ELECTROSTATICA', NULL, 3, 0, 1, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.07.01.00', 'HERRAJES', NULL, 3, 0, 1, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3'),
                        (gen_random_uuid(), r_emp.id, '115.07.02.00', 'ACCESORIOS', NULL, 3, 0, 1, 0, 0, false, '115.01', NULL, 0, NULL, 1, now(), now(), 'migration-sec3', 'migration-sec3')
                    ON CONFLICT (empresa_id, codigo) DO NOTHING;
                END LOOP;
            END $$;
            """);

        // 2. Asignación definitiva de cuentas oficiales a roles de cuenta
        migrationBuilder.Sql("""
            CREATE TEMP TABLE cuentas_oficiales_roles (rol text PRIMARY KEY, cuenta text NOT NULL, motivo text NOT NULL) ON COMMIT DROP;
            INSERT INTO cuentas_oficiales_roles (rol, cuenta, motivo) VALUES
                -- Cuentas oficiales del motor (86e3za6ck)
                ('MERCANCIA_RECIBIDA_POR_FACTURAR', '201.05.00.00', 'Cuenta puente oficial de recepción de compras (Laura Cerón, 5-nov)'),
                ('COBROS_TC_TD',                    '101.01.03.00', 'Cobros con tarjeta de crédito/débito en caja'),
                ('COBROS_CHEQUE',                   '101.01.04.00', 'Cobros con cheque en caja cobranza'),
                ('MERMA',                           '504.25.02.00', 'Costo por rotura y merma de proceso'),
                ('AJUSTE_INVENTARIO',               '504.25.02.00', 'Ajustes de inventario (faltantes y mermas)'),

                -- Retenciones oficiales Proveedores (86e3zcbjw)
                ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_ISR',       '216.04.00.00', 'ISR retenido por servicios profesionales 10%'),
                ('RETENCIONES_POR_PAGAR_HONORARIOS_PF_IVA',       '216.10.01.00', 'IVA retenido por honorarios 10.6667%'),
                ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_ISR',    '216.03.00.00', 'ISR retenido por arrendamiento 10%'),
                ('RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_IVA',    '216.02.03.00', 'IVA retenido por arrendamiento 10.6667%'),
                ('RETENCIONES_POR_PAGAR_FLETES_IVA',              '216.10.02.00', 'IVA retenido por fletes 4%'),
                ('RETENCIONES_POR_PAGAR_SERVICIOS_IVA',           '216.02.04.00', 'IVA retenido por servicios 6%'),
                ('RETENCIONES_POR_PAGAR_RESICO_PF_ISR',           '216.01.05.00', 'ISR retenido a RESICO 1.25%'),
                ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_ISR', '216.01.05.00', 'ISR retenido a RESICO con servicios 1.25%'),
                ('RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA', '216.02.02.02', 'IVA retenido a RESICO con servicios 10.6667%'),

                -- Retenciones oficiales Clientes (86e3zcbjw)
                ('RETENCIONES_CLIENTES_FLETES_IVA',               '113.01.02.01', 'IVA retenido por clientes en fletes 4%'),
                ('RETENCIONES_CLIENTES_SERVICIOS_IVA',            '113.01.02.03', 'IVA retenido por clientes en servicios 6%'),
                ('RETENCIONES_CLIENTES_DESPERDICIO_IVA',          '113.01.02.05', 'IVA retenido por clientes en desperdicio 16%'),
                ('RETENCIONES_CLIENTES_IMPORTACION_IVA',          '113.01.02.07', 'IVA retenido por clientes en importación 16%'),

                -- Cuentas oficiales inventario por grupo (86e3yyy8t)
                ('INVENTARIO_VIDRIO_FLOTADO',                     '115.02.01.00', 'Inventario MP Vidrio'),
                ('INVENTARIO_VIDRIO_TEMPLADO',                    '115.02.01.00', 'Inventario MP Vidrio'),
                ('INVENTARIO_VIDRIO_LAMINADO',                    '115.02.02.00', 'Inventario MP Laminado/Insulado'),
                ('INVENTARIO_VIDRIO_INSULADO',                    '115.02.02.00', 'Inventario MP Laminado/Insulado'),
                ('INVENTARIO_PINT_ELEC_QUIM_MP',                  '115.02.04.00', 'Inventario Pintura electrostática'),
                ('INVENTARIO_HERRAJES_IMPORTACION',               '115.07.01.00', 'Inventario Herrajes'),
                ('INVENTARIO_HERRAJES_NACIONALES',                '115.07.01.00', 'Inventario Herrajes'),
                ('INVENTARIO_ACCESORIOS',                         '115.07.02.00', 'Inventario Accesorios'),
                ('INVENTARIO_INSUMOS_PRODUCCION',                 '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_REFAC_MAQ_Y_OTROS',                  '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_REFAC_TRANSPORTE',                   '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_REFAC_COMPUTO',                      '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_REFAC_EDIFICIOS',                    '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_ALUMINIO',                           '115.07.02.00', 'Aluminio imputado a Accesorios (Contabilidad 5-nov)'),
                ('INVENTARIO_ALUCOM',                             '115.07.02.00', 'Alucom imputado a Accesorios (Contabilidad 5-nov)'),
                ('INVENTARIO_REYNOBON',                           '115.07.02.00', 'Reynobond imputado a Accesorios (Contabilidad 5-nov)'),
                ('INVENTARIO_EMPAQUE_Y_EMBALAJE',                 '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_SERIGRAFIA_TINT_DIG',                '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_PINTURAS_Y_QUIMICOS',                '115.02.03.00', 'Suministros y materiales'),
                ('INVENTARIO_PRODUCTOS_QUIMICOS',                 '115.02.03.00', 'Suministros y materiales');

            -- Asegurar que los roles existan en contabilidad.roles_cuenta
            INSERT INTO contabilidad.roles_cuenta
                (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                 version, created_at, updated_at, created_by, updated_by, deleted_at)
            SELECT gen_random_uuid(), e.id, cor.rol, cor.motivo, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                   1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-sec3', 'migration-sec3', NULL
            FROM compartido.empresas e
            CROSS JOIN cuentas_oficiales_roles cor
            WHERE NOT EXISTS (
                SELECT 1 FROM contabilidad.roles_cuenta r
                WHERE r.empresa_id = e.id AND r.codigo = cor.rol AND r.deleted_at IS NULL
                  AND r.sucursal_id IS NULL AND r.moneda IS NULL
            );

            -- Actualizar los roles con sus cuentas reales definitivas
            WITH por_actualizar AS (
                SELECT r.id AS rol_id, r.empresa_id, r.cuenta_id AS anterior_id, c.id AS nueva_id, cor.motivo
                FROM contabilidad.roles_cuenta r
                JOIN cuentas_oficiales_roles cor ON cor.rol = r.codigo
                JOIN contabilidad.cuentas_contables c ON c.empresa_id = r.empresa_id AND c.codigo = cor.cuenta AND c.deleted_at IS NULL
                WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL
                  AND (r.cuenta_id IS DISTINCT FROM c.id)
            ),
            actualizados AS (
                UPDATE contabilidad.roles_cuenta r
                   SET cuenta_id = p.nueva_id, version = r.version + 1,
                       updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-sec3'
                  FROM por_actualizar p
                 WHERE r.id = p.rol_id
                RETURNING r.id, r.empresa_id, p.anterior_id, p.nueva_id, p.motivo
            )
            INSERT INTO contabilidad.roles_cuenta_cambios
                (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
            SELECT gen_random_uuid(), a.empresa_id, a.id, a.anterior_id, a.nueva_id, 'migration-sec3', now() AT TIME ZONE 'UTC',
                   'Asignación oficial Sección 3: ' || a.motivo
            FROM actualizados a;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM contabilidad.roles_cuenta_cambios WHERE created_by = 'migration-sec3';
            DELETE FROM contabilidad.cuentas_contables WHERE created_by = 'migration-sec3';
            """);
    }
}
