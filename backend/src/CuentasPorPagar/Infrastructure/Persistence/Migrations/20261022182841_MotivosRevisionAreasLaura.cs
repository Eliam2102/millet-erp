using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MotivosRevisionAreasLaura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "area_avisada_codigo",
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000001"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000002"),
                columns: new[] { "area_avisada_codigo", "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { null, "COMPRAS", "Compras gestiona la nota de crédito por daño y la entrega a Cuentas por pagar" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000003"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000004"),
                columns: new[] { "area_avisada_codigo", "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { null, "COMPRAS", "Compras gestiona la nota de crédito; Almacén registra la salida por devolución" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000005"),
                columns: new[] { "area_avisada_codigo", "descripcion" },
                values: new object[] { "COMPRAS", "El área solicitante valida; si no está satisfecha, avisa a Compras" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000006"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000007"),
                columns: new[] { "area_avisada_codigo", "descripcion" },
                values: new object[] { "COMPRAS", "El área solicitante valida la calidad; si no está satisfecha, avisa a Compras" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000008"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000009"),
                columns: new[] { "area_avisada_codigo", "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { null, "COMPRAS", "Compras solicita el complemento de pago al proveedor y lo envía a Tesorería" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000a"),
                columns: new[] { "area_avisada_codigo", "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { null, "COMPRAS", "Cuentas por pagar detecta datos fiscales incorrectos; Compras gestiona la corrección con el proveedor" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000b"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000c"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000d"),
                column: "area_avisada_codigo",
                value: null);

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000e"),
                column: "area_avisada_codigo",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "area_avisada_codigo",
                schema: "cuentas_por_pagar",
                table: "motivos_revision");

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000002"),
                columns: new[] { "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { "ALMACEN", "Recepción reporta daño físico vía OcRecepcionRegistradaEvent" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000004"),
                columns: new[] { "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { "ALMACEN", "Devolución a proveedor abierta sin liquidar (Almacén sub-flujo 8.B)" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000005"),
                column: "descripcion",
                value: "Servicio no cumple especificación");

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000007"),
                column: "descripcion",
                value: "Servicio recibido no satisfactorio");

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-000000000009"),
                columns: new[] { "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { "CXP", "REPP no emitido pasados 5 días del pago" });

            migrationBuilder.UpdateData(
                schema: "cuentas_por_pagar",
                table: "motivos_revision",
                keyColumn: "id",
                keyValue: new Guid("00000007-1001-0000-0000-00000000000a"),
                columns: new[] { "dependencia_revisora_default_codigo", "descripcion" },
                values: new object[] { "CXP", "CFDI con datos inválidos, RFC mal, fechas inconsistentes" });
        }
    }
}
