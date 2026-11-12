using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaF1Cierre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_duplicado",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_tipo_cambio",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "solicitudes_cuenta_bancaria",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version_cuenta = table.Column<int>(type: "integer", nullable: true),
                    datos_vigentes_json = table.Column<string>(type: "jsonb", nullable: true),
                    datos_propuestos_json = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_rechazo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_cuenta_bancaria", x => x.id);
                    table.CheckConstraint("ck_solicitudes_cuenta_bancaria_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_solicitudes_cuenta_bancaria_tipo", "tipo IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_cuenta_bancaria_estado",
                schema: "tesoreria",
                table: "solicitudes_cuenta_bancaria",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_solicitudes_cuenta_bancaria_pendiente",
                schema: "tesoreria",
                table: "solicitudes_cuenta_bancaria",
                column: "cuenta_bancaria_id",
                unique: true,
                filter: "estado = 1 AND cuenta_bancaria_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitudes_cuenta_bancaria",
                schema: "tesoreria");

            migrationBuilder.DropColumn(
                name: "motivo_duplicado",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "motivo_tipo_cambio",
                schema: "tesoreria",
                table: "movimiento_bancario");
        }
    }
}
