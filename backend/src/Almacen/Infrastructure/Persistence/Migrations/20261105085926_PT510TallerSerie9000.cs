using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PT510TallerSerie9000 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cierres_taller",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    hojas_tomadas = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    retazos_tomados = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    m2descargados = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    m2cortados = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    pedidos_servidos = table.Column<int>(type: "integer", nullable: false),
                    pedidos_serie9000 = table.Column<int>(type: "integer", nullable: false),
                    cortadores = table.Column<int>(type: "integer", nullable: false),
                    factor_optimizacion = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    corregido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierres_taller", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "descargas_taller_aw",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    articulo_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    ancho_mm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    alto_mm = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    clave_sucursal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_pedido = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    m2 = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    folio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    intentado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_descargas_taller_aw", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cierres_taller_empresa_id_sucursal_id_fecha",
                schema: "almacen",
                table: "cierres_taller",
                columns: new[] { "empresa_id", "sucursal_id", "fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_descargas_taller_aw_empresa_id_estado_intentado_at",
                schema: "almacen",
                table: "descargas_taller_aw",
                columns: new[] { "empresa_id", "estado", "intentado_at" });

            migrationBuilder.CreateIndex(
                name: "ix_descargas_taller_aw_empresa_id_origen_numero_pedido_posicion",
                schema: "almacen",
                table: "descargas_taller_aw",
                columns: new[] { "empresa_id", "origen", "numero_pedido", "posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_descargas_taller_aw_empresa_id_sucursal_id_fecha_pedido",
                schema: "almacen",
                table: "descargas_taller_aw",
                columns: new[] { "empresa_id", "sucursal_id", "fecha_pedido" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cierres_taller",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "descargas_taller_aw",
                schema: "almacen");
        }
    }
}
