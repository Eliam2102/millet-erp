using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EventosProcesadosCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_procesados",
                schema: "compras",
                columns: table => new
                {
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    procesado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_procesados", x => new { x.evento_id, x.evento_tipo });
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_procesados_at",
                schema: "compras",
                table: "eventos_procesados",
                column: "procesado_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos_procesados",
                schema: "compras");
        }
    }
}
