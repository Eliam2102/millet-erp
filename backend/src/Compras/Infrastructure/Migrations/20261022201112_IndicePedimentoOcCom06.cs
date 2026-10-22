using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndicePedimentoOcCom06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ordenes_compra_empresa_id_info_import_numero_pedimento",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "empresa_id", "info_import_numero_pedimento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ordenes_compra_empresa_id_info_import_numero_pedimento",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
