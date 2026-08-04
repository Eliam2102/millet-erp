using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ClientesYProductoAw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    razon_social = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    regimen_fiscal = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    codigo_postal_fiscal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    uso_cfdi_default = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    forma_pago_default = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    metodo_pago_default = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    moneda_default = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    es_generico = table.Column<bool>(type: "boolean", nullable: false),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefono = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.CheckConstraint("ck_clientes_cp_5", "codigo_postal_fiscal IS NULL OR char_length(codigo_postal_fiscal) = 5");
                    table.CheckConstraint("ck_clientes_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_clientes_metodo_pago", "metodo_pago_default IS NULL OR metodo_pago_default IN ('PUE','PPD')");
                    table.CheckConstraint("ck_clientes_moneda_iso", "moneda_default ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_clientes_origen", "origen BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_clientes_regimen_3", "regimen_fiscal IS NULL OR char_length(regimen_fiscal) = 3");
                    table.CheckConstraint("ck_clientes_rfc_longitud", "rfc IS NULL OR char_length(rfc) BETWEEN 12 AND 13");
                });

            migrationBuilder.CreateTable(
                name: "producto_aw",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia_externa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unidad_medida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clave_prod_serv_sat = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    clave_unidad_sat = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    objeto_imp = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    tasa_iva_traslado = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    tasa_retencion_iva = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    tasa_retencion_isr = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    origen = table.Column<short>(type: "smallint", nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_producto_aw", x => x.id);
                    table.CheckConstraint("ck_producto_aw_clave_prodserv_8", "clave_prod_serv_sat IS NULL OR char_length(clave_prod_serv_sat) = 8");
                    table.CheckConstraint("ck_producto_aw_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_producto_aw_objeto_imp", "objeto_imp IS NULL OR objeto_imp IN ('01','02','03')");
                    table.CheckConstraint("ck_producto_aw_origen", "origen BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_producto_aw_tasas", "(tasa_iva_traslado IS NULL OR tasa_iva_traslado BETWEEN 0 AND 1) AND (tasa_retencion_iva IS NULL OR tasa_retencion_iva BETWEEN 0 AND 1) AND (tasa_retencion_isr IS NULL OR tasa_retencion_isr BETWEEN 0 AND 1)");
                    table.ForeignKey(
                        name: "fk_producto_aw_categorias_articulo_categoria_id",
                        column: x => x.categoria_id,
                        principalSchema: "compartido",
                        principalTable: "categorias_articulo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_producto_aw_unidades_medida_unidad_medida_id",
                        column: x => x.unidad_medida_id,
                        principalSchema: "compartido",
                        principalTable: "unidades_medida",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_clave",
                schema: "compartido",
                table: "clientes",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_estatus",
                schema: "compartido",
                table: "clientes",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_referencia_externa",
                schema: "compartido",
                table: "clientes",
                column: "referencia_externa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_rfc",
                schema: "compartido",
                table: "clientes",
                column: "rfc");

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_categoria_id",
                schema: "compartido",
                table: "producto_aw",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_estatus",
                schema: "compartido",
                table: "producto_aw",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_referencia_externa",
                schema: "compartido",
                table: "producto_aw",
                column: "referencia_externa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_producto_aw_unidad_medida_id",
                schema: "compartido",
                table: "producto_aw",
                column: "unidad_medida_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "producto_aw",
                schema: "compartido");
        }
    }
}
