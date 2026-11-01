using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CobroFolioTransferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "folio_transferencia",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cobro_forma_pago_folio_transferencia",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago",
                column: "folio_transferencia",
                filter: "folio_transferencia IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cobro_forma_pago_folio_transferencia",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago");

            migrationBuilder.DropColumn(
                name: "folio_transferencia",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago");
        }
    }
}
