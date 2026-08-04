using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotaCreditoYRelacionesCfdi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nota_credito",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<short>(type: "smallint", nullable: false),
                    anticipo_origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_relacionada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importe_afecta_inventario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    clave_prod_serv_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    clave_unidad_sat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nota_credito", x => x.id);
                    table.ForeignKey(
                        name: "fk_nota_credito_comprobante_id",
                        column: x => x.id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "relacion_cfdi",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comprobante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uuid_relacionado = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    tipo_relacion = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relacion_cfdi", x => x.id);
                    table.ForeignKey(
                        name: "fk_relacion_cfdi_comprobantes_comprobante_id",
                        column: x => x.comprobante_id,
                        principalSchema: "facturacion",
                        principalTable: "comprobante",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_nota_credito_anticipo",
                schema: "facturacion",
                table: "nota_credito",
                column: "anticipo_origen_id",
                filter: "anticipo_origen_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_nota_credito_factura",
                schema: "facturacion",
                table: "nota_credito",
                column: "factura_relacionada_id",
                filter: "factura_relacionada_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_relacion_cfdi_comprobante",
                schema: "facturacion",
                table: "relacion_cfdi",
                column: "comprobante_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nota_credito",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "relacion_cfdi",
                schema: "facturacion");
        }
    }
}
