using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SolicitudCancelacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solicitud_cancelacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo_sat = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    uuid_sustituto = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    estatus_sat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    mensaje_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitud_cancelacion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_cancelacion_comprobante",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                column: "comprobante_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_cancelacion_estado",
                schema: "facturacion",
                table: "solicitud_cancelacion",
                column: "estado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitud_cancelacion",
                schema: "facturacion");
        }
    }
}
