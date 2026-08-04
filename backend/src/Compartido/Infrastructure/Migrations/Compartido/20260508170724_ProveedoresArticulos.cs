using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedoresArticulos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "articulos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave_legacy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    descripcion_larga = table.Column<string>(type: "text", nullable: true),
                    unidad_medida_default = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    naturaleza = table.Column<short>(type: "smallint", nullable: false),
                    categoria = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    precio_referencia_monto = table.Column<decimal>(type: "numeric(15,4)", precision: 15, scale: 4, nullable: true),
                    precio_referencia_moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
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
                    table.PrimaryKey("pk_articulos", x => x.id);
                    table.CheckConstraint("ck_articulos_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_articulos_naturaleza", "naturaleza BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_articulos_precio_moneda_iso", "precio_referencia_moneda IS NULL OR precio_referencia_moneda ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_articulos_precio_no_negativo", "precio_referencia_monto IS NULL OR precio_referencia_monto >= 0");
                });

            migrationBuilder.CreateTable(
                name: "proveedores",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave_legacy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    razon_social = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    nombre_comercial = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    tipo_persona = table.Column<short>(type: "smallint", nullable: false),
                    condiciones_pago_dias = table.Column<short>(type: "smallint", nullable: true),
                    moneda_preferida_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_proveedores", x => x.id);
                    table.CheckConstraint("ck_proveedores_condiciones_pago", "condiciones_pago_dias IS NULL OR condiciones_pago_dias BETWEEN 0 AND 365");
                    table.CheckConstraint("ck_proveedores_estatus", "estatus BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_proveedores_rfc_longitud", "char_length(rfc) BETWEEN 12 AND 13");
                    table.CheckConstraint("ck_proveedores_tipo_persona", "tipo_persona BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_articulos_clave",
                schema: "compartido",
                table: "articulos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_articulos_estatus",
                schema: "compartido",
                table: "articulos",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_articulos_naturaleza",
                schema: "compartido",
                table: "articulos",
                column: "naturaleza");

            migrationBuilder.CreateIndex(
                name: "ix_proveedores_clave",
                schema: "compartido",
                table: "proveedores",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proveedores_estatus",
                schema: "compartido",
                table: "proveedores",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_proveedores_rfc",
                schema: "compartido",
                table: "proveedores",
                column: "rfc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "articulos",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "proveedores",
                schema: "compartido");
        }
    }
}
