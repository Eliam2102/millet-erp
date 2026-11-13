using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-283 (contrato v9 de la app de Almacenes): clave fija del motivo de la entrada y salida manual en el movimiento, para que
    /// Contabilidad asigne la cuenta por clave aunque el motivo se renombre. Nullable, sin catálogo.
    /// </summary>
    public partial class M283MotivoClaveMovimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_clave",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_clave",
                schema: "almacen",
                table: "movimientos_inventario");
        }
    }
}
