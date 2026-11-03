using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalTarjeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terminal_carga",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha_archivo = table.Column<DateOnly>(type: "date", nullable: false),
                    cargado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    cargado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    renglones = table.Column<int>(type: "integer", nullable: false),
                    nuevos = table.Column<int>(type: "integer", nullable: false),
                    duplicados = table.Column<int>(type: "integer", nullable: false),
                    emparejados = table.Column<int>(type: "integer", nullable: false),
                    sin_emparejar = table.Column<int>(type: "integer", nullable: false),
                    rechazados = table.Column<int>(type: "integer", nullable: false),
                    liquidaciones = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminal_carga", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminal_liquidacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    bruto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    neto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    transacciones = table.Column<int>(type: "integer", nullable: false),
                    carga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminal_liquidacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terminal_transaccion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    no_operacion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha_hora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ultimos4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    autorizacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    estatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    iva_comision = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    neto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    folio_liquidacion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    cobro_mostrador_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cobro_forma_pago_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terminal_transaccion", x => x.id);
                    table.CheckConstraint("ck_terminal_transaccion_estado", "estado IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_terminal_transaccion_terminal_carga_carga_id",
                        column: x => x.carga_id,
                        principalSchema: "facturacion",
                        principalTable: "terminal_carga",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terminal_carga_empresa_fecha",
                schema: "facturacion",
                table: "terminal_carga",
                columns: new[] { "empresa_id", "cargado_en" });

            migrationBuilder.CreateIndex(
                name: "ux_terminal_liquidacion_empresa_folio",
                schema: "facturacion",
                table: "terminal_liquidacion",
                columns: new[] { "empresa_id", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terminal_transaccion_carga_id",
                schema: "facturacion",
                table: "terminal_transaccion",
                column: "carga_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminal_transaccion_empresa_estado",
                schema: "facturacion",
                table: "terminal_transaccion",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_terminal_transaccion_empresa_operacion",
                schema: "facturacion",
                table: "terminal_transaccion",
                columns: new[] { "empresa_id", "no_operacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_terminal_transaccion_forma_pago",
                schema: "facturacion",
                table: "terminal_transaccion",
                column: "cobro_forma_pago_id",
                unique: true,
                filter: "cobro_forma_pago_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "terminal_liquidacion",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "terminal_transaccion",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "terminal_carga",
                schema: "facturacion");
        }
    }
}
