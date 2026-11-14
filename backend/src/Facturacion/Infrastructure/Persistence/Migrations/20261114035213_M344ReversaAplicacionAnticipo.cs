using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M344ReversaAplicacionAnticipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anticipo_reversa_aplicacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anticipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nc_amortizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    aplicado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autorizado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reversado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipo_reversa_aplicacion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_reversa_anticipo_factura",
                schema: "facturacion",
                table: "anticipo_reversa_aplicacion",
                columns: new[] { "anticipo_id", "factura_venta_id" });

            migrationBuilder.CreateIndex(
                name: "ux_anticipo_reversa_nc",
                schema: "facturacion",
                table: "anticipo_reversa_aplicacion",
                column: "nc_amortizacion_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anticipo_reversa_aplicacion",
                schema: "facturacion");
        }
    }
}
