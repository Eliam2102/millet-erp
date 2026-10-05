using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AgregarComposicionClasificacionProductoAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "familia_codigo",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grupo",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                schema: "compartido",
                table: "producto_aw",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "producto_aw_componente",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_aw_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    padre_orden = table.Column<int>(type: "integer", nullable: true),
                    componente_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    espesor_mm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto_aw_componente", x => x.id);
                    table.CheckConstraint("ck_producto_aw_componente_posicion", "orden >= 1 AND nivel >= 1 AND (padre_orden IS NULL OR (padre_orden >= 1 AND padre_orden < orden)) AND (espesor_mm IS NULL OR espesor_mm > 0)");
                    table.ForeignKey(
                        name: "fk_producto_aw_componente_producto_aw_producto_aw_id",
                        column: x => x.producto_aw_id,
                        principalSchema: "compartido",
                        principalTable: "producto_aw",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_componente_producto_aw_id_orden",
                schema: "compartido",
                table: "producto_aw_componente",
                columns: new[] { "producto_aw_id", "orden" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_aw_componente",
                schema: "compartido");

            migrationBuilder.DropColumn(
                name: "familia_codigo",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "grupo",
                schema: "compartido",
                table: "producto_aw");

            migrationBuilder.DropColumn(
                name: "tipo",
                schema: "compartido",
                table: "producto_aw");
        }
    }
}
