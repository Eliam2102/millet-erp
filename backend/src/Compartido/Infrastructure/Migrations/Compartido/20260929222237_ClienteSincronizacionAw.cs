using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClienteSincronizacionAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cliente_sincronizacion_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    mandant_origen = table.Column<int>(type: "integer", nullable: true),
                    nombre_comercial_origen = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    domicilio_origen_calle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    domicilio_origen_ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    domicilio_origen_cp = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    domicilio_origen_provincia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    domicilio_origen_pais = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    candidato_fiscal_ust_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    candidato_fiscal_steuernummer = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    telefono2_origen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    condicion_codigo_origen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    condicion_numero_origen = table.Column<int>(type: "integer", nullable: true),
                    dias_nominales_origen = table.Column<int>(type: "integer", nullable: true),
                    moneda_codigo_origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    moneda_normalizada = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    credito_referencia_limite = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    credito_referencia_limite_1 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    credito_referencia_net = table.Column<double>(type: "double precision", nullable: true),
                    estado_origen_crudo = table.Column<int>(type: "integer", nullable: true),
                    bloqueo_origen_crudo = table.Column<int>(type: "integer", nullable: true),
                    fecha_origen = table.Column<DateOnly>(type: "date", nullable: true),
                    transaccion_origen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ejecucion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leido_en_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aplicado_en_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hash_origen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_contrato = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_mapeo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cliente_sincronizacion_aw", x => x.id);
                    table.CheckConstraint("ck_cliente_sincronizacion_aw_resultado", "resultado BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_cliente_sincronizacion_aw_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "compartido",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cliente_sincronizacion_aw_cliente_id",
                schema: "compartido",
                table: "cliente_sincronizacion_aw",
                column: "cliente_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cliente_sincronizacion_aw_referencia_externa",
                schema: "compartido",
                table: "cliente_sincronizacion_aw",
                column: "referencia_externa",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cliente_sincronizacion_aw",
                schema: "compartido");
        }
    }
}
