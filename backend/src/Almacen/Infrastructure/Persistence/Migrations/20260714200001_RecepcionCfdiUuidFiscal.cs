using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecepcionCfdiUuidFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cfdi_uuid_fiscal",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_cfdi_uuid_pendiente",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "cfdi_uuid_fiscal",
                filter: "cfdi_uuid_fiscal IS NOT NULL AND cfdi_recibido_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_movimientos_cfdi_uuid_pendiente",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "cfdi_uuid_fiscal",
                schema: "almacen",
                table: "movimientos_inventario");
        }
    }
}
