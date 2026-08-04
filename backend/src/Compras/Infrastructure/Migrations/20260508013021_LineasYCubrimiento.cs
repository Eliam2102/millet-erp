using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LineasYCubrimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requisicion_lineas",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisicion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<short>(type: "smallint", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cuenta_contable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proyecto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_requerida = table.Column<DateOnly>(type: "date", nullable: true),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cant_de_almacen = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false, defaultValue: 0m),
                    cant_de_compra = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false, defaultValue: 0m),
                    cant_recibida = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false, defaultValue: 0m),
                    precio_estimado = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "MXN"),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisicion_lineas", x => x.id);
                    table.CheckConstraint("ck_requisicion_lineas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("ck_requisicion_lineas_cubrimiento_no_excede", "cant_de_almacen + cant_de_compra <= cantidad");
                    table.CheckConstraint("ck_requisicion_lineas_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_requisicion_lineas_recibida_no_excede_compra", "cant_recibida <= cant_de_compra");
                    table.ForeignKey(
                        name: "fk_requisicion_lineas_requisiciones_requisicion_id",
                        column: x => x.requisicion_id,
                        principalSchema: "compras",
                        principalTable: "requisiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisicion_lineas_requisicion_id_posicion",
                schema: "compras",
                table: "requisicion_lineas",
                columns: new[] { "requisicion_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisicion_lineas",
                schema: "compras");
        }
    }
}
