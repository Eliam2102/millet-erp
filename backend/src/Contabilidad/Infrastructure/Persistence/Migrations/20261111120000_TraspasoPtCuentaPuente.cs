using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// M-249 · traspaso entre sucursales (Fernando Alejos 11-nov 10:08, Laura Cerón 11-nov 10:34): no es un préstamo, es un movimiento de inventario con cuenta
/// puente. Laura creó 115.05.01.00 «TRASPASO DE INVENTARIO» (nivel 3) bajo 115.05.00.00 «MERCANCIAS EN TRANSITO» (nivel 2).
/// <list type="number">
///   <item>Da de alta las dos cuentas (si el catálogo importado no las trae).</item>
///   <item>El rol puente se llama <c>INVENTARIO_POR_TRASPASO</c> y apunta a 115.05.01.00: renombra el rol <c>INVENTARIO_EN_PRESTAMO</c> y su nombre dentro de las plantillas, o lo crea si no existe.</item>
///   <item>Plantillas de los dos eventos de PT: salida autorizada (cargo puente · abono INVENTARIO_PT del origen) y entrada registrada (cargo INVENTARIO_PT de la sucursal que recibe · abono puente).</item>
/// </list>
/// Solo datos: el modelo no cambia.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261111120000_TraspasoPtCuentaPuente")]
public sealed class TraspasoPtCuentaPuente : Migration
{
    private const string Usuario = "migration-M249";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($$"""
        INSERT INTO contabilidad.cuentas_contables
            (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control,
             no_afectable_manual, codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
        SELECT gen_random_uuid(), e.id, '115.05.00.00', 'MERCANCIAS EN TRANSITO', NULL, 2, 0, 0, 0, 0, false, '115.05', NULL, 0, NULL, 1, now(), now(), '{{Usuario}}', '{{Usuario}}'
        FROM compartido.empresas e
        ON CONFLICT (empresa_id, codigo) DO NOTHING;

        INSERT INTO contabilidad.cuentas_contables
            (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control,
             no_afectable_manual, codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
        SELECT gen_random_uuid(), e.id, '115.05.01.00', 'TRASPASO DE INVENTARIO',
               (SELECT p.id FROM contabilidad.cuentas_contables p WHERE p.empresa_id = e.id AND p.codigo = '115.05.00.00'),
               3, 0, 1, 0, 0, false, '115.05', NULL, 0, NULL, 1, now(), now(), '{{Usuario}}', '{{Usuario}}'
        FROM compartido.empresas e
        ON CONFLICT (empresa_id, codigo) DO NOTHING;

        -- Renombra el rol que existía como préstamo (sin pisar uno ya creado con el nombre nuevo) y lo manda a la cuenta puente.
        CREATE TEMP TABLE m249_renombrar ON COMMIT DROP AS
        SELECT r.id, r.empresa_id, r.cuenta_id AS anterior, c.id AS cuenta_nueva
        FROM contabilidad.roles_cuenta r
        JOIN contabilidad.cuentas_contables c ON c.empresa_id = r.empresa_id AND c.codigo = '115.05.01.00' AND c.deleted_at IS NULL
        WHERE r.codigo = 'INVENTARIO_EN_PRESTAMO' AND r.deleted_at IS NULL
          AND NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = r.empresa_id AND x.codigo = 'INVENTARIO_POR_TRASPASO'
                          AND x.sucursal_id IS NOT DISTINCT FROM r.sucursal_id AND x.moneda IS NOT DISTINCT FROM r.moneda);

        UPDATE contabilidad.roles_cuenta r
           SET codigo = 'INVENTARIO_POR_TRASPASO',
               descripcion = 'Cuenta puente del traspaso de inventario entre sucursales, no préstamo (115.05.01.00 TRASPASO DE INVENTARIO); siempre termina en cero',
               cuenta_id = m.cuenta_nueva, version = r.version + 1, updated_at = now() AT TIME ZONE 'UTC', updated_by = '{{Usuario}}'
          FROM m249_renombrar m
         WHERE r.id = m.id;

        INSERT INTO contabilidad.roles_cuenta_cambios (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
        SELECT gen_random_uuid(), empresa_id, id, anterior, cuenta_nueva, '{{Usuario}}', now() AT TIME ZONE 'UTC',
               'M-249: traspaso entre sucursales, no préstamo; cuenta puente 115.05.01.00'
        FROM m249_renombrar
        WHERE anterior IS DISTINCT FROM cuenta_nueva;

        UPDATE contabilidad.plantillas_contables
           SET lineas = replace(lineas::text, '"INVENTARIO_EN_PRESTAMO"', '"INVENTARIO_POR_TRASPASO"')::jsonb
         WHERE lineas::text LIKE '%"INVENTARIO_EN_PRESTAMO"%';

        -- Donde el rol no existía, se crea ya mapeado a la cuenta puente.
        INSERT INTO contabilidad.roles_cuenta
            (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, 'INVENTARIO_POR_TRASPASO',
               'Cuenta puente del traspaso de inventario entre sucursales, no préstamo (115.05.01.00 TRASPASO DE INVENTARIO); siempre termina en cero',
               (SELECT c.id FROM contabilidad.cuentas_contables c WHERE c.empresa_id = e.id AND c.codigo = '115.05.01.00' AND c.deleted_at IS NULL),
               NULL, NULL, DATE '2000-01-01', NULL, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', '{{Usuario}}', '{{Usuario}}', NULL
        FROM compartido.empresas e
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = 'INVENTARIO_POR_TRASPASO')
        ON CONFLICT DO NOTHING;

        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', '{{Usuario}}', '{{Usuario}}', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('producto_terminado.traspaso.salida-autorizada.v1', 'SM', 'Salida por traspaso', 'M-249: traspaso autorizado; cuenta puente de inventario por traspaso contra inventario de PT de la sucursal origen (no es préstamo)',
             '[{"Orden": 1, "Rol": "INVENTARIO_POR_TRASPASO", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario por traspaso"}, {"Orden": 2, "Rol": "INVENTARIO_PT", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario de producto terminado"}]'),
            ('producto_terminado.traspaso.entrada-registrada.v1', 'EM', 'Entrada por traspaso', 'M-249: piezas del traspaso que llegan a destino, regresan al origen o se dan por perdidas; inventario de PT de la sucursal que recibe contra la cuenta puente',
             '[{"Orden": 1, "Rol": "INVENTARIO_PT", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario de producto terminado"}, {"Orden": 2, "Rol": "INVENTARIO_POR_TRASPASO", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario por traspaso"}]')
        ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento);
        """);

    /// <summary>Quita lo que esta migración dio de alta; el rol renombrado conserva su nombre nuevo.</summary>
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = '{Usuario}';
        """);
}
