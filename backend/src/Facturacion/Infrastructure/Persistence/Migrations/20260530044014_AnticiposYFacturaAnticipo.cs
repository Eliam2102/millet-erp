using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnticiposYFacturaAnticipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anticipo",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receptor_rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    tipo_anticipo = table.Column<short>(type: "smallint", nullable: false),
                    moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monto_cobrado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    monto_amortizado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    saldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    factura_anticipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_origen_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    obra_id = table.Column<long>(type: "bigint", nullable: true),
                    obra_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "factura_anticipo",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_anticipo = table.Column<short>(type: "smallint", nullable: false),
                    pedido_facturable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anticipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_prod_serv_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    clave_unidad_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_factura_anticipo", x => x.id);
                    table.ForeignKey(
                        name: "fk_factura_anticipo_comprobante_id",
                        column: x => x.id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "anticipo_vinculacion",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anticipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nc_amortizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anticipo_vinculacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_anticipo_vinculacion_anticipos_anticipo_id",
                        column: x => x.anticipo_id,
                        principalSchema: "facturacion",
                        principalTable: "anticipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_cliente_estado",
                schema: "facturacion",
                table: "anticipo",
                columns: new[] { "empresa_id", "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_factura_anticipo",
                schema: "facturacion",
                table: "anticipo",
                column: "factura_anticipo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_vinculacion_factura",
                schema: "facturacion",
                table: "anticipo_vinculacion",
                column: "factura_venta_id");

            migrationBuilder.CreateIndex(
                name: "ix_anticipo_vinculacion_unica",
                schema: "facturacion",
                table: "anticipo_vinculacion",
                columns: new[] { "anticipo_id", "factura_venta_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_factura_anticipo_anticipo",
                schema: "facturacion",
                table: "factura_anticipo",
                column: "anticipo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_factura_anticipo_pedido",
                schema: "facturacion",
                table: "factura_anticipo",
                column: "pedido_facturable_id",
                filter: "pedido_facturable_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anticipo_vinculacion",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "factura_anticipo",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "anticipo",
                schema: "facturacion");
        }
    }
}
