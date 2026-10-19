using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AvisosSlaRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avisos_sla_revision",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependencia_revisora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    dias_habiles = table.Column<int>(type: "integer", nullable: false),
                    sla_dias = table.Column<int>(type: "integer", nullable: false),
                    fecha_entrada_revision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_sla_revision", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_sla_revision_factura_entrada",
                schema: "cuentas_por_pagar",
                table: "avisos_sla_revision",
                columns: new[] { "factura_proveedor_id", "fecha_entrada_revision" });

            migrationBuilder.CreateIndex(
                name: "ux_avisos_sla_revision_factura_nivel_fecha",
                schema: "cuentas_por_pagar",
                table: "avisos_sla_revision",
                columns: new[] { "factura_proveedor_id", "nivel", "fecha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_sla_revision",
                schema: "cuentas_por_pagar");
        }
    }
}
