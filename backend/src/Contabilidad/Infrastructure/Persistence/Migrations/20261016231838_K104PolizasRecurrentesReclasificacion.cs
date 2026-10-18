using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class K104PolizasRecurrentesReclasificacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "plantilla_recurrente_id",
                schema: "contabilidad",
                table: "polizas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "poliza_relacionada_id",
                schema: "contabilidad",
                table: "polizas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "relacion",
                schema: "contabilidad",
                table: "polizas",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plantillas_recurrentes",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_operacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    anio_inicio = table.Column<int>(type: "integer", nullable: false),
                    mes_inicio = table.Column<int>(type: "integer", nullable: false),
                    numero_periodos = table.Column<int>(type: "integer", nullable: false),
                    dia_del_mes = table.Column<int>(type: "integer", nullable: false),
                    reversa_automatica = table.Column<bool>(type: "boolean", nullable: false),
                    nace_autorizada = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    creada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activada_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    activada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    partidas = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantillas_recurrentes", x => x.id);
                    table.CheckConstraint("ck_plantillas_recurrentes_dia", "dia_del_mes BETWEEN 0 AND 28");
                    table.CheckConstraint("ck_plantillas_recurrentes_estado", "estado BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_plantillas_recurrentes_mes", "mes_inicio BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_plantillas_recurrentes_periodos", "numero_periodos BETWEEN 1 AND 120");
                    table.ForeignKey(
                        name: "fk_plantillas_recurrentes_tipos_documento_tipo_operacion_id",
                        column: x => x.tipo_operacion_id,
                        principalSchema: "contabilidad",
                        principalTable: "tipos_documento_contable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_polizas_plantilla_recurrente",
                schema: "contabilidad",
                table: "polizas",
                column: "plantilla_recurrente_id",
                filter: "plantilla_recurrente_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_polizas_relacionada",
                schema: "contabilidad",
                table: "polizas",
                column: "poliza_relacionada_id",
                filter: "poliza_relacionada_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_recurrentes_estado",
                schema: "contabilidad",
                table: "plantillas_recurrentes",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_recurrentes_tipo_operacion_id",
                schema: "contabilidad",
                table: "plantillas_recurrentes",
                column: "tipo_operacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plantillas_recurrentes",
                schema: "contabilidad");

            migrationBuilder.DropIndex(
                name: "ix_polizas_plantilla_recurrente",
                schema: "contabilidad",
                table: "polizas");

            migrationBuilder.DropIndex(
                name: "ix_polizas_relacionada",
                schema: "contabilidad",
                table: "polizas");

            migrationBuilder.DropColumn(
                name: "plantilla_recurrente_id",
                schema: "contabilidad",
                table: "polizas");

            migrationBuilder.DropColumn(
                name: "poliza_relacionada_id",
                schema: "contabilidad",
                table: "polizas");

            migrationBuilder.DropColumn(
                name: "relacion",
                schema: "contabilidad",
                table: "polizas");
        }
    }
}
