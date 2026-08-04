using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BitacoraEnvioCorreo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bitacora_envio_correo",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinatario = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    enviado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_envio_correo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_envio_correo_comprobante",
                schema: "facturacion",
                table: "bitacora_envio_correo",
                column: "comprobante_id");

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_envio_correo_estado",
                schema: "facturacion",
                table: "bitacora_envio_correo",
                column: "estado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_envio_correo",
                schema: "facturacion");
        }
    }
}
