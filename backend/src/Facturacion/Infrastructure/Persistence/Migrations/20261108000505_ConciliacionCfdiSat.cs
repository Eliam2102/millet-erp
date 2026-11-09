using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConciliacionCfdiSat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion_sat",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<DateOnly>(type: "date", nullable: false),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    fuente = table.Column<short>(type: "smallint", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ejecutada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    ejecutada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cfdi_sat = table.Column<int>(type: "integer", nullable: false),
                    cfdi_erp = table.Column<int>(type: "integer", nullable: false),
                    coincidentes = table.Column<int>(type: "integer", nullable: false),
                    con_diferencias = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_sat", x => x.id);
                    table.CheckConstraint("ck_conciliacion_sat_fuente", "fuente IN (1, 2)");
                    table.CheckConstraint("ck_conciliacion_sat_periodo", "hasta >= desde");
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_sat_diferencia",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rfc_receptor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    valor_sat = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    valor_erp = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    revisada = table.Column<bool>(type: "boolean", nullable: false),
                    revisada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_sat_diferencia", x => x.id);
                    table.CheckConstraint("ck_conciliacion_sat_diferencia_tipo", "tipo BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_conciliacion_sat_diferencia_conciliacion_sat_conciliacion_id",
                        column: x => x.conciliacion_id,
                        principalSchema: "facturacion",
                        principalTable: "conciliacion_sat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_sat_empresa_fecha",
                schema: "facturacion",
                table: "conciliacion_sat",
                columns: new[] { "empresa_id", "ejecutada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_sat_diferencia_corrida_tipo",
                schema: "facturacion",
                table: "conciliacion_sat_diferencia",
                columns: new[] { "conciliacion_id", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_sat_diferencia_empresa_uuid",
                schema: "facturacion",
                table: "conciliacion_sat_diferencia",
                columns: new[] { "empresa_id", "uuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_sat_diferencia",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "conciliacion_sat",
                schema: "facturacion");
        }
    }
}
