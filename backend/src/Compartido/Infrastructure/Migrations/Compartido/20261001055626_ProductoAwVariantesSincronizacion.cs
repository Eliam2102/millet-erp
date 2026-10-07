using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProductoAwVariantesSincronizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_baja",
                schema: "compartido",
                table: "producto_aw",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "producto_aw_variante",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_aw_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_variante = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    alto_mm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    ancho_mm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    espesor_mm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    composicion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto_aw_variante", x => x.id);
                    table.CheckConstraint("ck_producto_aw_variante_medidas", "(alto_mm IS NULL OR alto_mm > 0) AND (ancho_mm IS NULL OR ancho_mm > 0) AND (espesor_mm IS NULL OR espesor_mm > 0)");
                    table.ForeignKey(
                        name: "fk_producto_aw_variante_producto_aw_producto_aw_id",
                        column: x => x.producto_aw_id,
                        principalSchema: "compartido",
                        principalTable: "producto_aw",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "producto_sincronizacion_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_aw_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion_origen = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    unidad_origen_cruda = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    baja_origen_cruda = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    transaccion_origen_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ejecucion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leido_en_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    aplicado_en_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hash_origen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_contrato = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_mapeo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resultado = table.Column<short>(type: "smallint", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    diferencias = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto_sincronizacion_aw", x => x.id);
                    table.CheckConstraint("ck_producto_sincronizacion_aw_resultado", "resultado BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_producto_sincronizacion_aw_producto_aw_producto_aw_id",
                        column: x => x.producto_aw_id,
                        principalSchema: "compartido",
                        principalTable: "producto_aw",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_variante_producto_aw_id_clave_variante",
                schema: "compartido",
                table: "producto_aw_variante",
                columns: new[] { "producto_aw_id", "clave_variante" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_producto_sincronizacion_aw_producto_aw_id",
                schema: "compartido",
                table: "producto_sincronizacion_aw",
                column: "producto_aw_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_producto_sincronizacion_aw_referencia_externa",
                schema: "compartido",
                table: "producto_sincronizacion_aw",
                column: "referencia_externa",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto_aw_variante",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "producto_sincronizacion_aw",
                schema: "compartido");

            migrationBuilder.DropColumn(
                name: "fecha_baja",
                schema: "compartido",
                table: "producto_aw");
        }
    }
}
