using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M327PlantillaReversaAnticipoAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M-327: póliza de reversa de un anticipo al agente aduanal ya comprobado (autoriza el Contador General).
            // Todo lo anticipado pasa de Anticipo al agente aduanal al saldo a favor del agente; las facturas del agente que la
            // comprobación había saldado vuelven a Proveedores. Usa roles ya existentes; las cuentas las asigna Contabilidad.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, 'cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1', 1, DATE '2000-01-01', 'AN',
                       'Reversa de comprobación de anticipo al agente aduanal',
                       'M-327: el anticipo comprobado se revierte contra el saldo a favor del agente; las facturas del agente vuelven a Proveedores',
                       '[{"Orden": 1, "Rol": "ANTICIPO_A_PROVEEDORES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Saldo a favor del agente aduanal", "RolPorClasificador": false}, {"Orden": 2, "Rol": "ANTICIPO_AGENTE_ADUANAL", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Anticipo agente aduanal", "RolPorClasificador": false}, {"Orden": 3, "Rol": "ANTICIPO_A_PROVEEDORES", "Lado": "Cargo", "Campo": "GastosLiberados", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reversa de amortización del anticipo", "RolPorClasificador": false}, {"Orden": 4, "Rol": "PROVEEDORES", "Lado": "Abono", "Campo": "GastosLiberados", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Facturas del agente por pagar", "RolPorClasificador": false}]'::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M327', 'migration-M327', NULL
                FROM compartido.empresas e
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                                  WHERE x.empresa_id = e.id AND x.tipo_evento = 'cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M327';");
        }
    }
}
