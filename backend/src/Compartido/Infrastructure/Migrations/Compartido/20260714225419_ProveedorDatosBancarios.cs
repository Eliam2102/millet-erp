using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorDatosBancarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "banco",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "beneficiario",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clabe",
                schema: "compartido",
                table: "proveedores",
                type: "character varying(18)",
                maxLength: 18,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_proveedores_clabe_formato",
                schema: "compartido",
                table: "proveedores",
                sql: "clabe IS NULL OR clabe ~ '^[0-9]{18}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proveedores_clabe_formato",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "banco",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "beneficiario",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DropColumn(
                name: "clabe",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
