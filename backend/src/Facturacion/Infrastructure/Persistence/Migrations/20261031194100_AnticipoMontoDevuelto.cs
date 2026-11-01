using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <summary>F6.3 · CA6.17 (86e3kdypx): lo devuelto en dinero del anticipo por NC de devolución.</summary>
    public partial class AnticipoMontoDevuelto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "monto_devuelto",
                schema: "facturacion",
                table: "anticipo",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "monto_devuelto",
                schema: "facturacion",
                table: "anticipo");
        }
    }
}
