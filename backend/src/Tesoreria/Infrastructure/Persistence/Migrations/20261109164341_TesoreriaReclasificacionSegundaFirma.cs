using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaReclasificacionSegundaFirma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movimiento_reclasificaciones",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto_anterior_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concepto_nuevo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_resolucion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimiento_reclasificaciones", x => x.id);
                    table.CheckConstraint("ck_movimiento_reclasificaciones_estado", "estado IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "ux_movimiento_reclasificaciones_pendiente",
                schema: "tesoreria",
                table: "movimiento_reclasificaciones",
                column: "movimiento_id",
                unique: true,
                filter: "estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimiento_reclasificaciones",
                schema: "tesoreria");
        }
    }
}
