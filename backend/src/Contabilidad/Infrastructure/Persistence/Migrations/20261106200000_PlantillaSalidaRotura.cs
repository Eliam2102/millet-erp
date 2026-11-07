using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// 86e3zf04y: plantilla de la rotura capturada en la pantalla de Almacén (<c>almacen.salida_rotura.registrada.v1</c>, ALM-03). Misma
/// póliza que la rotura de la app (v2 de <c>almacen.salida_inventario.registrada.v1</c>, U-2): cargo MERMA con el área o equipo ·
/// abono INVENTARIO por grupo del artículo, tipo de operación SM. Usa los roles que ya existen (MERMA, INVENTARIO). Solo datos.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261106200000_PlantillaSalidaRotura")]
public sealed class PlantillaSalidaRotura : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, 'almacen.salida_rotura.registrada.v1', 1, DATE '2026-01-01', 'SM', 'Salida por rotura',
               'Rotura capturada en la pantalla de Almacén (ALM-03): merma con el área o equipo de la rotura contra inventario por grupo de artículo; igual que la rotura de la app (U-2)',
               '[{"Orden": 1, "Rol": "MERMA", "Lado": "Cargo", "Campo": "Merma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Merma por rotura o daño"}, {"Orden": 2, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]'::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-86e3zf04y', 'migration-86e3zf04y', NULL
        FROM compartido.empresas e
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                          WHERE x.empresa_id = e.id AND x.tipo_evento = 'almacen.salida_rotura.registrada.v1');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-86e3zf04y';
        """);
}
