using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <summary>Script 16 v4: claves de empleado del capturista y del vendedor de la orden de Planta Pintura.</summary>
    public partial class PinturasEmpleadoTrazabilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "empleado_captura_clave",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vendedor_clave",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "empleado_captura_clave",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "vendedor_clave",
                schema: "facturacion",
                table: "pedido_facturable");
        }
    }
}
