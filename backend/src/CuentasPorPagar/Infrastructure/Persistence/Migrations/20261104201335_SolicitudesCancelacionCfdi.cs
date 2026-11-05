using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SolicitudesCancelacionCfdi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solicitudes_cancelacion_cfdi",
                schema: "cuentas_por_pagar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cfdi_recibido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area = table.Column<short>(type: "smallint", nullable: false),
                    fecha_recepcion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    fecha_decision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decidido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    comentario_decision = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidencia_blob_ref = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    evidencia_nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_cancelacion_cfdi", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_solicitudes_cancelacion_cfdi_pendiente",
                schema: "cuentas_por_pagar",
                table: "solicitudes_cancelacion_cfdi",
                column: "cfdi_recibido_id",
                unique: true,
                filter: "estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitudes_cancelacion_cfdi",
                schema: "cuentas_por_pagar");
        }
    }
}
