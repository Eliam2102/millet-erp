using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeriodosContables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "periodos_contables",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ejercicio = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    cerrado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reabierto_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reabierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_contables", x => x.id);
                    table.CheckConstraint("ck_periodos_contables_estado", "estado BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_periodos_contables_numero", "numero BETWEEN 1 AND 13");
                });

            migrationBuilder.CreateTable(
                name: "periodos_contables_eventos",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<short>(type: "smallint", nullable: false),
                    usuario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_contables_eventos", x => x.id);
                    table.CheckConstraint("ck_periodos_eventos_accion", "accion BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_periodos_contables_eventos_periodos_contables_periodo_id",
                        column: x => x.periodo_id,
                        principalSchema: "contabilidad",
                        principalTable: "periodos_contables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_periodos_contables_ejercicio_numero",
                schema: "contabilidad",
                table: "periodos_contables",
                columns: new[] { "empresa_id", "ejercicio", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_periodos_contables_eventos_empresa_id_periodo_id_fecha",
                schema: "contabilidad",
                table: "periodos_contables_eventos",
                columns: new[] { "empresa_id", "periodo_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_periodos_contables_eventos_periodo_id",
                schema: "contabilidad",
                table: "periodos_contables_eventos",
                column: "periodo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "periodos_contables_eventos",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "periodos_contables",
                schema: "contabilidad");
        }
    }
}
