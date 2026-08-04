using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenciasAutorizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidencias_autorizacion",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<short>(type: "smallint", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    archivo_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tamanio_bytes = table.Column<long>(type: "bigint", nullable: true),
                    comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    estado_firma_fisica = table.Column<short>(type: "smallint", nullable: false),
                    fecha_limite_firma_fisica = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_recepcion_firma_fisica = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_captura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidencias_autorizacion", x => x.id);
                    table.CheckConstraint("ck_evidencias_autorizacion_tipo_soportado", "tipo_documento = 1");
                    table.CheckConstraint("ck_evidencias_firma_pendiente_requiere_fecha_limite", "estado_firma_fisica != 2 OR fecha_limite_firma_fisica IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_autorizacion_documento",
                schema: "cuentas_por_pagar",
                table: "evidencias_autorizacion",
                columns: new[] { "tipo_documento", "documento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidencias_firma_pendiente",
                schema: "cuentas_por_pagar",
                table: "evidencias_autorizacion",
                columns: new[] { "estado_firma_fisica", "fecha_limite_firma_fisica" },
                filter: "estado_firma_fisica = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidencias_autorizacion",
                schema: "cuentas_por_pagar");
        }
    }
}
