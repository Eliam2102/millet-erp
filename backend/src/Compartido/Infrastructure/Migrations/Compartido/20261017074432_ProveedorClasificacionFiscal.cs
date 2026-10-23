using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorClasificacionFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "concepto_retencion",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "origen",
                schema: "compartido",
                table: "proveedores",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "regimen_fiscal_codigo",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concepto_retencion",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "origen",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "regimen_fiscal_codigo",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
