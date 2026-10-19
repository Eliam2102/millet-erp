using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AwBitacoraHuellaPrevia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_referencia_externa_campo_t",
                schema: "compartido",
                table: "cambios_maestros_aw");

            migrationBuilder.AddColumn<string>(
                name: "huella_previa",
                schema: "compartido",
                table: "cambios_maestros_aw",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_referencia_externa_campo_t",
                schema: "compartido",
                table: "cambios_maestros_aw",
                columns: new[] { "tipo_entidad", "referencia_externa", "campo", "tipo", "version_evento", "huella_previa" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_referencia_externa_campo_t",
                schema: "compartido",
                table: "cambios_maestros_aw");

            migrationBuilder.DropColumn(
                name: "huella_previa",
                schema: "compartido",
                table: "cambios_maestros_aw");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_maestros_aw_tipo_entidad_referencia_externa_campo_t",
                schema: "compartido",
                table: "cambios_maestros_aw",
                columns: new[] { "tipo_entidad", "referencia_externa", "campo", "tipo", "version_evento" },
                unique: true);
        }
    }
}
