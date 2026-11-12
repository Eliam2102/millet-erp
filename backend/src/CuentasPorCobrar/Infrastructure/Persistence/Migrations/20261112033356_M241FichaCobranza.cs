using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M241FichaCobranza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ficha_cobranza",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_cartera_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsable_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    siguiente_revision = table.Column<DateOnly>(type: "date", nullable: true),
                    escalada = table.Column<bool>(type: "boolean", nullable: false),
                    escalada_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escalada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ficha_cobranza", x => x.id);
                    table.ForeignKey(
                        name: "fk_ficha_cobranza_factura_cartera_factura_cartera_id",
                        column: x => x.factura_cartera_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "factura_cartera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ficha_cobranza_evento",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ficha_cobranza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<short>(type: "smallint", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nota = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    canal = table.Column<short>(type: "smallint", nullable: true),
                    resultado = table.Column<short>(type: "smallint", nullable: true),
                    monto_comprometido = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    fecha_comprometida = table.Column<DateOnly>(type: "date", nullable: true),
                    revision_antes = table.Column<DateOnly>(type: "date", nullable: true),
                    revision_despues = table.Column<DateOnly>(type: "date", nullable: true),
                    responsable_antes_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsable_antes_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsable_despues_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsable_despues_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    corrige_evento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_anterior = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    motivo_correccion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ficha_cobranza_evento", x => x.id);
                    table.CheckConstraint("ck_ficha_cobranza_evento_tipo", "tipo IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "fk_ficha_cobranza_evento_ficha_cobranza_ficha_cobranza_id",
                        column: x => x.ficha_cobranza_id,
                        principalSchema: "cuentas_por_cobrar",
                        principalTable: "ficha_cobranza",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ficha_cobranza_cliente",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_ficha_cobranza_responsable_revision",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                columns: new[] { "responsable_id", "siguiente_revision" });

            migrationBuilder.CreateIndex(
                name: "ux_ficha_cobranza_factura",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                column: "factura_cartera_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ficha_cobranza_evento_ficha_fecha",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza_evento",
                columns: new[] { "ficha_cobranza_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ficha_cobranza_evento",
                schema: "cuentas_por_cobrar");

            migrationBuilder.DropTable(
                name: "ficha_cobranza",
                schema: "cuentas_por_cobrar");
        }
    }
}
