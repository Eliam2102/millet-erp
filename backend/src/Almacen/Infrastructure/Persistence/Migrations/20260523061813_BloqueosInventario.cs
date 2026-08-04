using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BloqueosInventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bloqueos_inventario",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bloquea_salidas = table.Column<bool>(type: "boolean", nullable: false),
                    bloquea_entradas = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bloqueos_inventario", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bloqueos_inv_sub_almacen_activos",
                schema: "almacen",
                table: "bloqueos_inventario",
                column: "sub_almacen_id",
                filter: "activo = true");

            migrationBuilder.CreateIndex(
                name: "ix_bloqueos_inventario_conteo_id",
                schema: "almacen",
                table: "bloqueos_inventario",
                column: "conteo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bloqueos_inventario",
                schema: "almacen");
        }
    }
}
