using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P25RecepcionPorGuia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "numero_contenedor",
                schema: "almacen",
                table: "recepciones_importacion",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN IF EXISTS (SELECT 1 FROM almacen.recepciones_importacion WHERE numero_contenedor IS NULL)
                THEN RAISE EXCEPTION 'No se puede revertir P25: existen recepciones por guía sin contenedor.'; END IF; END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "numero_contenedor",
                schema: "almacen",
                table: "recepciones_importacion",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
