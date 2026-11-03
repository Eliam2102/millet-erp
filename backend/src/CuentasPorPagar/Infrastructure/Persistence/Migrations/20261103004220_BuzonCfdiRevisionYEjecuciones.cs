using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuzonCfdiRevisionYEjecuciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buzon_ejecuciones",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mensajes = table.Column<int>(type: "integer", nullable: false),
                    ingresados = table.Column<int>(type: "integer", nullable: false),
                    duplicados = table.Column<int>(type: "integer", nullable: false),
                    a_revision = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buzon_ejecuciones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "buzon_revision_cfdi",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mensaje_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    asunto = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    remitente = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    descartado = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_descarte = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buzon_revision_cfdi", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_buzon_ejecuciones_inicio",
                schema: "cuentas_por_pagar",
                table: "buzon_ejecuciones",
                column: "inicio_utc");

            migrationBuilder.CreateIndex(
                name: "ix_buzon_revision_cfdi_estado_fecha",
                schema: "cuentas_por_pagar",
                table: "buzon_revision_cfdi",
                columns: new[] { "descartado", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ux_buzon_revision_cfdi_mensaje",
                schema: "cuentas_por_pagar",
                table: "buzon_revision_cfdi",
                column: "mensaje_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "buzon_ejecuciones",
                schema: "cuentas_por_pagar");

            migrationBuilder.DropTable(
                name: "buzon_revision_cfdi",
                schema: "cuentas_por_pagar");
        }
    }
}
