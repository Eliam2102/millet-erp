using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaTransitoLibros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion_transito_libros",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_bancario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_valor = table.Column<DateOnly>(type: "date", nullable: false),
                    referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    causa = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    fecha_prevista = table.Column<DateOnly>(type: "date", nullable: false),
                    autorizada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    autorizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_transito_libros", x => x.id);
                    table.CheckConstraint("ck_conciliacion_transito_libros_tipo", "tipo IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_conciliacion_transito_libros_conciliaciones_conciliacion_id",
                        column: x => x.conciliacion_id,
                        principalSchema: "tesoreria",
                        principalTable: "conciliacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_conciliacion_transito_libros_movimiento",
                schema: "tesoreria",
                table: "conciliacion_transito_libros",
                columns: new[] { "conciliacion_id", "movimiento_bancario_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_transito_libros",
                schema: "tesoreria");
        }
    }
}
