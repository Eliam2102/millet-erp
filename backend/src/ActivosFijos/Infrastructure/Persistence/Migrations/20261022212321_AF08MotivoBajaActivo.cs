using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AF08MotivoBajaActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_baja",
                schema: "activos_fijos",
                table: "activos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_baja",
                schema: "activos_fijos",
                table: "activos");
        }
    }
}
