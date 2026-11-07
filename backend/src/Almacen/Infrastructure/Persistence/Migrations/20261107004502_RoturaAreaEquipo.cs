using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoturaAreaEquipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "area_clave",
                schema: "almacen",
                table: "salidas_rotura",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "centro_costo_id",
                schema: "almacen",
                table: "salidas_rotura",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "equipo_clave",
                schema: "almacen",
                table: "salidas_rotura",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "area_clave",
                schema: "almacen",
                table: "salidas_rotura");

            migrationBuilder.DropColumn(
                name: "centro_costo_id",
                schema: "almacen",
                table: "salidas_rotura");

            migrationBuilder.DropColumn(
                name: "equipo_clave",
                schema: "almacen",
                table: "salidas_rotura");
        }
    }
}
