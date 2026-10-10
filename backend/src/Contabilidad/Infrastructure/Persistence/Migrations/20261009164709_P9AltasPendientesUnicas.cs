using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9AltasPendientesUnicas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_alta",
                schema: "contabilidad",
                table: "solicitudes_catalogo",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE contabilidad.solicitudes_catalogo
                SET codigo_alta = upper(btrim(comando_json ->> 'codigo'))
                WHERE operacion = 'Alta' AND estado = 'Pendiente';
                """);

            migrationBuilder.CreateIndex(
                name: "ux_p9_alta_pendiente",
                schema: "contabilidad",
                table: "solicitudes_catalogo",
                columns: new[] { "empresa_id", "codigo_alta" },
                unique: true,
                filter: "estado = 'Pendiente' AND codigo_alta IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_p9_alta_pendiente",
                schema: "contabilidad",
                table: "solicitudes_catalogo");

            migrationBuilder.DropColumn(
                name: "codigo_alta",
                schema: "contabilidad",
                table: "solicitudes_catalogo");
        }
    }
}
