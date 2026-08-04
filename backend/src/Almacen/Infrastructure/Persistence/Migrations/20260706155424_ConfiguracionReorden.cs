using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracionReorden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuraciones_reorden",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nivel = table.Column<short>(type: "smallint", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimo = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    maximo = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    punto_reorden = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    auto_requisicion = table.Column<bool>(type: "boolean", nullable: false),
                    objetivo = table.Column<short>(type: "smallint", nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuraciones_reorden", x => x.id);
                    table.CheckConstraint("ck_config_reorden_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_config_reorden_max_no_menor_min", "maximo >= minimo");
                    table.CheckConstraint("ck_config_reorden_nivel", "nivel BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_config_reorden_niveles_no_negativos", "minimo >= 0 AND maximo >= 0 AND punto_reorden >= 0");
                    table.CheckConstraint("ck_config_reorden_objetivo", "objetivo BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateIndex(
                name: "ix_config_reorden_estatus_auto",
                schema: "almacen",
                table: "configuraciones_reorden",
                columns: new[] { "estatus", "auto_requisicion" });

            migrationBuilder.CreateIndex(
                name: "ix_configuraciones_reorden_articulo_id",
                schema: "almacen",
                table: "configuraciones_reorden",
                column: "articulo_id");

            migrationBuilder.CreateIndex(
                name: "ux_config_reorden_articulo_nivel_entidad",
                schema: "almacen",
                table: "configuraciones_reorden",
                columns: new[] { "articulo_id", "nivel", "entidad_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuraciones_reorden",
                schema: "almacen");
        }
    }
}
