using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CotizacionEleccionesAnteriores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cotizacion_elecciones_anteriores",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    elegida_por = table.Column<Guid>(type: "uuid", nullable: false),
                    elegida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo_liberacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    liberada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    liberada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cotizacion_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizacion_elecciones_anteriores", x => x.id);
                    table.ForeignKey(
                        name: "fk_cotizacion_elecciones_anteriores_cotizaciones_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalSchema: "compras",
                        principalTable: "cotizaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_elecciones_anteriores_cotizacion_id",
                schema: "compras",
                table: "cotizacion_elecciones_anteriores",
                column: "cotizacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cotizacion_elecciones_anteriores",
                schema: "compras");
        }
    }
}
