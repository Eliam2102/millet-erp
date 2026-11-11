using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M327ReversaComprobacionAnticipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reversa_pendiente",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reversas_comprobacion",
                schema: "comercio_exterior",
                table: "anticipos_agente",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reversa_pendiente",
                schema: "comercio_exterior",
                table: "anticipos_agente");

            migrationBuilder.DropColumn(
                name: "reversas_comprobacion",
                schema: "comercio_exterior",
                table: "anticipos_agente");
        }
    }
}
