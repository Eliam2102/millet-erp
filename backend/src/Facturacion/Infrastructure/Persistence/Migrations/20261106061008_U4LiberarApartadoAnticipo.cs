using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class U4LiberarApartadoAnticipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anticipo_liberacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anticipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    liberado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liberado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    liberado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipo_liberacion", x => x.id);
                    table.CheckConstraint("ck_anticipo_liberacion_motivo", "char_length(motivo) BETWEEN 5 AND 500");
                    table.ForeignKey(
                        name: "fk_anticipo_liberacion_anticipos_anticipo_id",
                        column: x => x.anticipo_id,
                        principalSchema: "facturacion",
                        principalTable: "anticipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_liberacion_anticipo",
                schema: "facturacion",
                table: "anticipo_liberacion",
                column: "anticipo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anticipo_liberacion",
                schema: "facturacion");
        }
    }
}
