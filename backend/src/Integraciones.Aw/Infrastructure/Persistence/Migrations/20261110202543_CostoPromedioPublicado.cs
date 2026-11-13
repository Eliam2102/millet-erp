using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostoPromedioPublicado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "costo_promedio_publicado",
                schema: "integraciones_aw",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_aw = table.Column<int>(type: "integer", nullable: false),
                    sucursal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    almacen_tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    linea = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ancho_mm = table.Column<int>(type: "integer", nullable: false),
                    alto_mm = table.Column<int>(type: "integer", nullable: false),
                    costo = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    version_costo = table.Column<int>(type: "integer", nullable: false),
                    publicado_en_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costo_promedio_publicado", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_costo_promedio_publicado_llave",
                schema: "integraciones_aw",
                table: "costo_promedio_publicado",
                columns: new[] { "articulo_aw", "sucursal", "almacen_tipo", "linea", "ancho_mm", "alto_mm" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "costo_promedio_publicado",
                schema: "integraciones_aw");
        }
    }
}
