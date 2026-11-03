using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CertificadosCsd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "csd_revocado_en",
                schema: "integraciones_fiscal",
                table: "configuracion_pac",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alertas_csd",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificado_csd_id = table.Column<Guid>(type: "uuid", nullable: false),
                    umbral_dias = table.Column<int>(type: "integer", nullable: false),
                    dias_restantes = table.Column<int>(type: "integer", nullable: false),
                    disparada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    destinatarios = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alertas_csd", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificados_csd",
                schema: "integraciones_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuracion_pac_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_serie = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registrado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    finalizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalizado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    motivo_revocacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificados_csd", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_alertas_csd_certificado_umbral",
                schema: "integraciones_fiscal",
                table: "alertas_csd",
                columns: new[] { "certificado_csd_id", "umbral_dias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificados_csd_empresa_numero_serie",
                schema: "integraciones_fiscal",
                table: "certificados_csd",
                columns: new[] { "empresa_id", "numero_serie" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alertas_csd",
                schema: "integraciones_fiscal");

            migrationBuilder.DropTable(
                name: "certificados_csd",
                schema: "integraciones_fiscal");

            migrationBuilder.DropColumn(
                name: "csd_revocado_en",
                schema: "integraciones_fiscal",
                table: "configuracion_pac");
        }
    }
}
