using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations;

public partial class P15TrasladoDirecto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "motivo", schema: "activos_fijos", table: "traslados",
            type: "character varying(300)", maxLength: 300, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<DateOnly>(name: "fecha_efectiva", schema: "activos_fijos", table: "traslados",
            type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
        // Los registros históricos no capturaban motivo; se identifica su ausencia sin inventarlo.
        migrationBuilder.Sql("""
            UPDATE activos_fijos.traslados
            SET motivo = 'Traslado anterior a P15: motivo no registrado',
                fecha_efectiva = (COALESCE(aceptado_en, registrado_en) AT TIME ZONE 'America/Merida')::date;
            """);
        migrationBuilder.Sql(AplicarPendientesSql);
    }

    public const string AplicarPendientesSql = """
        WITH pendientes AS (
            SELECT t.*, ROW_NUMBER() OVER (
                PARTITION BY empresa_id, activo_id ORDER BY registrado_en DESC, id DESC
            ) AS orden
            FROM activos_fijos.traslados t WHERE aceptado_en IS NULL
        )
        UPDATE activos_fijos.activos a
        SET responsable_id = p.destino_responsable_id,
            sucursal_id = p.destino_sucursal_id,
            ubicacion = p.destino_ubicacion,
            centro_costo_id = p.destino_centro_costo_id,
            equipo_id = p.destino_equipo_id,
            version = a.version + 1
        FROM pendientes p
        WHERE p.orden = 1 AND a.id = p.activo_id AND a.empresa_id = p.empresa_id;
        
        UPDATE activos_fijos.traslados
        SET aceptado_en = registrado_en, version = version + 1
        WHERE aceptado_en IS NULL;
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No se deshace la custodia aplicada: requeriría un traslado inverso explícito.
        migrationBuilder.DropColumn(name: "motivo", schema: "activos_fijos", table: "traslados");
        migrationBuilder.DropColumn(name: "fecha_efectiva", schema: "activos_fijos", table: "traslados");
    }
}
