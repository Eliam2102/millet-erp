using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class SucursalZonaHoraria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "zona_horaria",
                schema: "compartido",
                table: "sucursales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "America/Merida");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "zona_horaria",
                schema: "compartido",
                table: "sucursales");
        }
    }
}
