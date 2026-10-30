using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortalProveedoresAcceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acceso_portal_proveedor",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceso_portal_proveedor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "codigo_portal",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acceso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    consumido = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigo_portal", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acceso_portal_proveedor_empresa_id_proveedor_id",
                schema: "cuentas_por_pagar",
                table: "acceso_portal_proveedor",
                columns: new[] { "empresa_id", "proveedor_id" });

            migrationBuilder.CreateIndex(
                name: "ux_acceso_portal_proveedor_email",
                schema: "cuentas_por_pagar",
                table: "acceso_portal_proveedor",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigo_portal_acceso_id_created_at",
                schema: "cuentas_por_pagar",
                table: "codigo_portal",
                columns: new[] { "acceso_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceso_portal_proveedor",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "codigo_portal",
                schema: "cuentas_por_pagar");
        }
    }
}
