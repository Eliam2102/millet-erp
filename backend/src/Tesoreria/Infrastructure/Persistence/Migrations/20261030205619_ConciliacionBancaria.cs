using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConciliacionBancaria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_bancaria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_anio = table.Column<int>(type: "integer", nullable: false),
                    periodo_mes = table.Column<int>(type: "integer", nullable: false),
                    saldo_extracto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    saldo_libros = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    cerrada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_reapertura = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reabierta_por = table.Column<Guid>(type: "uuid", nullable: true),
                    reabierta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    firmante_nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    firmante_puesto = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    acta_generada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion", x => x.id);
                    table.CheckConstraint("ck_conciliacion_estado", "estado IN (1, 2)");
                    table.CheckConstraint("ck_conciliacion_periodo_mes", "periodo_mes BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_conciliacion_cuenta_bancaria_cuenta_bancaria_id",
                        column: x => x.cuenta_bancaria_id,
                        principalSchema: "tesoreria",
                        principalTable: "cuenta_bancaria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bitacora_conciliacion",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracto_linea_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movimiento_bancario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_conciliacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_bitacora_conciliacion_conciliacion_conciliacion_id",
                        column: x => x.conciliacion_id,
                        principalSchema: "tesoreria",
                        principalTable: "conciliacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extracto_linea",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conciliacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_linea = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    abono = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    fuente = table.Column<short>(type: "smallint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    match_tipo = table.Column<short>(type: "smallint", nullable: true),
                    match_movimiento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linea_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    partida_transito = table.Column<bool>(type: "boolean", nullable: false),
                    partida_transito_tipo = table.Column<short>(type: "smallint", nullable: true),
                    partida_transito_responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    partida_transito_autorizada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    candidatos_revision_ids = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extracto_linea", x => x.id);
                    table.CheckConstraint("ck_extracto_linea_estado", "estado IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_extracto_linea_fuente", "fuente IN (1, 2)");
                    table.CheckConstraint("ck_extracto_linea_importe", "(cargo IS NOT NULL OR abono IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_extracto_linea_conciliacion_conciliacion_id",
                        column: x => x.conciliacion_id,
                        principalSchema: "tesoreria",
                        principalTable: "conciliacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_extracto_linea_movimiento_bancario_match_movimiento_id",
                        column: x => x.match_movimiento_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "extracto_linea_match",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extracto_linea_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movimiento_bancario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fecha_match = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emparejado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extracto_linea_match", x => x.id);
                    table.ForeignKey(
                        name: "fk_extracto_linea_match_extracto_linea_extracto_linea_id",
                        column: x => x.extracto_linea_id,
                        principalSchema: "tesoreria",
                        principalTable: "extracto_linea",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_extracto_linea_match_movimiento_bancario_movimiento_bancari",
                        column: x => x.movimiento_bancario_id,
                        principalSchema: "tesoreria",
                        principalTable: "movimiento_bancario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_conciliacion_conciliacion",
                schema: "tesoreria",
                table: "bitacora_conciliacion",
                column: "conciliacion_id");

            migrationBuilder.CreateIndex(
                name: "ux_conciliacion_cuenta_periodo",
                schema: "tesoreria",
                table: "conciliacion",
                columns: new[] { "cuenta_bancaria_id", "periodo_anio", "periodo_mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_conciliacion_numero",
                schema: "tesoreria",
                table: "extracto_linea",
                columns: new[] { "conciliacion_id", "numero_linea" });

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_hash",
                schema: "tesoreria",
                table: "extracto_linea",
                columns: new[] { "conciliacion_id", "linea_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_match_movimiento_id",
                schema: "tesoreria",
                table: "extracto_linea",
                column: "match_movimiento_id");

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_match_linea",
                schema: "tesoreria",
                table: "extracto_linea_match",
                column: "extracto_linea_id");

            migrationBuilder.CreateIndex(
                name: "ix_extracto_linea_match_movimiento",
                schema: "tesoreria",
                table: "extracto_linea_match",
                column: "movimiento_bancario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_conciliacion",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "extracto_linea_match",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "extracto_linea",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "conciliacion",
                schema: "tesoreria");
        }
    }
}
