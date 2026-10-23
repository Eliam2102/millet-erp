using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MotivosRevisionTextosLaura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000001"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Diferencias distintas al monto: artículo, cantidad, condiciones; las diferencias de monto las resuelve la tolerancia", "Diferencia con la OC (no de monto)" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000003"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Precio de factura diferente al acordado; solo aplica a facturas sin OC", "Diferencia de precio (solo sin OC)" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000b"),
                column: "descripcion",
                value: "Disputa con proveedor atendida por Dirección de Finanzas (15 días hábiles)");

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000c"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Autorización formal pendiente; una OC autorizada ya autoriza su factura", "Falta de firma (solo sin OC y gastos aduanales)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000001"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Monto factura supera tolerancia del proveedor", "Discrepancia con OC fuera de tolerancia" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000003"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Precio de factura diferente al acordado", "Diferencia de precio" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000b"),
                column: "descripcion",
                value: "Reclamación legal");

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000c"),
                columns: new[] { "descripcion", "nombre" },
                values: new object[] { "Autorización formal pendiente", "Pendiente firma o autorización" });
        }
    }
}
