using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeriodosCerrados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "periodos_cerrados",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    cerrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_cerrados", x => x.id);
                    table.CheckConstraint("ck_periodos_mes_valido", "mes BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateIndex(
                name: "ux_periodos_cerrados",
                schema: "almacen",
                table: "periodos_cerrados",
                columns: new[] { "empresa_id", "anio", "mes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "periodos_cerrados",
                schema: "almacen");
        }
    }
}
