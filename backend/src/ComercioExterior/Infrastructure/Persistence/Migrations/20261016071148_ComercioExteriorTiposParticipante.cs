using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComercioExteriorTiposParticipante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "descripcion",
                schema: "comercio_exterior",
                table: "participantes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE comercio_exterior.participantes
                SET descripcion = tipo,
                    tipo = CASE WHEN lower(btrim(tipo)) = 'agente aduanal' THEN 'AgenteAduanal' ELSE 'Otro' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE comercio_exterior.participantes
                SET tipo = left(coalesce(descripcion, tipo), 80);
                """);

            migrationBuilder.DropColumn(
                name: "descripcion",
                schema: "comercio_exterior",
                table: "participantes");
        }
    }
}
