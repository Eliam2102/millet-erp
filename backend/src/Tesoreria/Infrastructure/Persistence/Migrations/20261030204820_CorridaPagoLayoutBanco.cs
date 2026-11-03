using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorridaPagoLayoutBanco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corrida_pago_archivo",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    banco = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tipo_pago = table.Column<short>(type: "smallint", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    lineas_validas = table.Column<int>(type: "integer", nullable: false),
                    lineas_excluidas = table.Column<int>(type: "integer", nullable: false),
                    veces_descargado = table.Column<int>(type: "integer", nullable: false),
                    generado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_descarga_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_archivo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "corrida_pago_perfil_layout",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    banco = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_pago = table.Column<short>(type: "smallint", nullable: false),
                    separador = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    incluye_encabezado = table.Column<bool>(type: "boolean", nullable: false),
                    formato_importe = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    longitud_cuenta = table.Column<int>(type: "integer", nullable: false),
                    longitud_titular = table.Column<int>(type: "integer", nullable: false),
                    longitud_referencia = table.Column<int>(type: "integer", nullable: false),
                    longitud_concepto = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_perfil_layout", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "corrida_pago_archivo_linea",
                schema: "tesoreria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    archivo_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe_neto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cuenta_destino = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    concepto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corrida_pago_archivo_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_corrida_pago_archivo_linea_corrida_pago_archivo_archivo_lay",
                        column: x => x.archivo_layout_id,
                        principalSchema: "tesoreria",
                        principalTable: "corrida_pago_archivo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_corrida_pago_archivo_tipo",
                schema: "tesoreria",
                table: "corrida_pago_archivo",
                columns: new[] { "empresa_id", "corrida_id", "banco", "tipo_pago" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_corrida_pago_archivo_linea_factura",
                schema: "tesoreria",
                table: "corrida_pago_archivo_linea",
                columns: new[] { "archivo_layout_id", "factura_proveedor_id" });

            migrationBuilder.CreateIndex(
                name: "ux_corrida_pago_perfil_codigo",
                schema: "tesoreria",
                table: "corrida_pago_perfil_layout",
                columns: new[] { "empresa_id", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corrida_pago_archivo_linea",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "corrida_pago_perfil_layout",
                schema: "tesoreria");

            migrationBuilder.DropTable(
                name: "corrida_pago_archivo",
                schema: "tesoreria");
        }
    }
}
