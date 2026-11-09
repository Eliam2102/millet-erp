using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M166AperturaContable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M-166: OrigenPoliza.Apertura = 2.
            migrationBuilder.DropCheckConstraint(name: "ck_polizas_origen", schema: "contabilidad", table: "polizas");
            migrationBuilder.AddCheckConstraint(name: "ck_polizas_origen", schema: "contabilidad", table: "polizas", sql: "origen BETWEEN 0 AND 2");

            migrationBuilder.CreateTable(
                name: "aperturas",
                schema: "contabilidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha_corte = table.Column<DateOnly>(type: "date", nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subido_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aplicado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    aplicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aperturas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aperturas_lote_id",
                schema: "contabilidad",
                table: "aperturas",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ux_aperturas_empresa",
                schema: "contabilidad",
                table: "aperturas",
                column: "empresa_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_polizas_origen", schema: "contabilidad", table: "polizas");
            migrationBuilder.AddCheckConstraint(name: "ck_polizas_origen", schema: "contabilidad", table: "polizas", sql: "origen BETWEEN 0 AND 1");

            migrationBuilder.DropTable(
                name: "aperturas",
                schema: "contabilidad");
        }
    }
}
