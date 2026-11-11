using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M335OrigenCapturaMp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "origen_captura_mp",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    origen_anterior = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cambiado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    cambiado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_origen_captura_mp", x => x.id);
                    table.CheckConstraint("ck_origen_captura_mp_evento", "evento IN ('recepcion','consumo','merma','requisicion')");
                    table.CheckConstraint("ck_origen_captura_mp_origen", "origen IN ('ERP','APP')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_origen_captura_mp_evento_cambiado_at",
                schema: "almacen",
                table: "origen_captura_mp",
                columns: new[] { "evento", "cambiado_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "origen_captura_mp",
                schema: "almacen");
        }
    }
}
