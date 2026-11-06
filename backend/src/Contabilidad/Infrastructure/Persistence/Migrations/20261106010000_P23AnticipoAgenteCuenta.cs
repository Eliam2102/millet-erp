using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261106010000_P23AnticipoAgenteCuenta")]
public sealed class P23AnticipoAgenteCuenta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE contabilidad.plantillas_contables p SET lineas = (
          SELECT jsonb_agg(CASE WHEN l->>'Rol' = 'ANTICIPO_AGENTE_ADUANAL'
            THEN l || '{"Rol":"ANTICIPO_A_PROVEEDORES","Auxiliar":"Proveedor"}'::jsonb ELSE l END ORDER BY ord)
          FROM jsonb_array_elements(p.lineas) WITH ORDINALITY AS x(l, ord))
        WHERE tipo_evento IN ('tesoreria.anticipo-agente.pagado.v1', 'tesoreria.anticipo-agente.devolucion-confirmada.v1');
        """);
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
