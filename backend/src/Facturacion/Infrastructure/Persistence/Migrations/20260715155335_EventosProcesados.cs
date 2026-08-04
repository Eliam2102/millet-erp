using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventosProcesados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evento_procesado",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evento_procesado", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_evento_procesado_id_tipo",
                schema: "facturacion",
                table: "evento_procesado",
                columns: new[] { "evento_id", "evento_tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evento_procesado",
                schema: "facturacion");
        }
    }
}
