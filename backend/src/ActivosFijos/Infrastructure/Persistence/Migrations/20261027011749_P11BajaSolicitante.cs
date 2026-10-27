using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ActivosFijos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11BajaSolicitante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "solicitado_por",
                schema: "activos_fijos",
                table: "operaciones_libro",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "Sin dato (anterior al 26-oct-2026)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "solicitado_por",
                schema: "activos_fijos",
                table: "operaciones_libro");
        }
    }
}
