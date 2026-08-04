using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservasStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reservas_stock",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    documento_origen_tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    documento_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    motivo_liberacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    movimiento_consumo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservas_stock", x => x.id);
                    table.CheckConstraint("ck_reservas_cantidad_positiva", "cantidad > 0");
                    table.CheckConstraint("ck_reservas_consumida_tiene_movimiento", "(estado = 1 AND movimiento_consumo_id IS NOT NULL) OR (estado <> 1)");
                    table.CheckConstraint("ck_reservas_estado_valido", "estado BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_reservas_liberada_tiene_motivo", "(estado = 2 AND motivo_liberacion IS NOT NULL) OR (estado <> 2)");
                    table.ForeignKey(
                        name: "fk_reservas_stock_sub_almacenes_sub_almacen_id",
                        column: x => x.sub_almacen_id,
                        principalSchema: "almacen",
                        principalTable: "sub_almacenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservas_activas_articulo",
                schema: "almacen",
                table: "reservas_stock",
                columns: new[] { "articulo_id", "sub_almacen_id" },
                filter: "estado = 0");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_por_documento",
                schema: "almacen",
                table: "reservas_stock",
                columns: new[] { "documento_origen_tipo", "documento_origen_id" },
                filter: "estado = 0");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_stock_sub_almacen_id",
                schema: "almacen",
                table: "reservas_stock",
                column: "sub_almacen_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservas_stock",
                schema: "almacen");
        }
    }
}
