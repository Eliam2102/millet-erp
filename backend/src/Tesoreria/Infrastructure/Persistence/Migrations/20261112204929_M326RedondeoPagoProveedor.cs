using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M326RedondeoPagoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "redondeo",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "autorizacion_redondeo_pago",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redondeo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_redondeo_pago", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_redondeo_pago_factura",
                schema: "tesoreria",
                table: "autorizacion_redondeo_pago",
                columns: new[] { "empresa_id", "factura_proveedor_id" });

            migrationBuilder.CreateIndex(
                name: "ux_autorizacion_redondeo_pago_vigente",
                schema: "tesoreria",
                table: "autorizacion_redondeo_pago",
                column: "factura_proveedor_id",
                unique: true,
                filter: "estado IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_redondeo_pago",
                schema: "tesoreria");

            migrationBuilder.DropColumn(
                name: "redondeo",
                schema: "tesoreria",
                table: "aplicacion_pago_proveedor");
        }
    }
}
