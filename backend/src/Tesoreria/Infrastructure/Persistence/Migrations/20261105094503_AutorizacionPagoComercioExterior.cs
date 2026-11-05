using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutorizacionPagoComercioExterior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "autorizacion_pago_comercio_exterior",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    nivel1usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nivel1en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nivel2usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nivel2en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rechazado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rechazado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_pago_comercio_exterior", x => x.id);
                    table.CheckConstraint("ck_autorizacion_pago_ce_tipo", "tipo IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ux_autorizacion_pago_ce_factura",
                schema: "tesoreria",
                table: "autorizacion_pago_comercio_exterior",
                column: "factura_proveedor_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_pago_comercio_exterior",
                schema: "tesoreria");
        }
    }
}
