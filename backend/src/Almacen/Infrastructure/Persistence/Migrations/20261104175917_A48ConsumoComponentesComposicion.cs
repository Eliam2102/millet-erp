using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A48ConsumoComponentesComposicion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumos_componente_aw",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operacion = table.Column<int>(type: "integer", nullable: false),
                    componente_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    capas = table.Column<int>(type: "integer", nullable: false),
                    auftnr = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    pos = table.Column<int>(type: "integer", nullable: false),
                    ancho_mm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    alto_mm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    clave_sucursal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bearb_zeit = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    folio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intentado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumos_componente_aw", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "normas_consumo_componente",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumo_por_unidad = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    porcentaje_merma = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    factor_m3por_m2 = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_normas_consumo_componente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumos_componente_aw_empresa_id_estado_intentado_at",
                schema: "almacen",
                table: "consumos_componente_aw",
                columns: new[] { "empresa_id", "estado", "intentado_at" });

            migrationBuilder.CreateIndex(
                name: "ix_consumos_componente_aw_empresa_id_origen_etiqueta_operacion",
                schema: "almacen",
                table: "consumos_componente_aw",
                columns: new[] { "empresa_id", "origen", "etiqueta", "operacion", "componente_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_normas_consumo_componente_empresa_id_articulo_id",
                schema: "almacen",
                table: "normas_consumo_componente",
                columns: new[] { "empresa_id", "articulo_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumos_componente_aw",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "normas_consumo_componente",
                schema: "almacen");
        }
    }
}
