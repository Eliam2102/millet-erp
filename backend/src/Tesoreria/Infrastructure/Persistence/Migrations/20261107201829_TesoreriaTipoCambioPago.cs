using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaTipoCambioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tipo_cambio",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tipo_cambio",
                schema: "tesoreria",
                table: "movimiento_bancario");
        }
    }
}
