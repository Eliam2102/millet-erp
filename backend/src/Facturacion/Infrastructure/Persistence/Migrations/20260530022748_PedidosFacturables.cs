using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PedidosFacturables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pedido_facturable",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    numero_pedido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    canal_venta = table.Column<short>(type: "smallint", nullable: false),
                    comportamiento_fiscal = table.Column<short>(type: "smallint", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    obra_id = table.Column<long>(type: "bigint", nullable: true),
                    obra_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    comprobante_vigente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    comentarios = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_facturable", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedido_facturable_linea",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producto_descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    clave_prod_serv_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    clave_unidad_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cantidad = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    precio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    descuento = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    requiere_pedimento = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido_facturable_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedido_facturable_linea_pedidos_facturables_pedido_facturab",
                        column: x => x.pedido_facturable_id,
                        principalSchema: "facturacion",
                        principalTable: "pedido_facturable",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pedido_facturable_bandeja",
                schema: "facturacion",
                table: "pedido_facturable",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_pedido_facturable_obra",
                schema: "facturacion",
                table: "pedido_facturable",
                column: "obra_id",
                filter: "obra_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_facturable_origen_numero",
                schema: "facturacion",
                table: "pedido_facturable",
                columns: new[] { "origen", "numero_pedido" },
                filter: "numero_pedido IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_facturable_linea_posicion",
                schema: "facturacion",
                table: "pedido_facturable_linea",
                columns: new[] { "pedido_facturable_id", "posicion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pedido_facturable_linea",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "pedido_facturable",
                schema: "facturacion");
        }
    }
}
