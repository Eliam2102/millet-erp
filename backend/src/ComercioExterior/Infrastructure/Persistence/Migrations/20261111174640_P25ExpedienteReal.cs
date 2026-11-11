using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P25ExpedienteReal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id",
                schema: "comercio_exterior",
                table: "documentos");

            migrationBuilder.AddColumn<Guid[]>(
                name: "ordenes_compra_ids",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "requisitos_revisados",
                schema: "comercio_exterior",
                table: "expedientes",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE comercio_exterior.expedientes SET ordenes_compra_ids = ARRAY[oc_id] WHERE oc_id IS NOT NULL;
                UPDATE comercio_exterior.expedientes e SET requisitos_revisados = ARRAY(
                    SELECT t.id FROM compartido.adjunto_tipos_documento t
                    WHERE t.tipo_entidad = 'expediente_ce' AND t.activo
                    AND t.codigo LIKE CASE WHEN e.tipo = 'Importacion' THEN 'IMP_%' ELSE 'EXP_%' END
                    AND t.id NOT BETWEEN '0000000e-0003-0000-0000-00000000000f'::uuid AND '0000000e-0003-0000-0000-00000000001a'::uuid
                ) WHERE e.revisado_por_id IS NOT NULL OR e.estado = 'Cerrado';
                INSERT INTO comercio_exterior.requisitos_hitos (id, empresa_id, tipo, tipo_documento_id, hito_clave, version, created_at, updated_at, created_by, updated_by)
                SELECT gen_random_uuid(), e.empresa_id, CASE WHEN t.codigo LIKE 'IMP_%' THEN 'Importacion' ELSE 'Exportacion' END,
                    t.id, 'documentacion-completa', 1, now(), now(), 'P25', 'P25'
                FROM (SELECT DISTINCT empresa_id FROM comercio_exterior.expedientes) e
                CROSS JOIN compartido.adjunto_tipos_documento t
                WHERE t.id BETWEEN '0000000e-0003-0000-0000-00000000000f'::uuid AND '0000000e-0003-0000-0000-00000000001a'::uuid
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id",
                schema: "comercio_exterior",
                table: "documentos",
                columns: new[] { "expediente_id", "tipo_documento_id" },
                unique: true,
                filter: "estado = 'Vigente' AND tipo_documento_id IN ('0000000e-0003-0000-0000-000000000004', '0000000e-0003-0000-0000-00000000000a')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM comercio_exterior.expedientes WHERE cardinality(ordenes_compra_ids) > 1)
                    OR EXISTS (SELECT 1 FROM comercio_exterior.documentos WHERE estado = 'Vigente' GROUP BY expediente_id, tipo_documento_id HAVING count(*) > 1)
                    THEN RAISE EXCEPTION 'No se puede revertir P25 con varias OC o varios documentos vigentes; concilie antes.'; END IF;
                END $$;
                DELETE FROM comercio_exterior.requisitos_hitos WHERE created_by = 'P25';
                """);
            migrationBuilder.DropIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id",
                schema: "comercio_exterior",
                table: "documentos");

            migrationBuilder.DropColumn(
                name: "ordenes_compra_ids",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.DropColumn(
                name: "requisitos_revisados",
                schema: "comercio_exterior",
                table: "expedientes");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_expediente_id_tipo_documento_id",
                schema: "comercio_exterior",
                table: "documentos",
                columns: new[] { "expediente_id", "tipo_documento_id" },
                unique: true,
                filter: "estado = 'Vigente'");
        }
    }
}
