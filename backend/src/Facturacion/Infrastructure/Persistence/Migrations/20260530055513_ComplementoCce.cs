using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplementoCce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "complemento_cce",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_operacion = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    incoterm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tc_dof = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    receptor_num_reg_id_trib = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    receptor_pais_residencia = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_complemento_cce", x => x.id);
                    table.ForeignKey(
                        name: "fk_complemento_cce_facturas_venta_factura_venta_id",
                        column: x => x.factura_venta_id,
                        principalSchema: "facturacion",
                        principalTable: "factura_venta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "complemento_cce_linea",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    complemento_cce_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fraccion_arancelaria = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unidad_aduana = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cantidad_aduana = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    valor_unitario_aduana = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    valor_dolares = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    aplica_iva0 = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_complemento_cce_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_complemento_cce_linea_complemento_cce_complemento_cce_id",
                        column: x => x.complemento_cce_id,
                        principalSchema: "facturacion",
                        principalTable: "complemento_cce",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_complemento_cce_factura",
                schema: "facturacion",
                table: "complemento_cce",
                column: "factura_venta_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_complemento_cce_linea_complemento_cce_id",
                schema: "facturacion",
                table: "complemento_cce_linea",
                column: "complemento_cce_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complemento_cce_linea",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "complemento_cce",
                schema: "facturacion");
        }
    }
}
