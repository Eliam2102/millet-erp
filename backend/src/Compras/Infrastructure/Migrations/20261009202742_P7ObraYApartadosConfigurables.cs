using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class P7ObraYApartadosConfigurables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "apartar_existencia_al_autorizar",
                schema: "compras",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "obra",
                schema: "compras",
                table: "requisiciones",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "obra",
                schema: "compras",
                table: "ordenes_compra",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "apartar_existencia_al_autorizar",
                schema: "compras",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "obra",
                schema: "compras",
                table: "requisiciones");

            migrationBuilder.DropColumn(
                name: "obra",
                schema: "compras",
                table: "ordenes_compra");
        }
    }
}
