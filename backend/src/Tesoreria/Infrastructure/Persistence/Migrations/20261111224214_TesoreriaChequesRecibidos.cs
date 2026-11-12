using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TesoreriaChequesRecibidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cheques_recibidos",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banco_emisor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    numero = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    librador = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fecha_recepcion = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    movimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    extracto_linea_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contramovimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_rebote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    rebotado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    rebotado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cheques_recibidos", x => x.id);
                    table.CheckConstraint("ck_cheques_recibidos_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_cheques_recibidos_importe", "importe > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cheques_recibidos_estado",
                schema: "tesoreria",
                table: "cheques_recibidos",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_cheques_recibidos_movimiento",
                schema: "tesoreria",
                table: "cheques_recibidos",
                column: "movimiento_id",
                unique: true,
                filter: "movimiento_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_cheques_recibidos_vigente",
                schema: "tesoreria",
                table: "cheques_recibidos",
                columns: new[] { "empresa_id", "banco_emisor", "numero" },
                unique: true,
                filter: "estado <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cheques_recibidos",
                schema: "tesoreria");
        }
    }
}
