using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCheckpointsDescargaSat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkpoints_descarga_sat",
                schema: "cuentas_por_pagar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checkpoints_descarga_sat",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfc_receptor = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ultimo_error_codigo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ultimo_error_mensaje = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ultimo_exito_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_tick_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_tick_cantidad_cfdis = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkpoints_descarga_sat", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_checkpoints_descarga_sat_empresa_rfc",
                schema: "cuentas_por_pagar",
                table: "checkpoints_descarga_sat",
                columns: new[] { "empresa_id", "rfc_receptor" },
                unique: true);
        }
    }
}
