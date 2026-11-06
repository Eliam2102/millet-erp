using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P23BrechasAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.CreateTable(
                name: "comprobaciones_anticipo_ce",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comprobaciones_anticipo_ce", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                sql: "(origen = 1 AND uuid_cfdi IS NOT NULL AND referencia_origen IS NULL) OR (origen IN (2, 3, 4) AND uuid_cfdi IS NULL AND cfdi_recibido_id IS NULL AND referencia_origen IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comprobaciones_anticipo_ce",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                sql: "(origen = 1 AND uuid_cfdi IS NOT NULL AND referencia_origen IS NULL) OR (origen IN (2, 3) AND uuid_cfdi IS NULL AND cfdi_recibido_id IS NULL AND referencia_origen IS NOT NULL)");
        }
    }
}
