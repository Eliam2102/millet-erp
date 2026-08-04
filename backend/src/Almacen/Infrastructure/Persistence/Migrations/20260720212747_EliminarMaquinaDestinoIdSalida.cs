using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EliminarMaquinaDestinoIdSalida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "maquina_destino_id",
                schema: "almacen",
                table: "movimientos_inventario");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "maquina_destino_id",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "uuid",
                nullable: true);
        }
    }
}
