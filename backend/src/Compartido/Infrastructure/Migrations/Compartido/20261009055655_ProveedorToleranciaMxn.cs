using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ProveedorToleranciaMxn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tolerancia_factura_contra_oc_mxn",
                schema: "compartido",
                table: "proveedores",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "parametros_globales",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "descripcion", "modulo", "tipo", "updated_at", "updated_by", "valor", "version" },
                values: new object[] { new Guid("00000006-0001-0000-0000-000000000005"), "cxp.tolerancia-factura-contra-oc-mxn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, "Tolerancia factura contra OC (MXN) cuando el proveedor no tiene una propia. Sin opción de forzar el rechazo.", "cxp", (short)1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", "0.99", 1 });

            migrationBuilder.AddCheckConstraint(
                name: "ck_proveedores_tolerancia_no_negativa",
                schema: "compartido",
                table: "proveedores",
                sql: "tolerancia_factura_contra_oc_mxn IS NULL OR tolerancia_factura_contra_oc_mxn >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proveedores_tolerancia_no_negativa",
                schema: "compartido",
                table: "proveedores");

            migrationBuilder.DeleteData(
                schema: "compartido",
                table: "parametros_globales",
                keyColumn: "id",
                keyValue: new Guid("00000006-0001-0000-0000-000000000005"));

            migrationBuilder.DropColumn(
                name: "tolerancia_factura_contra_oc_mxn",
                schema: "compartido",
                table: "proveedores");
        }
    }
}
